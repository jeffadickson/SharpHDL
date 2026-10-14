using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SharpHDL
{
    /// <summary>A sampled, pre-edge view of ready/valid pipeline flow. A sample is not a clock tick.</summary>
    public sealed class PipelineFlowSample
    {
        public long Cycle { get; }
        public bool InputValid { get; }
        public bool InputReady { get; }
        public bool OutputValid { get; }
        public bool OutputReady { get; }
        public IReadOnlyList<bool> StageOccupied { get; }
        public bool InputAccepted => InputValid && InputReady;
        public bool OutputAccepted => OutputValid && OutputReady;
        public bool InputStalled => InputValid && !InputReady;
        public bool OutputStalled => OutputValid && !OutputReady;
        public PipelineFlowSample(long cycle,bool iv,bool ir,bool ov,bool or_,bool[] stages)
        { Cycle=cycle; InputValid=iv; InputReady=ir; OutputValid=ov; OutputReady=or_;
          StageOccupied=Array.AsReadOnly((bool[])stages.Clone()); }
    }
    /// <summary>Observe a real SharpHDL simulator without changing DUT behavior.</summary>
    public sealed class PipelineFlowProbe
    {
        readonly PipelineHandle handle;
        readonly HwChannel input;
        readonly HwChannel output;
        public PipelineFlowProbe(PipelineHandle handle,HwChannel input,HwChannel output)
        {
            this.handle=handle??throw new ArgumentNullException(nameof(handle));
            this.input=input??throw new ArgumentNullException(nameof(input));
            this.output=output??throw new ArgumentNullException(nameof(output));
            if(handle.StageValid==null || handle.StageValid.Count!=handle.Latency)
                throw new ArgumentException("Missing pipeline stage occupancy signals.");
        }
        public PipelineFlowSample Sample(Simulator simulator,long cycle)
        {
            if(simulator==null)throw new ArgumentNullException(nameof(simulator));
            if(cycle<0)throw new ArgumentOutOfRangeException(nameof(cycle));
            bool Read(Expr expression)=>simulator.Read(expression)!=BigInteger.Zero;
            return new PipelineFlowSample(cycle,Read(input.Valid),Read(input.Ready),
                Read(output.Valid),Read(output.Ready),handle.StageValid.Select(Read).ToArray());
        }
    }
}
