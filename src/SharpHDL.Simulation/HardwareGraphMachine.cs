using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using SharpHDL.Core;

namespace SharpHDL.Simulation;

/// <summary>
/// Clocked two-state Core IR execution engine. Register state and RAM writes
/// commit simultaneously after a pre-edge combinational evaluation.
/// No dependency on legacy Module, HLS or Verilog backend.
/// </summary>
public sealed class HardwareGraphMachine
{
    private readonly HardwareGraph graph;
    private readonly IReadOnlyDictionary<string,int> inputNodes;
    private readonly IReadOnlyDictionary<string,int> outputNodes;
    private readonly IReadOnlyDictionary<string,int> nextState;
    private readonly IReadOnlyDictionary<string,BigInteger> resetValues;
    private readonly IReadOnlyDictionary<string,HardwareMemoryPort> memoryPorts;
    private readonly Dictionary<string,BigInteger> inputValues=new(StringComparer.Ordinal);
    private readonly Dictionary<string,BigInteger> registers=new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string,IReadOnlyList<BigInteger>> memories;

    /// <summary>Number of completed active clock edges since reset.</summary>
    public long Cycle { get; private set; }

    /// <summary>Create an independent clocked machine from validated Core IR and port maps.</summary>
    public HardwareGraphMachine(HardwareGraph graph,
        IReadOnlyDictionary<string,int> inputs,
        IReadOnlyDictionary<string,int> outputs,
        IReadOnlyDictionary<string,int> nextState,
        IReadOnlyDictionary<string,BigInteger> resetValues,
        IReadOnlyDictionary<string,HardwareMemoryPort> memoryPorts)
    {
        this.graph=graph??throw new ArgumentNullException(nameof(graph));
        inputNodes=inputs??throw new ArgumentNullException(nameof(inputs));
        outputNodes=outputs??throw new ArgumentNullException(nameof(outputs));
        this.nextState=nextState??throw new ArgumentNullException(nameof(nextState));
        this.resetValues=resetValues??throw new ArgumentNullException(nameof(resetValues));
        this.memoryPorts=memoryPorts??throw new ArgumentNullException(nameof(memoryPorts));
        var ids=graph.Nodes.ToDictionary(n=>n.Id);
        foreach(var (name,id) in inputNodes)
            if(!ids.TryGetValue(id,out var n)||n.Operation!=HardwareOperation.Input)
                throw new ArgumentException($"Invalid input node '{name}'.",nameof(inputs));
        foreach(var id in outputNodes.Values.Concat(nextState.Values))
            if(!ids.ContainsKey(id))throw new ArgumentException("Unknown output/next-state IR node.");
        foreach(var name in nextState.Keys)
            if(!resetValues.ContainsKey(name)||!inputNodes.ContainsKey(name))
                throw new ArgumentException($"Missing register state boundary '{name}'.");
        foreach(var (name,value) in resetValues)
        {
            if(!inputNodes.TryGetValue(name,out var id) || value<0 ||
                value >= (BigInteger.One<<ids[id].Type.Width))
                throw new ArgumentException($"Invalid reset state '{name}'.");
        }
        memories=new ReadOnlyDictionary<string,IReadOnlyList<BigInteger>>(
            memoryPorts.ToDictionary(p=>p.Key,p=>p.Value.Initial));
        foreach(var name in inputNodes.Keys)
            if(!resetValues.ContainsKey(name))inputValues[name]=0;
        Reset();
    }

    /// <summary>Reset registers without clearing RAM cells, matching hardware semantics.</summary>
    public void Reset()
    {
        registers.Clear();
        foreach(var (name,value) in resetValues)registers[name]=value;
        Cycle=0;
    }

    /// <summary>Drive an external input; state registers cannot be assigned externally.</summary>
    public void SetInput(string name,BigInteger value)
    {
        if(!inputValues.ContainsKey(name))throw new ArgumentException("Unknown or state-only input.",nameof(name));
        var width=graph.Nodes.First(n=>n.Id==inputNodes[name]).Type.Width;
        if(value<0||value >= (BigInteger.One<<width))
            throw new ArgumentOutOfRangeException(nameof(value));
        inputValues[name]=value;
    }

    private IReadOnlyDictionary<int,BigInteger> Evaluate()
    {
        var values=new Dictionary<int,BigInteger>();
        foreach(var (name,id) in inputNodes)
            values[id]=registers.TryGetValue(name,out var state)?state:inputValues[name];
        return HardwareGraphEvaluator.Evaluate(graph,values,memories);
    }

    /// <summary>Read an output using the current pre-edge register/RAM snapshot.</summary>
    public BigInteger ReadOutput(string name)
    {
        if(!outputNodes.TryGetValue(name,out var id))
            throw new ArgumentException("Unknown output.",nameof(name));
        return Evaluate()[id];
    }

    /// <summary>
    /// Evaluate all observable outputs against one current pre-edge snapshot.
    /// The returned dictionary is independent of later input changes and ticks.
    /// This avoids repeating graph evaluation when a testbench observes many ports.
    /// </summary>
    public IReadOnlyDictionary<string,BigInteger> ReadOutputs()
    {
        var values=Evaluate();
        return new ReadOnlyDictionary<string,BigInteger>(outputNodes.ToDictionary(p=>p.Key,p=>values[p.Value],StringComparer.Ordinal));
    }

    /// <summary>Evaluate and atomically commit one rising clock edge.</summary>
    public void Tick()
    {
        var values=Evaluate();
        var next=nextState.ToDictionary(p=>p.Key,p=>values[p.Value],StringComparer.Ordinal);
        var nextMemories=HardwareMemoryTransitions.Commit(graph,memoryPorts,memories,values);
        registers.Clear();
        foreach(var (name,value) in next)registers[name]=value;
        memories=nextMemories;
        Cycle++;
    }

    /// <summary>Read the current value of a register by its canonical name.</summary>
    public BigInteger ReadRegister(string name)
        =>registers.TryGetValue(name,out var value)?value:
            throw new ArgumentException("Unknown register.",nameof(name));
}
