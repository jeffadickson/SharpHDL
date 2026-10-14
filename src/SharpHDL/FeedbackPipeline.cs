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
        /// <summary>Allow a new input on the output commit edge using completed next state. Default: false.</summary>
        public bool ForwardOnCommit=false;
        /// <summary>Opt in to input-acceptance state updates for proven single-stage recurrences. Requires forwarding.</summary>
        public bool AdvanceStateOnAccept=false;
    }
    public sealed class FeedbackPipelineHandle
    {
        public bool StateAdvancesOnInput { get; internal set; }
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
        /// <summary>Schedule a scalar recurrence with one iteration in flight. State commits with output acceptance unless AdvanceStateOnAccept is explicitly selected.</summary>
        public static FeedbackPipelineHandle AttachFeedbackPipeline<TIn,TOut>(Module m,string name,MethodInfo method,
            HwStream<TIn> input,HwStream<TOut> output,FeedbackPipelineOptions options=null) where TIn:struct where TOut:struct
        {
            if(input==null||input.Data.Owner!=m)throw new ArgumentException("Pipeline input must belong to this module.");
            var o=options??new FeedbackPipelineOptions();
            if(o.LogicLevelsPerStage<1||o.LogicLevelsPerStage>32||o.MaxStages<1||o.MaxStages>256)throw new ArgumentException("Invalid feedback pipeline stage limits.");
            if(o.AdvanceStateOnAccept&&!o.ForwardOnCommit)throw new ArgumentException("AdvanceStateOnAccept requires ForwardOnCommit.");
            var program=CompilePipeline(method,input,output,true);
            if(o.AdvanceStateOnAccept)
            {
                Expr bundle=program.Value;
                foreach(var state in program.NextState)bundle=Expr.Concat(state,bundle);
                if(PipelineScheduler.RequiredStages(bundle,o.LogicLevelsPerStage)!=1)
                    throw new NotSupportedException("AdvanceStateOnAccept requires all output and next-state logic to fit in one stage. Increase LogicLevelsPerStage or use committed-state scheduling.");
                return AttachAcceptedStatePipeline(m,name,input,output,program);
            }
            var states=program.StateIds.Select(id=>m.Register(name+"_state_local"+id,32,program.Initial[id].Literal)).ToArray();
            var stateMap=program.StateIds.Select((id,n)=>new{id,n}).ToDictionary(x=>x.id,x=>x.n);
            var incoming=new PackedPipelineChannel(m,name+"_iteration_input",program.Placeholder.Width);
            var completed=new PackedPipelineChannel(m,name+"_iteration_result",output.Data.Width+32*states.Length);
            var busy=m.Register(name+"_busy",1,0);
            busy.Describe(name+": one dependent iteration in flight; "+(o.ForwardOnCommit?"allows replacement on the commit edge using forwarded next state":"blocks input through the commit edge")+". Reset discards the iteration.");
            var commit=output.Fire;
            Expr admit=o.ForwardOnCommit?(~busy | commit):~busy;
            Expr packedInput=input.Data;var inputLayout=new StringBuilder("Iteration bundle: low "+input.Data.Width+" bits are input data.");
            for(int n=0;n<program.Candidates.Length;n++)
            {
                int id=program.Candidates[n],stateIndex;Expr value=program.Initial[id];
                if(stateMap.TryGetValue(id,out stateIndex))
                {
                    int low=output.Data.Width+32*stateIndex;
                    // The completed bundle is registered. Forward every state field atomically
                    // only when its output transfers, never merely when it becomes valid.
                    value=o.ForwardOnCommit?Select(commit,completed.Data.Slice(low+31,low),states[stateIndex]):(Expr)states[stateIndex];
                }
                packedInput=Expr.Concat(value,packedInput);
                inputLayout.Append(" ["+(input.Data.Width+32*n+31)+":"+(input.Data.Width+32*n)+"] local "+id+" snapshot/constant.");
            }
            if(o.ForwardOnCommit)inputLayout.Append(" State snapshots select completed next-state fields on output acceptance; otherwise committed registers.");
            incoming.Data.Describe(inputLayout.ToString());m.Drive(incoming.Data,packedInput);
            m.Drive(incoming.Valid,input.Valid & admit);m.Drive(input.Ready,incoming.Ready & admit);
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
            var accept=input.Fire;
            // Acceptance wins when commit and replacement happen on the same edge.
            m.Next(busy,Select(accept,K(1,1),Select(commit,K(1,0),busy)));
            var layout=new StringBuilder("Completed iteration: low "+output.Data.Width+" bits are output; upper fields commit together only on output valid AND ready.");
            for(int n=0;n<states.Length;n++)
            {
                int low=output.Data.Width+32*n;
                states[n].Describe(name+": persistent CIL local "+program.StateIds[n]+"; 32-bit evaluation representation; reset="+program.Initial[program.StateIds[n]].Literal+"; commits on accepted output only.");
                m.NextWhen(states[n],commit,completed.Data.Slice(low+31,low));
                layout.Append(" ["+(low+31)+":"+low+"] next local "+program.StateIds[n]+".");
            }
            completed.Data.Describe(layout.ToString());
            int initiationInterval=datapath.Latency+(o.ForwardOnCommit?0:1);
            var report=new StringBuilder("Feedback pipeline "+name+"\n");
            report.AppendLine("External latency: "+datapath.Latency+" clocks to earliest output transfer; stalls extend latency.");
            report.AppendLine("External initiation interval: "+initiationInterval+" clocks with continuously valid input and ready output; capacity: 1 iteration.");
            report.AppendLine(o.ForwardOnCommit
                ?"State commits on output acceptance; a simultaneous input snapshots completed next state. Same-edge forwarding enabled."
                :"State commits on output acceptance; next input accepted no earlier than the following edge. No same-edge forwarding.");
            report.AppendLine("State registers: "+states.Length+" x 32 bits; busy: 1 bit. Reset restores declared initial constants and flushes in-flight output.");
            report.AppendLine("This is single-iteration recurrence scheduling, not modulo scheduling or resource sharing.");
            report.Append(program.Report);report.AppendLine("Internal datapath schedule (its II=1 is gated by the external recurrence controller):");report.Append(datapath.Report);
            return new FeedbackPipelineHandle{Latency=datapath.Latency,InitiationInterval=initiationInterval,Busy=busy,
                StateRegisters=Array.AsReadOnly(states),StageValid=datapath.StageValid,Report=report.ToString()};
        }
        // Eligible CIL has exactly one read/write, pure scalar transitions, and one
        // stage for ALL output/state expressions. The output slot gates acceptance,
        // so no later iteration can pass a stalled result. Reset cancels both.
        static FeedbackPipelineHandle AttachAcceptedStatePipeline(Module m,string name,HwChannel input,HwChannel output,PipelineProgram program)
        {
            var states=program.StateIds.Select(id=>m.Register(name+"_state_local"+id,32,program.Initial[id].Literal)).ToArray();
            var stateMap=program.StateIds.Select((id,n)=>new{id,n}).ToDictionary(x=>x.id,x=>(Expr)states[x.n]);
            Expr packed=input.Data;
            foreach(int id in program.Candidates)packed=Expr.Concat(stateMap.ContainsKey(id)?stateMap[id]:program.Initial[id],packed);
            var copies=new Dictionary<Expr,Expr>();Func<Expr,Expr> replace=null;
            replace=e=>
            {
                if(e==program.Placeholder)return packed;
                if(e.Op=="const")return e;
                Expr cached;if(copies.TryGetValue(e,out cached))return cached;
                var clone=new Expr(e.Width,e.Op,e.Args.Select(replace).ToArray()){Literal=e.Literal,Offset=e.Offset,Annotation=e.Annotation};
                if(e.NameHint!=null)clone.Named(e.NameHint);copies[e]=clone;return clone;
            };
            var valid=m.Register(name+"_output_valid",1,0);
            var data=m.Register(name+"_output_data",output.Data.Width,0);
            valid.Describe(name+": one output slot; also busy. Replacement allowed only when empty or output transfers. Reset flushes slot.");
            data.Describe(name+": captured output value, held during backpressure; source Write ordering preserved. Reset clears data.");
            Expr ready=~valid|output.Ready;
            m.Drive(input.Ready,ready);m.Drive(output.Valid,valid);m.Drive(output.Data,data);
            m.NextWhen(valid,ready,input.Valid);
            m.NextWhen(data,input.Fire,replace(program.Value));
            for(int n=0;n<states.Length;n++)
            {
                states[n].Describe(name+": CIL local "+program.StateIds[n]+" advances on INPUT acceptance; may lead output retirement by one token. Reset restores declared seed. Not committed-output state.");
                m.NextWhen(states[n],input.Fire,replace(program.NextState[n]));
            }
            var report=new StringBuilder("Feedback pipeline "+name+"\n");
            report.AppendLine("External latency: 1 clock; initiation interval: 1 with continuously ready sink; capacity: 1 iteration.");
            report.AppendLine("Optimization: single-stage state advances on INPUT acceptance. No duplicate committed state, next-state bundle registers, busy register, or commit-forwarding muxes.");
            report.AppendLine("Eligibility: scalar pure CIL transitions; exactly one input/output per path; all output and next-state logic fits one stage.");
            report.AppendLine("StateRegisters expose accepted-input state, not output-committed state. Reset flushes output and restores every initial constant.");
            report.AppendLine("State bits before synthesis: "+states.Length*32+"; output bits: "+output.Data.Width+"; valid bits: 1. Ready propagates combinationally.");
            report.Append(program.Report);
            return new FeedbackPipelineHandle{Latency=1,InitiationInterval=1,Busy=valid,StateAdvancesOnInput=true,
                StateRegisters=Array.AsReadOnly(states),StageValid=Array.AsReadOnly(new[]{valid}),Report=report.ToString()};
        }
    }
}
