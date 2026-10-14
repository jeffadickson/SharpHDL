using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;

namespace SharpHDL
{
    public sealed class PipelineOptions
    {
        // Abstract combinational graph depth, NOT nanoseconds or a device timing estimate.
        public int LogicLevelsPerStage = 1;
        public int MaxStages = 64;
        public int InitiationInterval = 1;
    }
    /// <summary>Operation-level abstract schedule metadata, not placed-and-routed timing.</summary>
    public sealed class PipelineOperation
    {
        public int Stage { get; }
        public int LogicLevel { get; }
        public int Width { get; }
        public string Operation { get; }
        public string Name { get; }
        public PipelineOperation(int stage, int logicLevel, int width, string operation, string name)
        { Stage=stage; LogicLevel=logicLevel; Width=width; Operation=operation; Name=name; }
    }
    public sealed class PipelineHandle
    {
        /// <summary>Register boundaries, including the edge accepting an input. Stalls extend latency.</summary>
        public int Latency { get; internal set; }
        public int InitiationInterval { get; internal set; }
        public int PayloadRegisterBits { get; internal set; }
        public IReadOnlyList<Signal> StageValid { get; internal set; }
        public string Report { get; internal set; }
        public IReadOnlyList<PipelineOperation> Operations { get; internal set; }
    }
    internal static class PipelineScheduler
    {
        internal static int OperationCost(Expr e)
        {
            switch(e.Op)
            {
                case "const":case "signal":case "resize":case "signextend":case "slice":case "concat":case "<<":case ">>":case "asr":return 0;
                case "+":case "-":case "*":case "&":case "|":case "^":case "~":case "==":case "<":case "slt":case "mux":return 1;
                default:throw new NotSupportedException("Unsupported scheduled expression: "+e.Op);
            }
        }
        // Dry-run eligibility check, before allocating any signals.
        internal static int RequiredStages(Expr value,int levelsPerStage)
        {
            var levels=new Dictionary<Expr,int>();
            Func<Expr,int> visit=null;
            visit=e=>
            {
                int level;if(levels.TryGetValue(e,out level))return level;
                if(levels.Count>=4096)throw new NotSupportedException("Pipeline exceeds 4096 expression nodes.");
                level=(e.Args.Length==0?0:e.Args.Max(visit))+OperationCost(e);
                levels[e]=level;return level;
            };
            return Math.Max(1,(visit(value)+levelsPerStage-1)/levelsPerStage);
        }
        internal static PipelineHandle Attach(Module m,string name,HwChannel input,HwChannel output,Expr value,PipelineOptions options)
        {
            var o=options??new PipelineOptions();
            if(o.LogicLevelsPerStage<1||o.LogicLevelsPerStage>32||o.MaxStages<1||o.MaxStages>256||o.InitiationInterval!=1)
                throw new ArgumentException("Pipeline requires logic levels 1..32, max stages 1..256, and initiation interval 1.");
            var levels=new Dictionary<Expr,int>();var costs=new Dictionary<Expr,int>();var nodes=new List<Expr>();
            Func<Expr,int> visit=null;
            visit=e=>
            {
                int prior;if(levels.TryGetValue(e,out prior))return prior;
                if(nodes.Count>=4096)throw new NotSupportedException("Pipeline exceeds 4096 expression nodes.");
                if(e is Signal && e!=input.Data)throw new NotSupportedException("Pipeline expressions may only depend on the current input token.");
                int cost=OperationCost(e);
                int level=e.Args.Length==0?0:e.Args.Max(a=>visit(a));level+=cost;
                levels[e]=level;costs[e]=cost;nodes.Add(e);return level;
            };
            int depth=Math.Max(1,(visit(value)+o.LogicLevelsPerStage-1)/o.LogicLevelsPerStage);
            if(depth>o.MaxStages)throw new NotSupportedException("Pipeline needs "+depth+" stages; configured maximum is "+o.MaxStages+".");
            Func<Expr,int> stage=e=>(levels[e]+o.LogicLevelsPerStage-1)/o.LogicLevelsPerStage;
            var valid=new Signal[depth];var ready=new Expr[depth];
            for(int i=0;i<depth;i++)
            {
                valid[i]=m.Register(name+"_stage"+(i+1)+"_valid",1,0);
                valid[i].Describe(name+": stage "+(i+1)+" holds one token; synchronous reset empties every stage.");
            }
            for(int i=depth-1;i>=0;i--)ready[i]=~valid[i]|(i+1==depth?output.Ready:ready[i+1]);
            for(int i=0;i<depth;i++)m.NextWhen(valid[i],ready[i],i==0?input.Valid:valid[i-1]);
            m.Drive(input.Ready,ready[0]);m.Drive(output.Valid,valid[depth-1]);
            var registers=new Dictionary<Tuple<Expr,int>,Expr>();var combinations=new Dictionary<Tuple<Expr,int>,Expr>();int bits=0,index=0;
            Func<Expr,int,Expr> at=null,combine=null;
            combine=(e,s)=>
            {
                if(e.Op=="const"||e==input.Data)return e;
                var key=Tuple.Create(e,s);Expr cached;if(combinations.TryGetValue(key,out cached))return cached;
                var args=e.Args.Select(a=>stage(a)==s?combine(a,s):at(a,Math.Max(0,s-1))).ToArray();
                var result=new Expr(e.Width,e.Op,args){Literal=e.Literal,Offset=e.Offset};
                if(e.NameHint!=null)result.Named(name+"_stage"+s+"_"+e.NameHint);
                result.Describe((e.Description??("Operation "+e.Op))+" Scheduled in "+name+" stage "+s+".");
                combinations[key]=result;return result;
            };
            at=(e,s)=>
            {
                if(e.Op=="const")return e;
                if(s==0)return combine(e,0);
                var key=Tuple.Create(e,s);Expr cached;if(registers.TryGetValue(key,out cached))return cached;
                Expr source=stage(e)==s?combine(e,s):at(e,s-1);
                var r=m.Register(name+"_stage"+s+"_value"+(index++),e.Width,0);
                r.Describe(name+": "+e.Width+"-bit value after stage "+s+"; "+(e.NameHint??e.Op)+"; aligned with this stage's token and held during stalls.");
                m.NextWhen(r,ready[s-1] & (s==1?input.Valid:valid[s-2]),source);
                bits+=r.Width;registers[key]=r;return r;
            };
            // Even a constant/identity transform has a valid register and one-token capacity.
            m.Drive(output.Data,at(value,depth));
            var report=new StringBuilder();
            report.AppendLine("Elastic pipeline "+name);
            report.AppendLine("Latency: "+depth+" register boundaries (acceptance edge counts as boundary 1); stalls extend latency.");
            report.AppendLine("Initiation interval: 1 with a continuously ready sink; capacity: "+depth+" tokens.");
            report.AppendLine("Logic levels per stage: "+o.LogicLevelsPerStage+"; abstract operation count, not device timing.");
            report.AppendLine("Payload register bits: "+bits+"; valid register bits: "+depth+".");
            report.AppendLine("No resource sharing; multiply is a whole combinational operator in one logic level.");
            report.AppendLine("Ready propagates combinationally backward; no automatic skid-buffer insertion.");
            foreach(var e in nodes.Where(e=>costs[e]>0))report.AppendLine("stage "+stage(e)+" | level "+levels[e]+" | "+e.Width+" bits | "+e.Op+" | "+(e.NameHint??"expression"));
            var operations=nodes.Where(e=>costs[e]>0).Select(e=>new PipelineOperation(stage(e), levels[e], e.Width, e.Op, e.NameHint??"expression")).ToArray();
            return new PipelineHandle{Latency=depth,InitiationInterval=1,PayloadRegisterBits=bits,StageValid=Array.AsReadOnly(valid),Operations=Array.AsReadOnly(operations),Report=report.ToString()};
        }
    }
}
