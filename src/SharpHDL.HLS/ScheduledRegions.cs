using System.Collections.ObjectModel;
using System.Numerics;

namespace SharpHDL.HLS;

/// <summary>An atomic source-order operation body, not a control state. Reads sample pre-edge values.</summary>
public sealed class ScheduledOperationBlock
{
    internal ScheduledOperationBlock(ScheduledMachineBuilder owner,string name){Owner=owner;Body=new(owner,name,-1);}
    internal ScheduledMachineBuilder Owner {get;}
    internal ScheduledState Body {get;}
    internal bool Consumed {get;set;}
    private void Mutable(){if(Consumed)throw new InvalidOperationException("Operation body is already scheduled.");}
    /// <summary>Stable identifier used for ordinal arbitration among independent ready operations.</summary>
    public string Name=>Body.Name;
    /// <summary>Actual fixed result latency derived from the declared physical implementation.</summary>
    public int Latency=>Body.ArithmeticBindings.Select(b=>b.Resource.Latency).DefaultIfEmpty(1).Max();
    /// <summary>Assign a register when this operation commits.</summary>
    public ScheduledOperationBlock Assign(ScheduledRegister target,ScheduledValue value){Mutable();Body.Assign(target,value);return this;}
    /// <summary>Issue a synchronous single-port memory read.</summary>
    public ScheduledOperationBlock Read(ScheduledMemory memory,ScheduledValue address,ScheduledRegister target,ScheduledValue guard)
    {Mutable();Body.Read(memory,address,target,guard);return this;}
    /// <summary>Issue a single-port memory write.</summary>
    public ScheduledOperationBlock Write(ScheduledMemory memory,ScheduledValue address,ScheduledValue data,ScheduledValue guard)
    {Mutable();Body.Write(memory,address,data,guard);return this;}
    /// <summary>Bind arithmetic; iterative results must be assigned directly to exactly one register.</summary>
    public ScheduledValue Compute(ScheduledArithmeticResource resource,ScheduledValue left,ScheduledValue right)
    {Mutable();return Body.Compute(resource,left,right);}
    /// <summary>Reserve a declared service, also checked independently during lowering.</summary>
    public ScheduledOperationBlock Use(ScheduledResource resource){Mutable();Body.Use(resource);return this;}
}

internal sealed record RegionEffect(ScheduledOperationBlock Block,HashSet<string> Reads,HashSet<string> Writes,
    IReadOnlyDictionary<string,int> Demands,IReadOnlyList<string> Dependencies);

/// <summary>Canonical fixed-latency region schedule and emitted control states. Dependencies are extracted from actual typed effects.</summary>
public sealed class ScheduledRegionPlan
{
    private readonly RegionEffect[] effects;
    private readonly IReadOnlyDictionary<string,int> capacities;
    internal ScheduledRegionPlan(RegionEffect[] effects,Dictionary<string,int> capacities,Dictionary<string,int> starts,
        ScheduledState[] states,ScheduledState done,Dictionary<string,ScheduledResource> reservations)
    {
        this.effects=effects;this.capacities=new ReadOnlyDictionary<string,int>(capacities);
        Starts=new ReadOnlyDictionary<string,int>(starts);Latencies=new ReadOnlyDictionary<string,int>(effects.ToDictionary(e=>e.Block.Name,e=>e.Block.Latency,StringComparer.Ordinal));
        Dependencies=new ReadOnlyDictionary<string,IReadOnlyList<string>>(effects.ToDictionary(e=>e.Block.Name,e=>e.Dependencies,StringComparer.Ordinal));
        ExecutionStates=Array.AsReadOnly(states);Done=done;ReservationResources=new ReadOnlyDictionary<string,ScheduledResource>(reservations);
    }
    /// <summary>Zero-based first execution edge, enumerated by ordinal operation name.</summary>
    public IReadOnlyDictionary<string,int> Starts {get;}
    /// <summary>Operand-sampling through result-commit edges per operation.</summary>
    public IReadOnlyDictionary<string,int> Latencies {get;}
    /// <summary>Automatically extracted RAW, WAR, WAW and potential memory-alias predecessors.</summary>
    public IReadOnlyDictionary<string,IReadOnlyList<string>> Dependencies {get;}
    /// <summary>Actual execution states before the parked completion state.</summary>
    public IReadOnlyList<ScheduledState> ExecutionStates {get;}
    /// <summary>Number of rising edges required to complete from reset.</summary>
    public int CycleCount=>ExecutionStates.Count;
    /// <summary>Parked completion state; create predicates only after the complete region has been emitted.</summary>
    public ScheduledState Done {get;}
    /// <summary>Physical-resource reservation gates checked again by Lower, including every iterative phase.</summary>
    public IReadOnlyDictionary<string,ScheduledResource> ReservationResources {get;}
    /// <summary>Independently sweep a proposed assignment; reject incomplete, early, overflowing or overbooked intervals.</summary>
    public void ValidateAssignments(IReadOnlyDictionary<string,int> starts)=>Validate(effects,capacities,starts);
    internal static void Validate(RegionEffect[] effects,IReadOnlyDictionary<string,int> capacities,IReadOnlyDictionary<string,int> starts)
    {
        ArgumentNullException.ThrowIfNull(starts);
        var names=starts.Keys.ToHashSet(StringComparer.Ordinal);
        if(starts.Count!=effects.Length||effects.Any(e=>!names.Contains(e.Block.Name)||starts[e.Block.Name]<0||
            (long)starts[e.Block.Name]+e.Block.Latency>4095))throw new ArgumentException("Every operation needs a bounded complete interval.",nameof(starts));
        var byName=effects.ToDictionary(e=>e.Block.Name,StringComparer.Ordinal);
        var occupied=new Dictionary<(int Edge,string Resource),int>();
        foreach(var e in effects)
        {
            if(e.Dependencies.Any(d=>starts[d]+byName[d].Block.Latency>starts[e.Block.Name]))
                throw new ArgumentException("An operation starts before a predecessor commits.",nameof(starts));
            foreach(int phase in Enumerable.Range(0,e.Block.Latency))
                foreach(var demand in e.Demands)
                {
                    int edge=starts[e.Block.Name]+phase;
                    var key=(edge,demand.Key);occupied.TryGetValue(key,out int count);count+=demand.Value;occupied[key]=count;
                    if(count>capacities[demand.Key])throw new ArgumentException($"Resource '{demand.Key}' interval conflicts at edge {edge}.",nameof(starts));
                }
        }
    }
}

public sealed partial class ScheduledMachineBuilder
{
    private readonly List<ScheduledOperationBlock> operationBlocks=new();
    /// <summary>Declare one of at most 128 source-order operation bodies, before any control states or predicates.</summary>
    public ScheduledOperationBlock Operation(string operationName)
    {
        EnsureMutable();
        if(states.Count!=0||stateValue is not null)throw new InvalidOperationException("Declare operation bodies before control states.");
        if(operationBlocks.Count>=128)throw new ArgumentException("Automatic regions support at most 128 operations.");
        AddName(operationName);var block=new ScheduledOperationBlock(this,operationName);operationBlocks.Add(block);return block;
    }
    /// <summary>Declare one real 2..32-bit iterative shift-add multiplier, with Width-edge latency and initiation interval Width.</summary>
    public ScheduledArithmeticResource IterativeMultiplier(string resourceName,int width)
    {
        EnsureMutable();if(width is <2 or >32)throw new ArgumentOutOfRangeException(nameof(width));AddName(resourceName);
        var resource=new ScheduledArithmeticResource(resourceName,Core.HardwareOperation.Multiply,width,1,true);arithmeticResources.Add(resource);return resource;
    }
    private void ValidateOperationRegions()
    {if(operationBlocks.Any(b=>!b.Consumed))throw new InvalidOperationException("Unscheduled operation bodies cannot be silently omitted.");}

    /// <summary>
    /// Extract conservative hazards from actual register/memory effects in the supplied source order.
    /// Shared ScheduledMemory objects alias; distinct literal addresses do not, unknown addresses may.
    /// Allocate non-overlapping resource intervals with ordinal-name priority, then emit real hardware.
    /// Only one straight-line region per builder is admitted; loops/branches and pipelined initiation
    /// intervals are unsupported. Discard the builder after a failed emission.
    /// </summary>
    public ScheduledRegionPlan ScheduleRegion(string prefix,params ScheduledOperationBlock[] program)
    {
        EnsureMutable();ArgumentNullException.ThrowIfNull(program);
        if(string.IsNullOrWhiteSpace(prefix)||!char.IsAsciiLetter(prefix[0])&&prefix[0]!='_'||prefix.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='_'))
            throw new ArgumentException("Region prefix must be an ASCII identifier.",nameof(prefix));
        if(states.Count!=0||stateValue is not null||program.Length is <1 or >128||program.Length!=operationBlocks.Count||
            program.Any(b=>b is null||b.Owner!=this||b.Consumed)||program.Distinct().Count()!=program.Length)
            throw new ArgumentException("Schedule every owned operation exactly once, before control states.",nameof(program));
        var registerNames=new Dictionary<ScheduledValue,string>(ReferenceEqualityComparer.Instance);
        foreach(var register in registers)registerNames.Add(register.Value,register.Name);
        var caps=new Dictionary<string,int>(StringComparer.Ordinal);
        var effects=new List<RegionEffect>();
        foreach(var block in program)
        {
            var body=block.Body;
            if(body.Assignments.Count+body.MemoryAccesses.Count+body.ArithmeticBindings.Count+body.ResourceUses.Count is <1 or >256)
                throw new ArgumentException("Operation bodies require 1..256 effects.");
            var iterative=body.ArithmeticBindings.Where(b=>b.Resource.IsIterative).ToArray();
            if(iterative.Length!=0&&(iterative.Length!=1||body.ArithmeticBindings.Count!=1||body.Assignments.Count!=1||body.MemoryAccesses.Count!=0||
                body.Assignments[0].Value!=iterative[0].Result||body.Assignments[0].Guard.Literal!=BigInteger.One||iterative[0].Guard.Literal!=BigInteger.One))
                throw new NotSupportedException("An iterative block must directly assign its sole unguarded arithmetic result to one register.");
            var reads=new HashSet<string>(StringComparer.Ordinal);var writes=body.Assignments.Select(a=>a.Target.Name).ToHashSet(StringComparer.Ordinal);
            foreach(var access in body.MemoryAccesses.Where(a=>!a.Write))writes.Add(access.ReadTarget!.Name);
            var roots=body.Assignments.SelectMany(a=>new[]{a.Value,a.Guard})
                .Concat(body.MemoryAccesses.SelectMany(a=>a.Data is null?new[]{a.Address,a.Guard}:new[]{a.Address,a.Guard,a.Data}))
                .Concat(body.ArithmeticBindings.SelectMany(a=>new[]{a.Left,a.Right,a.Guard}));
            var pending=new Stack<ScheduledValue>(roots);var visited=new HashSet<ScheduledValue>(ReferenceEqualityComparer.Instance);
            while(pending.TryPop(out var value))
            {
                if(!visited.Add(value))continue;
                if(visited.Count>4096)throw new ArgumentException("Operation expression exceeds the 4096-value analysis budget.");
                if(registerNames.TryGetValue(value,out string? name))reads.Add(name);
                foreach(var operand in value.Operands)pending.Push(operand);
            }
            var demands=new Dictionary<string,int>(StringComparer.Ordinal);
            void Demand(string name,int capacity){caps[name]=capacity;demands.TryGetValue(name,out int count);demands[name]=count+1;}
            foreach(var a in body.MemoryAccesses)Demand(a.Memory.Name,1);
            foreach(var a in body.ArithmeticBindings)Demand(a.Resource.Name,a.Resource.Capacity);
            foreach(var r in body.ResourceUses)Demand(r.Name,r.Capacity);
            if(demands.Values.Sum()>32||demands.Any(p=>p.Value>caps[p.Key]))throw new ArgumentException("Operation overbooks a physical resource or exceeds 32 claims.");
            effects.Add(new(block,reads,writes,new ReadOnlyDictionary<string,int>(demands),Array.Empty<string>()));
        }
        // Every edge points forward in the caller's semantic source order. Name
        // sorting is used only for arbitration, never to infer memory ordering.
        for(int j=0;j<effects.Count;j++)
        {
            var predecessors=new List<string>();
            for(int i=0;i<j;i++)
            {
                var a=effects[i];var b=effects[j];
                bool hazard=a.Writes.Overlaps(b.Reads)||a.Writes.Overlaps(b.Writes)||a.Reads.Overlaps(b.Writes)||
                    a.Block.Body.MemoryAccesses.Any(x=>b.Block.Body.MemoryAccesses.Any(y=>ReferenceEquals(x.Memory,y.Memory)&&(x.Write||y.Write)&&
                        !(x.Address.Literal is BigInteger ax&&y.Address.Literal is BigInteger ay&&ax!=ay)));
                if(hazard)predecessors.Add(a.Block.Name);
            }
            effects[j]=effects[j] with{Dependencies=Array.AsReadOnly(predecessors.Order(StringComparer.Ordinal).ToArray())};
        }
        var ordered=effects.OrderBy(e=>e.Block.Name,StringComparer.Ordinal).ToArray();
        var byName=effects.ToDictionary(e=>e.Block.Name,StringComparer.Ordinal);
        var starts=new Dictionary<string,int>(StringComparer.Ordinal);var used=new Dictionary<(int,string),int>();
        for(int edge=0;edge<4095&&starts.Count<ordered.Length;edge++)foreach(var effect in ordered)
        {
            if(starts.ContainsKey(effect.Block.Name)||effect.Dependencies.Any(d=>!starts.TryGetValue(d,out int start)||start+byName[d].Block.Latency>edge))continue;
            bool fits=(long)edge+effect.Block.Latency<=4095;
            for(int phase=0;fits&&phase<effect.Block.Latency;phase++)foreach(var demand in effect.Demands)
                if(used.GetValueOrDefault((edge+phase,demand.Key))+demand.Value>caps[demand.Key]){fits=false;break;}
            if(!fits)continue;
            starts.Add(effect.Block.Name,edge);
            for(int phase=0;phase<effect.Block.Latency;phase++)foreach(var demand in effect.Demands)
                used[(edge+phase,demand.Key)]=used.GetValueOrDefault((edge+phase,demand.Key))+demand.Value;
        }
        if(starts.Count!=ordered.Length)throw new InvalidOperationException("Region exceeds the 4095-edge execution budget.");
        ScheduledRegionPlan.Validate(ordered,caps,starts);
        int cycles=ordered.Max(e=>starts[e.Block.Name]+e.Block.Latency);
        var gates=caps.OrderBy(p=>p.Key,StringComparer.Ordinal).ToDictionary(p=>p.Key,p=>Resource(prefix+"_busy_"+p.Key,p.Value),StringComparer.Ordinal);
        var buffers=new Dictionary<ScheduledArithmeticResource,(ScheduledRegister A,ScheduledRegister B,ScheduledRegister Acc)>();
        foreach(var resource in program.SelectMany(b=>b.Body.ArithmeticBindings).Select(a=>a.Resource).Where(r=>r.IsIterative).Distinct())
            buffers.Add(resource,(Register(prefix+"_"+resource.Name+"_a",resource.Width),Register(prefix+"_"+resource.Name+"_b",resource.Width),Register(prefix+"_"+resource.Name+"_acc",resource.Width)));
        var steps=buffers.ToDictionary(p=>p.Key,p=>p.Value.Acc.Value.Add(ScheduledValue.Select(
            p.Value.B.Value.And(Constant(p.Key.Width,1)).Equal(Constant(p.Key.Width,1)),p.Value.A.Value,Constant(p.Key.Width,0))));
        var execution=Enumerable.Range(0,cycles).Select(e=>State(prefix+"_"+e)).ToArray();var done=State(prefix+"_done");
        foreach(var effect in ordered)
        {
            int start=starts[effect.Block.Name];var body=effect.Block.Body;
            for(int phase=0;phase<effect.Block.Latency;phase++)foreach(var demand in effect.Demands)
                for(int unit=0;unit<demand.Value;unit++)execution[start+phase].Use(gates[demand.Key]);
            if(effect.Block.Latency==1)
            {
                var state=execution[start];state.Assignments.AddRange(body.Assignments);state.MemoryAccesses.AddRange(body.MemoryAccesses);
                state.ArithmeticBindings.AddRange(body.ArithmeticBindings);state.ResourceUses.AddRange(body.ResourceUses);
            }
            else
            {
                var binding=body.ArithmeticBindings.Single();var target=body.Assignments.Single().Target;var temp=buffers[binding.Resource];
                int width=binding.Resource.Width;var one=Constant(width,1);var zero=Constant(width,0);
                // Sample operands on the first edge, consuming bit zero immediately.
                // Later phases use only shared registered operands/accumulator.
                execution[start].Assign(temp.A,binding.Left.ShiftLeft(1)).Assign(temp.B,binding.Right.ShiftRight(1));
                execution[start].Assign(temp.Acc,ScheduledValue.Select(binding.Right.And(one).Equal(one),binding.Left,zero));
                var step=steps[binding.Resource]; // One physical adder expression shared across invocations and phases.
                for(int phase=1;phase<width;phase++)
                {
                    var state=execution[start+phase];state.Assign(phase==width-1?target:temp.Acc,step);
                    state.Assign(temp.A,temp.A.Value.ShiftLeft(1)).Assign(temp.B,temp.B.Value.ShiftRight(1));
                    foreach(var service in body.ResourceUses)state.Use(service);
                }
                foreach(var service in body.ResourceUses)execution[start].Use(service);
            }
            effect.Block.Consumed=true;
        }
        for(int i=0;i<execution.Length;i++)execution[i].Transition(i+1<execution.Length?execution[i+1].Name:done.Name);
        done.Transition(done.Name);
        return new(ordered,caps,ordered.ToDictionary(e=>e.Block.Name,e=>starts[e.Block.Name],StringComparer.Ordinal),execution,done,gates);
    }
}
