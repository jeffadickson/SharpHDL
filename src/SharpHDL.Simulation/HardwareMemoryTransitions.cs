using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using SharpHDL.Core;

namespace SharpHDL.Simulation;

/// <summary>
/// Synchronous RAM transition logic operating on immutable pre-edge snapshots.
/// All write addresses/data/enables come from the same evaluated IR graph.
/// </summary>
public static class HardwareMemoryTransitions
{
    /// <summary>
    /// Return a new RAM snapshot after committing all enabled writes.
    /// Reads in the preceding graph evaluation always see the old snapshot.
    /// Disabled writes preserve cells. Out-of-range addresses do not write.
    /// </summary>
    public static IReadOnlyDictionary<string,IReadOnlyList<BigInteger>> Commit(
        HardwareGraph graph,
        IReadOnlyDictionary<string,HardwareMemoryPort> ports,
        IReadOnlyDictionary<string,IReadOnlyList<BigInteger>> before,
        IReadOnlyDictionary<int,BigInteger> evaluated)
    {
        if(graph==null)throw new ArgumentNullException(nameof(graph));
        if(ports==null)throw new ArgumentNullException(nameof(ports));
        if(before==null)throw new ArgumentNullException(nameof(before));
        if(evaluated==null)throw new ArgumentNullException(nameof(evaluated));
        var nodes=graph.Nodes.ToDictionary(n=>n.Id);
        var result=new Dictionary<string,IReadOnlyList<BigInteger>>(StringComparer.Ordinal);
        foreach(var (name,port) in ports)
        {
            if(name!=port.Name || !before.TryGetValue(name,out var prior) || prior==null ||
                prior.Count!=port.Initial.Count)
                throw new ArgumentException($"Missing or invalid RAM snapshot '{name}'.",nameof(before));
            if(!nodes.TryGetValue(port.AddressNode,out var addressNode) ||
                !nodes.TryGetValue(port.WriteEnableNode,out var enableNode) ||
                !nodes.TryGetValue(port.WriteDataNode,out var dataNode) ||
                enableNode.Type.Width!=1 || dataNode.Type.Width!=port.Width)
                throw new ArgumentException($"Invalid RAM port node widths for '{name}'.",nameof(ports));
            if(!evaluated.TryGetValue(port.AddressNode,out var address) ||
                !evaluated.TryGetValue(port.WriteEnableNode,out var enabled) ||
                !evaluated.TryGetValue(port.WriteDataNode,out var data))
                throw new ArgumentException($"RAM '{name}' control values are missing.",nameof(evaluated));
            if(address<0 || address >= (BigInteger.One<<addressNode.Type.Width) ||
                enabled<0 || enabled>1 || data<0 || data >= (BigInteger.One<<port.Width))
                throw new ArgumentOutOfRangeException(nameof(evaluated),$"RAM '{name}' control value out of range.");
            var cells=prior.ToArray();
            if(enabled!=0 && address<cells.Length)cells[(int)address]=data;
            result.Add(name,Array.AsReadOnly(cells));
        }
        return new ReadOnlyDictionary<string,IReadOnlyList<BigInteger>>(result);
    }
}
