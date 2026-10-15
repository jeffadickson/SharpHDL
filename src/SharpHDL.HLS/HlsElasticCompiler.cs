using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using SharpHDL.Core;

namespace SharpHDL.HLS;

/// <summary>A clocked ready/valid pipeline represented entirely by canonical Core IR.</summary>
public sealed class HlsElasticPipeline
{
    public HardwareGraph Graph { get; }
    public IReadOnlyDictionary<string,int> Inputs { get; }
    public IReadOnlyDictionary<string,int> Outputs { get; }
    public IReadOnlyDictionary<string,int> NextState { get; }
    public IReadOnlyDictionary<string,BigInteger> ResetValues { get; }
    public int Latency { get; }
    public int InitiationInterval => 1;
    public int PayloadRegisterBits { get; }

    internal HlsElasticPipeline(HardwareGraph graph,Dictionary<string,int> inputs,
        Dictionary<string,int> outputs,Dictionary<string,int> next,
        Dictionary<string,BigInteger> reset,int latency,int bits)
    {
        Graph=graph;
        Inputs=new ReadOnlyDictionary<string,int>(inputs);
        Outputs=new ReadOnlyDictionary<string,int>(outputs);
        NextState=new ReadOnlyDictionary<string,int>(next);
        ResetValues=new ReadOnlyDictionary<string,BigInteger>(reset);
        Latency=latency;
        PayloadRegisterBits=bits;
    }
}

/// <summary>
/// Compile a stateless single-input Core IR expression into real elastic
/// register boundaries. Data and valid hold under downstream backpressure.
/// No legacy Module or Simulation assembly is referenced.
/// </summary>
public static class HlsElasticCompiler
{
    /// <summary>
    /// Create a ready/valid pipeline. Stateful nodes, RAM and direct DSP
    /// primitives are rejected until their timing is explicitly supported.
    /// </summary>
    public static HlsElasticPipeline Compile(HardwareGraph source,int inputNodeId,
        int outputNodeId,int stageBudget=4,HlsCostProfile? profile=null,
        string prefix="hls")
    {
        if(source==null)throw new ArgumentNullException(nameof(source));
        if(string.IsNullOrWhiteSpace(prefix) ||
            !System.Text.RegularExpressions.Regex.IsMatch(prefix,@"^[A-Za-z][A-Za-z0-9_]*$"))
            throw new ArgumentException("Invalid HDL prefix.",nameof(prefix));
        if(stageBudget<1)throw new ArgumentOutOfRangeException(nameof(stageBudget));
        var byId=source.Nodes.ToDictionary(n=>n.Id);
        if(!byId.TryGetValue(inputNodeId,out var token) ||
           token.Operation!=HardwareOperation.Input)
            throw new ArgumentException("Input must identify a Core IR input.",nameof(inputNodeId));
        if(!byId.ContainsKey(outputNodeId))
            throw new ArgumentException("Unknown output node.",nameof(outputNodeId));
        var reachable=new HashSet<int>();
        void Visit(int id)
        {
            if(!reachable.Add(id))return;
            var n=byId[id];
            if(n.Operation==HardwareOperation.Input && id!=inputNodeId)
                throw new NotSupportedException("Only one external input token is supported.");
            if(n.Operation is HardwareOperation.Register or HardwareOperation.MemoryRead or
                HardwareOperation.MemoryWrite or HardwareOperation.DspMultiplyUnsigned)
                throw new NotSupportedException("State, RAM and direct DSP need dedicated scheduling.");
            foreach(var operand in n.Operands)Visit(operand);
        }
        Visit(outputNodeId);
        var plan=HlsGraphAnalyzer.Analyze(source,stageBudget,profile);
        var schedules=plan.Nodes.ToDictionary(n=>n.NodeId);
        foreach(var id in reachable)
            if(schedules[id].OperatorCost>stageBudget)
                throw new NotSupportedException("An operator exceeds the stage budget; internal operator pipelining is not supported.");
        var latency=Math.Max(1,(schedules[outputNodeId].ArrivalCost+stageBudget-1)/stageBudget);
        if(latency>256)throw new NotSupportedException("Pipeline exceeds 256 stages.");
        int Stage(int id)
        {
            var arrival=schedules[id].ArrivalCost;
            return arrival==0?0:(arrival-1)/stageBudget;
        }

        var nodes=new List<HardwareNode>();
        var inputs=new Dictionary<string,int>(StringComparer.Ordinal);
        var outputs=new Dictionary<string,int>(StringComparer.Ordinal);
        var next=new Dictionary<string,int>(StringComparer.Ordinal);
        var reset=new Dictionary<string,BigInteger>(StringComparer.Ordinal);
        var constants=new Dictionary<(int,BigInteger),int>();
        int Add(HardwareOperation op,int width,int[] operands,
            BigInteger? literal=null,string? name=null,int offset=0,
            IReadOnlyList<BigInteger>? table=null)
        {
            int id=nodes.Count;
            nodes.Add(new HardwareNode(id,op,new HardwareType(width),
                operands,literal,name,offset,table));
            return id;
        }
        int Constant(int width,BigInteger value)
        {
            var key=(width,value);
            if(!constants.TryGetValue(key,out var id))
                constants[key]=id=Add(HardwareOperation.Constant,width,Array.Empty<int>(),value);
            return id;
        }
        int Input(string name,int width,bool state=false)
        {
            var id=Add(HardwareOperation.Input,width,Array.Empty<int>(),null,name);
            inputs.Add(name,id);
            if(state)reset.Add(name,BigInteger.Zero);
            return id;
        }
        int Unary(HardwareOperation op,int width,int a)=>Add(op,width,new[]{a});
        int Binary(HardwareOperation op,int width,int a,int b)=>Add(op,width,new[]{a,b});
        int Mux(int cond,int yes,int no,int width)=>
            Add(HardwareOperation.Select,width,new[]{cond,yes,no});
        int one=Constant(1,1);
        int tokenInput=Input(prefix+"_input_data",token.Type.Width);
        int inputValid=Input(prefix+"_input_valid",1);
        int outputReady=Input(prefix+"_output_ready",1);
        var valid=new int[latency];
        for(int i=0;i<latency;i++)
            valid[i]=Input(prefix+"_valid_"+i,1,true);
        var ready=new int[latency];
        for(int i=latency-1;i>=0;i--)
        {
            int downstream=i==latency-1?outputReady:ready[i+1];
            ready[i]=Binary(HardwareOperation.Or,1,
                Unary(HardwareOperation.Not,1,valid[i]),downstream);
        }
        for(int i=0;i<latency;i++)
            next.Add(prefix+"_valid_"+i,
                Mux(ready[i],i==0?inputValid:valid[i-1],valid[i],1));
        outputs.Add(prefix+"_input_ready",ready[0]);
        outputs.Add(prefix+"_output_valid",valid[latency-1]);

        var values=new Dictionary<(int,int),int>();
        var registers=new Dictionary<(int,int),int>();
        int payloadBits=0;
        int At(int id,int stage)
        {
            if(!reachable.Contains(id))throw new InvalidOperationException("Unreachable expression.");
            var n=byId[id];
            if(n.Operation==HardwareOperation.Constant)
                return Constant(n.Type.Width,n.ConstantValue!.Value);
            if(stage>Stage(id))return Register(id,stage);
            if(stage<Stage(id))throw new InvalidOperationException("Expression scheduled too early.");
            if(n.Operation==HardwareOperation.Input)return tokenInput;
            var key=(id,stage);
            if(values.TryGetValue(key,out var cached))return cached;
            var args=n.Operands.Select(a=>At(a,stage)).ToArray();
            var value=Add(n.Operation,n.Type.Width,args,n.ConstantValue,n.Name,n.Offset,n.Table);
            values.Add(key,value);
            return value;
        }
        int Register(int id,int stage)
        {
            if(stage<1||stage>latency)throw new ArgumentOutOfRangeException(nameof(stage));
            var key=(id,stage);
            if(registers.TryGetValue(key,out var cached))return cached;
            var width=byId[id].Type.Width;
            var name=prefix+"_data_"+stage+"_"+id;
            var reg=Input(name,width,true);
            registers.Add(key,reg);
            payloadBits+=width;
            int priorValid=stage==1?inputValid:valid[stage-2];
            int write=Binary(HardwareOperation.And,1,ready[stage-1],priorValid);
            var computed=At(id,stage-1);
            next.Add(name,Mux(write,computed,reg,width));
            return reg;
        }
        outputs.Add(prefix+"_output_data",Register(outputNodeId,latency));
        return new HlsElasticPipeline(new HardwareGraph(nodes),inputs,outputs,next,
            reset,latency,payloadBits);
    }
}
