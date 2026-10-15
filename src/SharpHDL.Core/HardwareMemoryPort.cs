using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SharpHDL.Core;

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

    public HardwareMemoryPort(string name,int width,int addressNode,int writeEnableNode,
        int writeDataNode,IEnumerable<BigInteger> initial)
    {
        if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("Memory name required.",nameof(name));
        if(width is < 1 or > 4096)throw new ArgumentOutOfRangeException(nameof(width));
        if(addressNode<0||writeEnableNode<0||writeDataNode<0)
            throw new ArgumentOutOfRangeException(nameof(addressNode));
        if(initial==null)throw new ArgumentNullException(nameof(initial));
        var cells=initial.ToArray();
        if(cells.Length is < 1 or > 65536)throw new ArgumentOutOfRangeException(nameof(initial));
        var limit=BigInteger.One<<width;
        if(cells.Any(v=>v<0||v>=limit))
            throw new ArgumentException("RAM initialization exceeds word width.",nameof(initial));
        Name=name;Width=width;AddressNode=addressNode;WriteEnableNode=writeEnableNode;
        WriteDataNode=writeDataNode;Initial=Array.AsReadOnly(cells);
    }
}
