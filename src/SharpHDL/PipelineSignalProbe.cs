using System;
using System.Linq;
using System.Numerics;

namespace SharpHDL
{
    /// <summary>Observe a pipeline via the actual exposed ready/valid signals.</summary>
    public sealed class PipelineSignalProbe
    {
        readonly PipelineHandle handle;
        readonly Expr iv,ir,ov,or_;
        public PipelineSignalProbe(PipelineHandle handle,Expr inputValid,Expr inputReady,Expr outputValid,Expr outputReady)
        {
            this.handle=handle??throw new ArgumentNullException(nameof(handle));
            iv=inputValid??throw new ArgumentNullException(nameof(inputValid));
            ir=inputReady??throw new ArgumentNullException(nameof(inputReady));
            ov=outputValid??throw new ArgumentNullException(nameof(outputValid));
            or_=outputReady??throw new ArgumentNullException(nameof(outputReady));
            if(handle.StageValid==null || handle.StageValid.Count!=handle.Latency)
                throw new ArgumentException("Incomplete stage metadata.");
        }
        public PipelineFlowSample Sample(Simulator sim,long cycle)
        {
            if(sim==null)throw new ArgumentNullException(nameof(sim));
            if(cycle<0)throw new ArgumentOutOfRangeException(nameof(cycle));
            bool Read(Expr e)=>sim.Read(e)!=BigInteger.Zero;
            return new PipelineFlowSample(cycle,Read(iv),Read(ir),Read(ov),Read(or_),handle.StageValid.Select(Read).ToArray());
        }
    }
}
