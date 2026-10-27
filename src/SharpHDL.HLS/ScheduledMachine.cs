using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using SharpHDL.Core;

namespace SharpHDL.HLS;

/// <summary>A typed value used by a scheduled hardware state machine.</summary>
public sealed class ScheduledValue
{
    internal ScheduledValue(int width, HardwareOperation operation,
        IReadOnlyList<ScheduledValue>? operands = null, BigInteger? literal = null,
        string? name = null, object? bindingOwner = null)
    {
        Width=width;Operation=operation;Operands=operands??Array.Empty<ScheduledValue>();
        Literal=literal;Name=name;BindingOwner=bindingOwner;
    }

    /// <summary>Width of the value in bits.</summary>
    public int Width { get; }
    internal HardwareOperation Operation { get; }
    internal IReadOnlyList<ScheduledValue> Operands { get; }
    internal BigInteger? Literal { get; }
    internal string? Name { get; }
    internal object? BindingOwner { get; }

    /// <summary>Create an equality comparison.</summary>
    public ScheduledValue Equal(ScheduledValue other)=>Binary(HardwareOperation.Equal,other,1);
    /// <summary>Create an unsigned less-than comparison.</summary>
    public ScheduledValue LessThan(ScheduledValue other)=>Binary(HardwareOperation.LessThan,other,1);
    /// <summary>Compare two's-complement signed operands.</summary>
    public ScheduledValue SignedLessThan(ScheduledValue other)=>Binary(HardwareOperation.SignedLessThan,other,1);
    /// <summary>Create a width-preserving addition.</summary>
    public ScheduledValue Add(ScheduledValue other)=>Binary(HardwareOperation.Add,other,Width);
    /// <summary>Create a width-preserving subtraction.</summary>
    public ScheduledValue Subtract(ScheduledValue other)=>Binary(HardwareOperation.Subtract,other,Width);
    /// <summary>Create a width-preserving multiplication.</summary>
    public ScheduledValue Multiply(ScheduledValue other)=>Binary(HardwareOperation.Multiply,other,Width);
    /// <summary>Create a one-bit logical AND.</summary>
    public ScheduledValue And(ScheduledValue other)=>Binary(HardwareOperation.And,other,Width);
    /// <summary>Create a one-bit logical OR.</summary>
    public ScheduledValue Or(ScheduledValue other)=>Binary(HardwareOperation.Or,other,Width);
    /// <summary>Create a width-preserving bitwise exclusive OR.</summary>
    public ScheduledValue Xor(ScheduledValue other)=>Binary(HardwareOperation.Xor,other,Width);
    /// <summary>Zero-extend or truncate to an explicit width.</summary>
    public ScheduledValue Resize(int width)
    {
        if(width is <1 or >4096)throw new ArgumentOutOfRangeException(nameof(width));
        return width==Width?this:new ScheduledValue(width,HardwareOperation.Resize,new[]{this});
    }
    /// <summary>Create a one-bit logical negation or a bitwise complement.</summary>
    public ScheduledValue Not()=>new(Width,HardwareOperation.Not,new[]{this});

    private ScheduledValue Binary(HardwareOperation operation,ScheduledValue other,int resultWidth)
    {
        ArgumentNullException.ThrowIfNull(other);
        if(Width!=other.Width)throw new ArgumentException("Scheduled operand widths must match.",nameof(other));
        return new ScheduledValue(resultWidth,operation,new[]{this,other});
    }

    /// <summary>Create a condition-controlled value selection.</summary>
    public static ScheduledValue Select(ScheduledValue condition,ScheduledValue whenTrue,ScheduledValue whenFalse)
    {
        ArgumentNullException.ThrowIfNull(condition);ArgumentNullException.ThrowIfNull(whenTrue);
        ArgumentNullException.ThrowIfNull(whenFalse);
        if(condition.Width!=1||whenTrue.Width!=whenFalse.Width)
            throw new ArgumentException("Select requires a one-bit condition and equal branch widths.");
        return new ScheduledValue(whenTrue.Width,HardwareOperation.Select,
            new[]{condition,whenTrue,whenFalse});
    }
}

/// <summary>A state-holding value in a scheduled machine.</summary>
public sealed class ScheduledRegister
{
    internal ScheduledRegister(string name,int width,BigInteger reset,ScheduledValue value)
        {Name=name;Width=width;ResetValue=reset;Value=value;}
    /// <summary>Canonical register name.</summary>
    public string Name { get; }
    /// <summary>Register width in bits.</summary>
    public int Width { get; }
    /// <summary>Synchronous reset value.</summary>
    public BigInteger ResetValue { get; }
    /// <summary>Current pre-edge value.</summary>
    public ScheduledValue Value { get; }
}

/// <summary>A synchronous, read-before-write, single-port scheduled RAM.</summary>
public sealed class ScheduledMemory
{
    internal ScheduledMemory(string name,int width,int depth)
        {Name=name;Width=width;Depth=depth;}
    /// <summary>Canonical memory name.</summary>
    public string Name { get; }
    /// <summary>RAM word width.</summary>
    public int Width { get; }
    /// <summary>RAM depth in words.</summary>
    public int Depth { get; }
}

/// <summary>A named finite-capacity resource used for schedule arbitration.</summary>
public sealed class ScheduledResource
{
    internal ScheduledResource(string name,int capacity){Name=name;Capacity=capacity;}
    /// <summary>Resource name.</summary>
    public string Name { get; }
    /// <summary>Maximum operations admitted in one state.</summary>
    public int Capacity { get; }
}

internal sealed record ScheduledAssignment(ScheduledRegister Target,ScheduledValue Value,ScheduledValue Guard);
internal sealed record ScheduledTransition(string Target,ScheduledValue Guard);
internal sealed record ScheduledMemoryAccess(ScheduledMemory Memory,bool Write,ScheduledValue Address,
    ScheduledValue? Data,ScheduledRegister? ReadTarget,ScheduledValue Guard);

/// <summary>Operations executed while one scheduled state is active.</summary>
public sealed partial class ScheduledState
{
    private readonly ScheduledMachineBuilder owner;
    internal readonly List<ScheduledAssignment> Assignments=new();
    internal readonly List<ScheduledTransition> Transitions=new();
    internal readonly List<ScheduledMemoryAccess> MemoryAccesses=new();
    internal readonly List<ScheduledResource> ResourceUses=new();
    internal ScheduledState(ScheduledMachineBuilder owner,string name,int index)
        {this.owner=owner;Name=name;Index=index;}
    /// <summary>State name.</summary>
    public string Name { get; }
    internal int Index { get; }

    /// <summary>Assign a register on the active edge when guard is true.</summary>
    public ScheduledState Assign(ScheduledRegister target,ScheduledValue value,ScheduledValue guard)
    {
        owner.ValidateOwned(target);owner.ValidateValue(value);owner.ValidateGuard(guard);
        if(target.Width!=value.Width)throw new ArgumentException("Register assignment width mismatch.");
        Assignments.Add(new(target,value,guard));return this;
    }

    /// <summary>Assign a register unconditionally while this state is active.</summary>
    public ScheduledState Assign(ScheduledRegister target,ScheduledValue value)
        =>Assign(target,value,owner.One);

    /// <summary>Transition to another state when guard is true. Earlier transitions have priority.</summary>
    public ScheduledState Transition(string targetState,ScheduledValue guard)
    {
        owner.ValidateGuard(guard);
        if(string.IsNullOrWhiteSpace(targetState))throw new ArgumentException("Target state required.",nameof(targetState));
        Transitions.Add(new(targetState,guard));return this;
    }

    /// <summary>Transition to another state unconditionally.</summary>
    public ScheduledState Transition(string targetState)=>Transition(targetState,owner.One);

    /// <summary>Issue a synchronous RAM read whose data is captured into a register at this edge.</summary>
    public ScheduledState Read(ScheduledMemory memory,ScheduledValue address,
        ScheduledRegister target,ScheduledValue guard)
    {
        owner.ValidateOwned(memory);owner.ValidateOwned(target);owner.ValidateValue(address);owner.ValidateGuard(guard);
        if(target.Width!=memory.Width)throw new ArgumentException("RAM read target width mismatch.");
        MemoryAccesses.Add(new(memory,false,address,null,target,guard));return this;
    }

    /// <summary>Write one RAM word at this edge when guard is true.</summary>
    public ScheduledState Write(ScheduledMemory memory,ScheduledValue address,
        ScheduledValue data,ScheduledValue guard)
    {
        owner.ValidateOwned(memory);owner.ValidateValue(address);owner.ValidateValue(data);owner.ValidateGuard(guard);
        if(data.Width!=memory.Width)throw new ArgumentException("RAM write data width mismatch.");
        MemoryAccesses.Add(new(memory,true,address,data,null,guard));return this;
    }

    /// <summary>Reserve one unit of a named resource in this schedule state.</summary>
    public ScheduledState Use(ScheduledResource resource)
    {
        owner.ValidateOwned(resource);ResourceUses.Add(resource);return this;
    }
}

/// <summary>
/// Canonical Core IR plus the named boundaries required by simulation and RTL.
/// </summary>
public sealed class ScheduledHardwareDesign
{
    internal ScheduledHardwareDesign(string name,HardwareGraph graph,Dictionary<string,int> inputs,
        Dictionary<string,int> outputs,Dictionary<string,int> nextState,
        Dictionary<string,BigInteger> resetValues,Dictionary<string,HardwareMemoryPort> memories)
    {
        Name=name;Graph=graph;
        Inputs=new ReadOnlyDictionary<string,int>(inputs);
        Outputs=new ReadOnlyDictionary<string,int>(outputs);
        NextState=new ReadOnlyDictionary<string,int>(nextState);
        ResetValues=new ReadOnlyDictionary<string,BigInteger>(resetValues);
        Memories=new ReadOnlyDictionary<string,HardwareMemoryPort>(memories);
    }
    /// <summary>Hardware module name.</summary>
    public string Name { get; }
    /// <summary>Validated canonical Core IR.</summary>
    public HardwareGraph Graph { get; }
    /// <summary>External inputs and register state boundaries.</summary>
    public IReadOnlyDictionary<string,int> Inputs { get; }
    /// <summary>Observable outputs.</summary>
    public IReadOnlyDictionary<string,int> Outputs { get; }
    /// <summary>Register next-state expressions.</summary>
    public IReadOnlyDictionary<string,int> NextState { get; }
    /// <summary>Synchronous register reset values.</summary>
    public IReadOnlyDictionary<string,BigInteger> ResetValues { get; }
    /// <summary>Declared synchronous RAM ports.</summary>
    public IReadOnlyDictionary<string,HardwareMemoryPort> Memories { get; }
}

/// <summary>
/// Builds a finite, guarded state machine and lowers it into canonical Core IR.
/// The representation is algorithm-neutral: clients declare values, registers,
/// RAM accesses and transitions without depending on legacy Module or RTL.
/// </summary>
public sealed partial class ScheduledMachineBuilder
{
    private readonly string name;
    private readonly List<ScheduledValue> inputs=new();
    private readonly List<ScheduledRegister> registers=new();
    private readonly List<ScheduledMemory> memories=new();
    private readonly List<ScheduledResource> resources=new();
    private readonly List<ScheduledState> states=new();
    private readonly Dictionary<string,ScheduledValue> outputs=new(StringComparer.Ordinal);
    private readonly HashSet<string> names=new(StringComparer.Ordinal) { "scheduler_state" };
    private bool lowered;

    /// <summary>Create an empty scheduled machine.</summary>
    public ScheduledMachineBuilder(string name)
    {
        if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("Machine name required.",nameof(name));
        this.name=name;Zero=Constant(1,0);One=Constant(1,1);
    }
    internal ScheduledValue Zero { get; }
    internal ScheduledValue One { get; }

    /// <summary>Create an exact-width constant.</summary>
    public ScheduledValue Constant(int width,BigInteger value)
    {
        ValidateWidth(width);
        if(value<0||value>=(BigInteger.One<<width))throw new ArgumentOutOfRangeException(nameof(value));
        return new ScheduledValue(width,HardwareOperation.Constant,literal:value);
    }

    /// <summary>Declare an external input.</summary>
    public ScheduledValue Input(string inputName,int width)
    {
        EnsureMutable();ValidateWidth(width);AddName(inputName);
        var value=new ScheduledValue(width,HardwareOperation.Input,name:inputName);
        inputs.Add(value);return value;
    }

    /// <summary>Declare a synchronously reset register.</summary>
    public ScheduledRegister Register(string registerName,int width,BigInteger resetValue=default)
    {
        EnsureMutable();ValidateWidth(width);
        if(resetValue<0||resetValue>=(BigInteger.One<<width))throw new ArgumentOutOfRangeException(nameof(resetValue));
        AddName(registerName);
        var value=new ScheduledValue(width,HardwareOperation.Input,name:registerName);
        var register=new ScheduledRegister(registerName,width,resetValue,value);
        registers.Add(register);return register;
    }

    /// <summary>Declare a zero-initialized synchronous single-port RAM.</summary>
    public ScheduledMemory Memory(string memoryName,int width,int depth)
    {
        EnsureMutable();ValidateWidth(width);
        if(depth is <1 or >65536)throw new ArgumentOutOfRangeException(nameof(depth));
        AddName(memoryName);
        var memory=new ScheduledMemory(memoryName,width,depth);memories.Add(memory);return memory;
    }

    /// <summary>Declare a finite-capacity scheduled operator or shared service.</summary>
    public ScheduledResource Resource(string resourceName,int capacity=1)
    {
        EnsureMutable();
        if(capacity<1)throw new ArgumentOutOfRangeException(nameof(capacity));
        AddName(resourceName);
        var resource=new ScheduledResource(resourceName,capacity);resources.Add(resource);return resource;
    }

    /// <summary>Add a control state. The first state is the reset state.</summary>
    public ScheduledState State(string stateName)
    {
        EnsureMutable();
        if(stateValue!=null)throw new InvalidOperationException("Declare all states before creating state predicates.");
        if(string.IsNullOrWhiteSpace(stateName)||states.Any(s=>s.Name==stateName))
            throw new ArgumentException("State names must be nonempty and unique.",nameof(stateName));
        var state=new ScheduledState(this,stateName,states.Count);states.Add(state);return state;
    }

    /// <summary>Declare an observable combinational output.</summary>
    public void Output(string outputName,ScheduledValue value)
    {
        EnsureMutable();ValidateValue(value);AddName(outputName);outputs.Add(outputName,value);
    }

    /// <summary>Return a one-bit expression true while the given state is active.</summary>
    public ScheduledValue InState(ScheduledState state)
    {
        if(!states.Contains(state))throw new ArgumentException("Foreign state.",nameof(state));
        int width=StateWidth();
        return StateValue().Equal(Constant(width,state.Index));
    }

    private ScheduledValue? stateValue;
    private ScheduledValue StateValue()=>stateValue??=new ScheduledValue(StateWidth(),HardwareOperation.Input,name:"scheduler_state");
    private int StateWidth(){int width=1;while((1<<width)<Math.Max(1,states.Count))width++;return width;}

    /// <summary>Validate resource use and lower the machine into canonical Core IR. Successful lowering freezes all mutation; validation failure leaves the builder editable.</summary>
    public ScheduledHardwareDesign Lower()
    {
        EnsureMutable();
        if(states.Count==0)throw new InvalidOperationException("A scheduled machine requires at least one state.");

        foreach(var state in states)
        {
            foreach(var transition in state.Transitions)
                if(states.All(s=>s.Name!=transition.Target))
                    throw new InvalidOperationException($"State '{state.Name}' targets unknown state '{transition.Target}'.");
            foreach(var group in state.MemoryAccesses.GroupBy(a=>a.Memory))
                if(group.Count()>1)
                    throw new InvalidOperationException($"State '{state.Name}' oversubscribes single-port RAM '{group.Key.Name}'.");
            foreach(var group in state.ResourceUses.GroupBy(r=>r))
                if(group.Count()>group.Key.Capacity)
                    throw new InvalidOperationException($"State '{state.Name}' oversubscribes resource '{group.Key.Name}'.");
            foreach(var access in state.MemoryAccesses)
            {
                int width=1;while((1<<width)<access.Memory.Depth)width++;
                if(access.Address.Width!=width)
                    throw new InvalidOperationException($"RAM '{access.Memory.Name}' address width must be {width} bits.");
            }
        }
        ValidateArithmeticBindings();
        var arithmeticValues=LowerArithmeticBindings();
        int stateWidth=StateWidth();
        var stateRegister=new ScheduledRegister("scheduler_state",stateWidth,0,StateValue());
        var allRegisters=registers.Concat(new[]{stateRegister}).ToArray();
        var nodes=new List<HardwareNode>();
        var ids=new Dictionary<ScheduledValue,int>(ReferenceEqualityComparer.Instance);
        var inputMap=new Dictionary<string,int>(StringComparer.Ordinal);
        var outputMap=new Dictionary<string,int>(StringComparer.Ordinal);
        var nextMap=new Dictionary<string,int>(StringComparer.Ordinal);
        var resetMap=new Dictionary<string,BigInteger>(StringComparer.Ordinal);
        int Compile(ScheduledValue value)
        {
            if(ids.TryGetValue(value,out int existing))return existing;
            if(arithmeticValues.TryGetValue(value,out var routed))
            {
                int routedId=Compile(routed);ids.Add(value,routedId);return routedId;
            }
            int[] operands=value.Operands.Select(Compile).ToArray();int id=nodes.Count;
            nodes.Add(value.Operation==HardwareOperation.MemoryRead
                ? new HardwareNode(id,value.Operation,new HardwareType(value.Width),operands,
                    MemoryName:value.Name)
                : new HardwareNode(id,value.Operation,new HardwareType(value.Width),operands,
                    value.Literal,value.Name));
            ids.Add(value,id);return id;
        }
        foreach(var input in inputs)inputMap.Add(input.Name!,Compile(input));
        foreach(var register in allRegisters)
        {inputMap.Add(register.Name,Compile(register.Value));resetMap.Add(register.Name,register.ResetValue);}
        ScheduledValue Active(ScheduledState state,ScheduledValue guard)=>InState(state).And(guard);
        foreach(var register in allRegisters)
        {
            ScheduledValue next=register.Value;
            if(register==stateRegister)
            {
                foreach(var state in states.AsEnumerable().Reverse())
                    foreach(var transition in state.Transitions.AsEnumerable().Reverse())
                    {
                        int target=states.Single(s=>s.Name==transition.Target).Index;
                        next=ScheduledValue.Select(Active(state,transition.Guard),Constant(stateWidth,target),next);
                    }
            }
            else
            {
                foreach(var state in states.AsEnumerable().Reverse())
                {
                    foreach(var assignment in state.Assignments.Where(a=>a.Target==register).Reverse())
                        next=ScheduledValue.Select(Active(state,assignment.Guard),assignment.Value,next);
                    foreach(var access in state.MemoryAccesses.Where(a=>a.ReadTarget==register).Reverse())
                    {
                        var read=new ScheduledValue(access.Memory.Width,HardwareOperation.MemoryRead,
                            new[]{access.Address},name:access.Memory.Name);
                        next=ScheduledValue.Select(Active(state,access.Guard),read,next);
                    }
                }
            }
            nextMap.Add(register.Name,Compile(next));
        }
        foreach(var (outputName,value) in outputs)outputMap.Add(outputName,Compile(value));
        var memoryMap=new Dictionary<string,HardwareMemoryPort>(StringComparer.Ordinal);
        foreach(var memory in memories)
        {
            int addressWidth=1;while((1<<addressWidth)<memory.Depth)addressWidth++;
            ScheduledValue address=Constant(addressWidth,0),data=Constant(memory.Width,0),enable=Zero;
            foreach(var state in states.AsEnumerable().Reverse())
                foreach(var access in state.MemoryAccesses.Where(a=>a.Memory==memory&&a.Write).Reverse())
                {
                    var active=Active(state,access.Guard);
                    if(access.Address.Width!=addressWidth)
                        throw new InvalidOperationException($"RAM '{memory.Name}' address width must be {addressWidth} bits.");
                    address=ScheduledValue.Select(active,access.Address,address);
                    data=ScheduledValue.Select(active,access.Data!,data);
                    enable=active.Or(enable);
                }
            memoryMap.Add(memory.Name,new HardwareMemoryPort(memory.Name,memory.Width,
                Compile(address),Compile(enable),Compile(data),Enumerable.Repeat(BigInteger.Zero,memory.Depth)));
        }
        var design=new ScheduledHardwareDesign(name,new HardwareGraph(nodes,inputMap.Values.Where(id=>
            resetMap.ContainsKey(inputMap.Single(p=>p.Value==id).Key))),inputMap,outputMap,nextMap,resetMap,memoryMap);
        lowered=true;
        return design;
    }

    internal void ValidateOwned(ScheduledRegister register)
    {EnsureMutable();if(!registers.Contains(register))throw new ArgumentException("Foreign register.",nameof(register));}
    internal void ValidateOwned(ScheduledMemory memory)
    {EnsureMutable();if(!memories.Contains(memory))throw new ArgumentException("Foreign memory.",nameof(memory));}
    internal void ValidateOwned(ScheduledResource resource)
    {EnsureMutable();if(!resources.Contains(resource))throw new ArgumentException("Foreign resource.",nameof(resource));}
    internal void ValidateValue(ScheduledValue value)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        // Constants are portable, but every input leaf must belong to this machine.
        // Walk iteratively so deep expression DAGs cannot exhaust the call stack here.
        var pending=new Stack<ScheduledValue>();
        var visited=new HashSet<ScheduledValue>(ReferenceEqualityComparer.Instance);
        pending.Push(value);
        while(pending.TryPop(out var current))
        {
            if(!visited.Add(current))continue;
            if(current.BindingOwner!=null && !ReferenceEquals(current.BindingOwner,this))
                throw new ArgumentException("Scheduled value contains a foreign arithmetic binding.",nameof(value));
            if(current.Operation==HardwareOperation.Input && current!=stateValue &&
                !inputs.Contains(current) && !registers.Any(r=>r.Value==current))
                throw new ArgumentException("Scheduled value contains a foreign input or register.",nameof(value));
            foreach(var operand in current.Operands)pending.Push(operand);
        }
    }
    internal void ValidateGuard(ScheduledValue guard)
    {ValidateValue(guard);if(guard.Width!=1)throw new ArgumentException("Guard must be one bit.",nameof(guard));}
    private void AddName(string value)
    {if(string.IsNullOrWhiteSpace(value)||!names.Add(value))throw new ArgumentException("Hardware names must be nonempty and unique.");}
    private static void ValidateWidth(int width)
    {if(width is <1 or >4096)throw new ArgumentOutOfRangeException(nameof(width));}
    private void EnsureMutable(){if(lowered)throw new InvalidOperationException("Scheduled machine already lowered.");}
}
