using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace SharpHDL
{
    /// <summary>Record a real simulator's sampled pre-edge handshakes and stage occupancy.</summary>
    public sealed class PipelineTraceRecorder
    {
        readonly PipelineSignalProbe probe;
        readonly List<PipelineFlowSample> samples = new List<PipelineFlowSample>();
        public PipelineTraceRecorder(PipelineSignalProbe probe) { this.probe=probe??throw new ArgumentNullException(nameof(probe)); }
        public IReadOnlyList<PipelineFlowSample> Samples => samples.AsReadOnly();
        public void Capture(Simulator simulator,long cycle)
        {
            if(samples.Count>0 && cycle<=samples[samples.Count-1].Cycle)
                throw new InvalidOperationException("Capture cycle must increase.");
            samples.Add(probe.Sample(simulator,cycle));
        }
        public string ToJson(PipelineDataflowGraph graph)
        {
            if(graph==null)throw new ArgumentNullException(nameof(graph));
            return JsonSerializer.Serialize(new {
                SchemaVersion=1,
                Observation="pre-edge",
                Graph=graph,
                Samples=samples.Select(s=>new {
                    s.Cycle,s.InputValid,s.InputReady,s.OutputValid,s.OutputReady,
                    s.InputAccepted,s.OutputAccepted,s.InputStalled,s.OutputStalled,
                    StageOccupied=s.StageOccupied
                }).ToArray()
            },new JsonSerializerOptions{WriteIndented=true});
        }
    }
}
