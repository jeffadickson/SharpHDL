using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SharpHDL.Core;

/// <summary>Explicit vendor inference requests; hints are not mapped-resource guarantees.</summary>
public enum HardwareMemoryImplementation
{
    /// <summary>Leave storage selection to synthesis.</summary>
    Auto,
    /// <summary>Request Xilinx block RAM.</summary>
    XilinxBlock,
    /// <summary>Request Xilinx distributed RAM.</summary>
    XilinxDistributed,
    /// <summary>Request Intel M10K storage.</summary>
    IntelM10K,
    /// <summary>Request Intel M20K storage.</summary>
    IntelM20K
}

/// <summary>
/// One synchronous single-port RAM's pre-edge write interface.
/// Memory reads are ordinary MemoryRead IR nodes. Writes commit after all
/// outputs and next-state values have been evaluated (read-before-write).
/// </summary>
public sealed class HardwareMemoryPort
{
    public string Name { get; }
    public int Width { get; }
    public int AddressNode { get; }
    public int WriteEnableNode { get; }
    public int WriteDataNode { get; }
    public IReadOnlyList<BigInteger> Initial { get; }
    /// <summary>Explicit target inference policy, independent of simulation behavior.</summary>
    public HardwareMemoryImplementation Implementation { get; }

    /// <summary>Create generic RAM metadata, retaining the original public constructor signature.</summary>
    public HardwareMemoryPort(string name,int width,int addressNode,int writeEnableNode,
        int writeDataNode,IEnumerable<BigInteger> initial)
        :this(name,width,addressNode,writeEnableNode,writeDataNode,initial,HardwareMemoryImplementation.Auto) { }

    /// <summary>Create RAM metadata with an explicit validated inference request.</summary>
    public HardwareMemoryPort(string name,int width,int addressNode,int writeEnableNode,
        int writeDataNode,IEnumerable<BigInteger> initial,HardwareMemoryImplementation implementation)
    {
        if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("Memory name required.",nameof(name));
        if(width is < 1 or > 4096)throw new ArgumentOutOfRangeException(nameof(width));
        if(addressNode<0||writeEnableNode<0||writeDataNode<0)
            throw new ArgumentOutOfRangeException(nameof(addressNode));
        if(initial==null)throw new ArgumentNullException(nameof(initial));
        if(!Enum.IsDefined(implementation))throw new ArgumentOutOfRangeException(nameof(implementation));
        var cells=initial.ToArray();
        if(cells.Length is < 1 or > 65536)throw new ArgumentOutOfRangeException(nameof(initial));
        var limit=BigInteger.One<<width;
        if(cells.Any(v=>v<0||v>=limit))
            throw new ArgumentException("RAM initialization exceeds word width.",nameof(initial));
        Name=name;Width=width;AddressNode=addressNode;WriteEnableNode=writeEnableNode;
        WriteDataNode=writeDataNode;Initial=Array.AsReadOnly(cells);
        Implementation=implementation;
    }
}
