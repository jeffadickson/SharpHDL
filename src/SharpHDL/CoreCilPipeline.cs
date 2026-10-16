using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Text;
using SharpHDL.Core;
using SharpHDL.HLS;

namespace SharpHDL
{
    public static partial class CilCompiler
    {
        /// <summary>
        /// Production CIL Read/Write frontend to canonical Core IR and
        /// independently compiled ready/valid pipeline. This is opt-in
        /// through PipelineOptions.UseCoreCompiler while equivalence is gated.
        /// </summary>
        private static PipelineHandle AttachCorePipeline(Module m,string name,
            MethodInfo method,HwChannel input,HwChannel output,Expr value,
            PipelineOptions options,MultiplierImplementation multiplier)
        {
            var o=options??new PipelineOptions();
            if(o.LogicLevelsPerStage<1||o.LogicLevelsPerStage>32||
                o.MaxStages<1||o.MaxStages>256||o.InitiationInterval!=1)
                throw new ArgumentException("Core HLS requires logic levels 1..32, max stages 1..256 and II=1.");
            if(multiplier==MultiplierImplementation.DspPreferred)
                throw new NotSupportedException("Core HLS cannot preserve vendor DSP hints yet. Select Operator or use the legacy pipeline.");
            var lowered=CoreTokenLowering.Lower(value,input.Data);
            // Abstract operation weights reproduce one logic level per
            // combinational operation. They are not physical FPGA delays.
            var costs=new HlsCostProfile
            {
                MultiplyCost=1,DspMultiplyCost=1,
                ArithmeticCost=1,MemoryReadCost=1,LogicCost=1
            };
            var plan=HlsGraphAnalyzer.Analyze(lowered.Graph,o.LogicLevelsPerStage,costs);
            var compiled=HlsElasticCompiler.Compile(lowered.Graph,
                lowered.InputId,lowered.OutputId,o.LogicLevelsPerStage,costs,name);
            if(compiled.Latency>o.MaxStages)
                throw new NotSupportedException("Core HLS needs "+compiled.Latency+
                    " stages; maximum is "+o.MaxStages+".");
            // All validation occurs before allocating any Module signals.
            var valids=CorePipelineAttach.Attach(m,input,output,compiled,name);
            var schedule=plan.Nodes.ToDictionary(n=>n.NodeId);
            var nodes=lowered.Graph.Nodes.Select(n=>new PipelineGraphNode(
                n.Id,schedule[n.Id].Stage,schedule[n.Id].ArrivalCost,
                n.Type.Width,n.Operation.ToString(),n.Name??n.Operation.ToString())).ToArray();
            var edges=lowered.Graph.Nodes.SelectMany(n=>
                n.Operands.Select((operand,i)=>new PipelineGraphEdge(operand,n.Id,i))).ToArray();
            var graph=new PipelineDataflowGraph(nodes,edges,
                compiled.Latency,compiled.InitiationInterval,compiled.PayloadRegisterBits);
            var operations=lowered.Graph.Nodes
                .Where(n=>schedule[n.Id].OperatorCost>0)
                .Select(n=>new PipelineOperation(schedule[n.Id].Stage,
                    schedule[n.Id].ArrivalCost,n.Type.Width,
                    n.Operation.ToString(),n.Name??n.Operation.ToString())).ToArray();
            var report=new StringBuilder();
            report.AppendLine("Canonical Core IR elastic HLS pipeline "+name);
            report.AppendLine("CIL frontend: "+method.DeclaringType?.FullName+"."+method.Name);
            report.AppendLine("Latency: "+compiled.Latency+" register boundaries; stalls extend latency.");
            report.AppendLine("Initiation interval: 1 with ready sink; token capacity: "+compiled.Latency+".");
            report.AppendLine("Payload register bits: "+compiled.PayloadRegisterBits+".");
            report.AppendLine("Abstract logic budget per stage: "+o.LogicLevelsPerStage+
                "; no physical timing guarantee.");
            report.AppendLine("Multiplier implementation: "+multiplier);
            report.AppendLine("Backend: Core IR compiler; compatibility Module binding; RTL remains independently verifiable.");
            return new PipelineHandle
            {
                Latency=compiled.Latency,
                InitiationInterval=compiled.InitiationInterval,
                PayloadRegisterBits=compiled.PayloadRegisterBits,
                StageValid=Array.AsReadOnly(valids),
                DataflowGraph=graph,
                Operations=Array.AsReadOnly(operations),
                Report=report.ToString()
            };
        }
    }
}
