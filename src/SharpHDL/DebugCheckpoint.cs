using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SharpHDL
{
    /// <summary>
    /// An in-process hardware debugger checkpoint including simulator state, cycle
    /// number, watch trace and value/change-breakpoint history.
    /// Only the owning debug session may restore this checkpoint.
    /// </summary>
    public sealed class HardwareDebugCheckpoint
    {
        internal readonly HardwareDebugSession Owner;
        internal readonly SimulatorSnapshot Simulator;
        internal readonly long SavedCycle;
        internal readonly int? SavedBreakpointId;
        internal readonly HardwareTraceSample[] Samples;
        internal readonly Dictionary<int, BigInteger?> BreakpointHistory;

        internal HardwareDebugCheckpoint(HardwareDebugSession owner, SimulatorSnapshot simulator,
            long cycle, int? breakpointId, IEnumerable<HardwareTraceSample> samples,
            Dictionary<int, BigInteger?> breakpointHistory)
        {
            Owner = owner;
            Simulator = simulator;
            SavedCycle = cycle;
            SavedBreakpointId = breakpointId;
            Samples = samples.ToArray();
            BreakpointHistory = new Dictionary<int, BigInteger?>(breakpointHistory);
        }
    }

    public sealed partial class HardwareDebugSession
    {
        /// <summary>
        /// Capture the entire logical debug position. Watches and breakpoint definitions
        /// are session configuration and are not modified by checkpoints.
        /// </summary>
        public HardwareDebugCheckpoint CaptureCheckpoint()
        {
            return new HardwareDebugCheckpoint(this, simulator.CaptureSnapshot(), Cycle,
                LastBreakpointId, trace,
                breakpoints.ToDictionary(bp => bp.Id, bp => bp.LastValue));
        }

        /// <summary>
        /// Restore simulator and debugger history without emitting an artificial clock edge.
        /// Reject checkpoints whose breakpoint membership changed since capture, rather than
        /// silently restoring incomplete trigger state.
        /// </summary>
        public void RestoreCheckpoint(HardwareDebugCheckpoint checkpoint)
        {
            if (checkpoint == null) throw new ArgumentNullException(nameof(checkpoint));
            if (!ReferenceEquals(checkpoint.Owner, this))
                throw new ArgumentException("Checkpoint belongs to another debug session.", nameof(checkpoint));
            if (breakpoints.Count != checkpoint.BreakpointHistory.Count ||
                breakpoints.Any(bp => !checkpoint.BreakpointHistory.ContainsKey(bp.Id)))
                throw new InvalidOperationException("Breakpoint set changed since checkpoint capture.");
            simulator.RestoreSnapshot(checkpoint.Simulator);
            Cycle = checkpoint.SavedCycle;
            LastBreakpointId = checkpoint.SavedBreakpointId;
            trace.Clear();
            trace.AddRange(checkpoint.Samples);
            foreach (var bp in breakpoints) bp.LastValue = checkpoint.BreakpointHistory[bp.Id];
        }
    }
}
