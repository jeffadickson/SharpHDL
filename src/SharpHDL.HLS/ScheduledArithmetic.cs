using System;
using System.Collections.Generic;
using System.Linq;
using SharpHDL.Core;

namespace SharpHDL.HLS;

/// <summary>Identical exact-width unsigned arithmetic instances with explicit implementation latency.</summary>
public sealed class ScheduledArithmeticResource
{
    internal ScheduledArithmeticResource(string name, HardwareOperation operation, int width, int capacity,bool iterative=false)
        { Name=name; Operation=operation; Width=width; Capacity=capacity;IsIterative=iterative; }
    /// <summary>Unique hardware resource name.</summary>
    public string Name { get; }
    /// <summary>Compatible operation: addition, subtraction or multiplication.</summary>
    public HardwareOperation Operation { get; }
    /// <summary>Operand and result width; results wrap modulo 2^Width.</summary>
    public int Width { get; }
    /// <summary>Number of operations admitted simultaneously in a control state.</summary>
    public int Capacity { get; }
    /// <summary>True for a real registered shift-add multiplier, admitted only in automatic regions.</summary>
    public bool IsIterative { get; }
    /// <summary>Edges from operand sampling through result commit: Width for shift-add, otherwise one.</summary>
    public int Latency=>IsIterative?Width:1;
}

internal sealed record ScheduledArithmeticBinding(ScheduledArithmeticResource Resource,
    ScheduledValue Left, ScheduledValue Right, ScheduledValue Guard, ScheduledValue Result);

public sealed partial class ScheduledState
{
    internal readonly List<ScheduledArithmeticBinding> ArithmeticBindings=new();

    /// <summary>
    /// Bind one operation to a shared instance. The result is combinational in this
    /// state while guard is true, and zero otherwise. Capture it with Assign to
    /// cross a clock boundary. Bound results cannot feed other bound operations
    /// without an explicit register, preventing resource arbitration cycles.
    /// </summary>
    public ScheduledValue Compute(ScheduledArithmeticResource resource, ScheduledValue left,
        ScheduledValue right, ScheduledValue guard)
    {
        owner.ValidateArithmeticResource(resource);
        if(resource.IsIterative&&Index>=0)throw new InvalidOperationException("Iterative arithmetic requires an automatically scheduled operation block.");
        owner.ValidateValue(left); owner.ValidateValue(right); owner.ValidateGuard(guard);
        if(left.Width!=resource.Width || right.Width!=resource.Width)
            throw new ArgumentException($"Resource '{resource.Name}' requires {resource.Width}-bit operands.");
        if(ContainsBinding(left)||ContainsBinding(right)||ContainsBinding(guard))
            throw new ArgumentException("Bound arithmetic dependencies require an explicit register boundary.");
        var result=new ScheduledValue(resource.Width,resource.Operation,new[]{left,right},bindingOwner:owner);
        ArithmeticBindings.Add(new(resource,left,right,guard,result));
        return result;
    }

    /// <summary>Bind an unconditionally active operation in this state.</summary>
    public ScheduledValue Compute(ScheduledArithmeticResource resource, ScheduledValue left, ScheduledValue right)
        =>Compute(resource,left,right,owner.One);

    private static bool ContainsBinding(ScheduledValue value)
    {
        var pending=new Stack<ScheduledValue>();
        var visited=new HashSet<ScheduledValue>(ReferenceEqualityComparer.Instance);
        pending.Push(value);
        while(pending.TryPop(out var current))
        {
            if(!visited.Add(current))continue;
            if(current.BindingOwner!=null)return true;
            foreach(var operand in current.Operands)pending.Push(operand);
        }
        return false;
    }
}

public sealed partial class ScheduledMachineBuilder
{
    private readonly List<ScheduledArithmeticResource> arithmeticResources=new();

    /// <summary>
    /// Declare identical one-edge combinational operators. No target timing, DSP
    /// mapping or multicycle/iterative operator implementation is implied.
    /// </summary>
    public ScheduledArithmeticResource ArithmeticResource(string resourceName, HardwareOperation operation,
        int width, int capacity=1)
    {
        EnsureMutable(); ValidateWidth(width);
        if(operation is not (HardwareOperation.Add or HardwareOperation.Subtract or HardwareOperation.Multiply))
            throw new ArgumentException("Arithmetic resources support Add, Subtract and Multiply only.",nameof(operation));
        if(capacity<1)throw new ArgumentOutOfRangeException(nameof(capacity));
        AddName(resourceName);
        var resource=new ScheduledArithmeticResource(resourceName,operation,width,capacity);
        arithmeticResources.Add(resource);return resource;
    }

    internal void ValidateArithmeticResource(ScheduledArithmeticResource resource)
    {
        EnsureMutable();
        if(!arithmeticResources.Contains(resource))throw new ArgumentException("Foreign arithmetic resource.",nameof(resource));
    }

    private void ValidateArithmeticBindings()
    {
        if(states.SelectMany(s=>s.ArithmeticBindings).Any(b=>b.Resource.IsIterative))
            throw new InvalidOperationException("Iterative bindings must be expanded into registered phases before lowering.");
        foreach(var state in states)
            foreach(var group in state.ArithmeticBindings.GroupBy(b=>b.Resource))
                if(group.Count()>group.Key.Capacity)
                    throw new InvalidOperationException($"State '{state.Name}' oversubscribes arithmetic resource '{group.Key.Name}' ({group.Count()}/{group.Key.Capacity}).");
    }

    private Dictionary<ScheduledValue,ScheduledValue> LowerArithmeticBindings()
    {
        var replacements=new Dictionary<ScheduledValue,ScheduledValue>(ReferenceEqualityComparer.Instance);
        foreach(var resource in arithmeticResources)
        {
            var uses=states.Select(s=>(State:s,Bindings:s.ArithmeticBindings.Where(b=>b.Resource==resource).ToArray())).ToArray();
            int lanes=uses.Max(u=>u.Bindings.Length);
            // Lane assignment is deterministic declaration order within each state.
            // Distinct states are mutually exclusive; guards do not overbook a lane.
            for(int lane=0;lane<lanes;lane++)
            {
                var zero=Constant(resource.Width,0);
                ScheduledValue left=zero,right=zero;
                foreach(var use in uses.Where(u=>u.Bindings.Length>lane))
                {
                    var binding=use.Bindings[lane];
                    var active=InState(use.State).And(binding.Guard);
                    left=ScheduledValue.Select(active,binding.Left,left);
                    right=ScheduledValue.Select(active,binding.Right,right);
                }
                var shared=new ScheduledValue(resource.Width,resource.Operation,new[]{left,right},name:resource.Name+"_"+lane);
                foreach(var use in uses.Where(u=>u.Bindings.Length>lane))
                {
                    var binding=use.Bindings[lane];
                    replacements.Add(binding.Result,ScheduledValue.Select(InState(use.State).And(binding.Guard),shared,zero));
                }
            }
        }
        return replacements;
    }
}
