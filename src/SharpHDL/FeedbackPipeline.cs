using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace SharpHDL
{
    public sealed class FeedbackPipelineOptions
    {
        public int LogicLevelsPerStage=1;
        public int MaxStages=64;
    }
    public sealed class FeedbackPipelineHandle
    {
        public int Latency { get; internal set; }
        public int InitiationInterval { get; internal set; }
        public Signal Busy { get; internal set; }
        public IReadOnlyList<Signal> StateRegisters { get; internal set; }
        public IReadOnlyList<Signal> StageValid { get; internal set; }
        public string Report { get; internal set; }
    }
    public static partial class CilCompiler
    {
        sealed class PackedPipelineChannel : HwChannel
        {
            internal PackedPipelineChannel(Module m,string name,int width)
            {Data=m.Wire(name+"_data",width);Valid=m.Wire(name+"_valid",1);Ready=m.Wire(name+"_ready",1);}
        }
        /// <summary>Schedule a scalar recurrence with one iteration in flight. State commits with output acceptance.</summary>
        public static FeedbackPipelineHandle AttachFeedbackPipeline<TIn,TOut>(Module m,string name,MethodInfo method,
            HwStream<TIn> input,HwStream<TOut> output,FeedbackPipelineOptions options=null) where TIn:struct where TOut:struct
        {
            if(input==null||input.Data.Owner!=m)throw new ArgumentException("Pipeline input must belong to this module.");
            var o=options??new FeedbackPipelineOptions();
            if(o.LogicLevelsPerStage<1||o.LogicLevelsPerStage>32||o.MaxStages<1||o.MaxStages>256)throw new ArgumentException("Invalid feedback pipeline stage limits.");
            var program=CompilePipeline(method,input,output,true);
            var states=program.StateIds.Select(id=>m.Register(name+"_state_local"+id,32,program.Initial[id].Literal)).ToArray();
            var stateMap=program.StateIds.Select((id,n)=>new{id,n}).ToDictionary(x=>x.id,x=>states[x.n]);
            var incoming=new PackedPipelineChannel(m,name+"_iteration_input",program.Placeholder.Width);
            var completed=new PackedPipelineChannel(m,name+"_iteration_result",output.Data.Width+32*states.Length);
            var busy=m.Register(name+"_busy",1,0);
            busy.Describe(name+": one dependent iteration in flight; blocks input until output acceptance commits state. Reset discards the iteration.");
            Expr packedInput=input.Data;var inputLayout=new StringBuilder("Iteration bundle: low "+input.Data.Width+" bits are input data.");
            for(int n=0;n<program.Candidates.Length;n++)
            {
                int id=program.Candidates[n];Signal state;Expr value=stateMap.TryGetValue(id,out state)?(Expr)state:program.Initial[id];
                packedInput=Expr.Concat(value,packedInput);
                inputLayout.Append(" ["+(input.Data.Width+32*n+31)+":"+(input.Data.Width+32*n)+"] local "+id+" snapshot/constant.");
            }
            incoming.Data.Describe(inputLayout.ToString());m.Drive(incoming.Data,packedInput);
            m.Drive(incoming.Valid,input.Valid & ~busy);m.Drive(input.Ready,incoming.Ready & ~busy);
            var copies=new Dictionary<Expr,Expr>();
            Func<Expr,Expr> replace=null;
            replace=e=>
            {
                if(e==program.Placeholder)return incoming.Data;
                if(e.Op=="const")return e;
                Expr cached;if(copies.TryGetValue(e,out cached))return cached;
                var clone=new Expr(e.Width,e.Op,e.Args.Select(replace).ToArray()){Literal=e.Literal,Offset=e.Offset,Annotation=e.Annotation};
                if(e.NameHint!=null)clone.Named(e.NameHint);copies[e]=clone;return clone;
            };
            Expr result=program.Value;for(int n=0;n<states.Length;n++)result=Expr.Concat(program.NextState[n],result);
            var datapath=PipelineScheduler.Attach(m,name+"_datapath",incoming,completed,replace(result),
                new PipelineOptions{LogicLevelsPerStage=o.LogicLevelsPerStage,MaxStages=o.MaxStages});
            m.Drive(output.Data,completed.Data.Slice(output.Data.Width-1,0));m.Drive(output.Valid,completed.Valid);m.Drive(completed.Ready,output.Ready);
            var commit=output.Fire;var accept=input.Fire;
            m.Next(busy,Select(commit,K(1,0),Select(accept,K(1,1),busy)));
            var layout=new StringBuilder("Completed iteration: low "+output.Data.Width+" bits are output; upper fields commit together only on output valid AND ready.");
            for(int n=0;n<states.Length;n++)
            {
                int low=output.Data.Width+32*n;
                states[n].Describe(name+": persistent CIL local "+program.StateIds[n]+"; 32-bit evaluation representation; reset="+program.Initial[program.StateIds[n]].Literal+"; commits on accepted output only.");
                m.NextWhen(states[n],commit,completed.Data.Slice(low+31,low));
                layout.Append(" ["+(low+31)+":"+low+"] next local "+program.StateIds[n]+".");
            }
            completed.Data.Describe(layout.ToString());
            var report=new StringBuilder("Feedback pipeline "+name+"\n");
            report.AppendLine("External latency: "+datapath.Latency+" clocks to earliest output transfer; stalls extend latency.");
            report.AppendLine("External initiation interval: "+(datapath.Latency+1)+" clocks with continuously valid input and ready output; capacity: 1 iteration.");
            report.AppendLine("State commits on output acceptance; next input accepted no earlier than the following edge. No same-edge forwarding.");
            report.AppendLine("State registers: "+states.Length+" x 32 bits; busy: 1 bit. Reset restores declared initial constants and flushes in-flight output.");
            report.AppendLine("This is conservative recurrence scheduling, not modulo scheduling or resource sharing.");
            report.Append(program.Report);report.AppendLine("Internal datapath schedule (its II=1 is gated by the external recurrence controller):");report.Append(datapath.Report);
            return new FeedbackPipelineHandle{Latency=datapath.Latency,InitiationInterval=datapath.Latency+1,Busy=busy,
                StateRegisters=Array.AsReadOnly(states),StageValid=datapath.StageValid,Report=report.ToString()};
        }
    }
}
