using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SharpHDL.Core;

namespace SharpHDL.HLS;

/// <summary>
/// Abstract combinational scheduling weights. These are NOT device delays.
/// The default multiplier weight is deliberately larger than bitwise logic.
/// </summary>
public sealed class HlsCostProfile
{
    /// <summary>Cost of an arithmetic multiply, in abstract scheduling units.</summary>
    public int MultiplyCost { get; init; } = 4;
    /// <summary>Cost of an unsigned direct-DSP multiply, not a timing guarantee.</summary>
    public int DspMultiplyCost { get; init; } = 2;
    /// <summary>Cost of an adder, subtractor, or comparison.</summary>
    public int ArithmeticCost { get; init; } = 2;
    /// <summary>Abstract cost of ROM lookup or memory address logic; this is not synchronous RAM latency.</summary>
    public int MemoryReadCost { get; init; } = 3;
    /// <summary>Cost of a mux or ordinary bitwise operation.</summary>
    public int LogicCost { get; init; } = 1;

    internal void Validate()
    {
        if(MultiplyCost<1||DspMultiplyCost<1||ArithmeticCost<1||
            MemoryReadCost<1||LogicCost<1)
            throw new ArgumentOutOfRangeException(nameof(HlsCostProfile),
                "All abstract operator costs must be positive.");
    }
}

/// <summary>Stage assignment and abstract logic depth for a canonical IR node.</summary>
public sealed record HlsNodeSchedule(int NodeId,int Stage,int ArrivalCost,int OperatorCost);

/// <summary>
/// Immutable combinational scheduling analysis. No registers are inserted
/// and no clock period or physical FPGA resource use is implied.
/// </summary>
public sealed class HlsGraphSchedule
{
    public IReadOnlyList<HlsNodeSchedule> Nodes { get; }
    public int SuggestedStages { get; }
    public int CriticalPathCost { get; }
    public int MultiplyNodes { get; }
    public int DirectDspNodes { get; }
    internal HlsGraphSchedule(IReadOnlyList<HlsNodeSchedule> nodes,int maxCost,
        int multiplyNodes,int directDspNodes,int budget)
    {
        Nodes=Array.AsReadOnly(nodes.ToArray());
        CriticalPathCost=maxCost;
        SuggestedStages=maxCost==0?0:(maxCost+budget-1)/budget;
        MultiplyNodes=multiplyNodes;
        DirectDspNodes=directDspNodes;
    }
}

/// <summary>
/// First production-IR consumer in the independent HLS assembly.
/// Provides technology-neutral planning before implementing register
/// insertion and ready/valid-aware resource scheduling.
/// </summary>
public static class HlsGraphAnalyzer
{
    /// <summary>
    /// Analyze a validated Core graph with a per-stage abstract cost budget.
    /// Input and constant nodes have zero cost; all other operators are
    /// counted on the longest operand-dependency path. When requireCombinational\n    /// is true, reject stateful nodes rather than treating cost as clock latency.
    /// </summary>
    public static HlsGraphSchedule Analyze(HardwareGraph graph,int stageBudget=4,
        HlsCostProfile? profile=null)
    {
        if(graph==null)throw new ArgumentNullException(nameof(graph));
        if(stageBudget<1)throw new ArgumentOutOfRangeException(nameof(stageBudget));
        profile??=new HlsCostProfile();
        profile.Validate();
        if(requireCombinational && graph.Nodes.Any(n => n.Operation is
            HardwareOperation.Register or HardwareOperation.MemoryRead or
            HardwareOperation.MemoryWrite))
            throw new NotSupportedException("Combinational HLS scheduling cannot assign cycle latency to stateful operations.");
        var arrival=new Dictionary<int,int>();
        var nodes=new List<HlsNodeSchedule>();
        int multiply=0,dsp=0,max=0;
        foreach(var node in graph.Nodes)
        {
            int cost=node.Operation switch
            {
                HardwareOperation.Constant or HardwareOperation.Input => 0,
                HardwareOperation.Multiply => profile.MultiplyCost,
                HardwareOperation.DspMultiplyUnsigned => profile.DspMultiplyCost,
                HardwareOperation.Add or HardwareOperation.Subtract or
                    HardwareOperation.Equal or HardwareOperation.LessThan or
                    HardwareOperation.SignedLessThan => profile.ArithmeticCost,
                HardwareOperation.Lookup => profile.MemoryReadCost,
                // Compatibility reporting only: these weights are not clock latencies.\n                HardwareOperation.MemoryRead => profile.MemoryReadCost,\n                HardwareOperation.Register => 0,
                _ => profile.LogicCost
            };
            if(node.Operation==HardwareOperation.Multiply)multiply++;
            if(node.Operation==HardwareOperation.DspMultiplyUnsigned)dsp++;
            int previous=node.Operands.Count==0?0:node.Operands.Max(id=>arrival[id]);
            int end=checked(previous+cost);
            arrival.Add(node.Id,end);
            max=Math.Max(max,end);
            // A boundary is suggested after each abstract stageBudget units;
            // this is a report, not an inserted physical pipeline register.
            int stage=end==0?0:(end-1)/stageBudget;
            nodes.Add(new HlsNodeSchedule(node.Id,stage,end,cost));
        }
        return new HlsGraphSchedule(nodes,max,multiply,dsp,stageBudget);
    }
}
