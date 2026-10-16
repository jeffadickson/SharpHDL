using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SharpHDL.HLS;

/// <summary>
/// An operation in a cycle-accurate schedule for a single-port synchronous
/// RAM. These are hardware clock offsets, not abstract combinational costs.
/// </summary>
public enum HlsMemoryAction
{
    /// <summary>Accept a new sample and write it into RAM.</summary>
    AcceptAndWrite,
    /// <summary>Issue a registered RAM read address for a historical tap.</summary>
    IssueRead,
    /// <summary>Consume the previous cycle's registered RAM data and accumulate.</summary>
    Accumulate,
    /// <summary>Offer the completed output until downstream accepts it.</summary>
    Output
}

/// <summary>
/// A clock offset and operation in a resource-constrained FIR schedule.
/// Tap 0 is the incoming sample; taps 1..N-1 read prior history.
/// </summary>
public sealed record HlsMemoryStep(int Cycle, HlsMemoryAction Action, int Tap);

/// <summary>
/// Immutable, technology-neutral single-port FIR schedule. Output is held
/// under backpressure; initiation interval is the no-stall minimum.
/// This plan does not by itself synthesize a Core IR state machine.
/// </summary>
public sealed class HlsSinglePortFirSchedule
{
    /// <summary>Clock-indexed operations, including acceptance and output.</summary>
    public IReadOnlyList<HlsMemoryStep> Steps { get; }
    /// <summary>Number of FIR coefficients.</summary>
    public int Taps { get; }
    /// <summary>Minimum cycles between accepted input transactions.</summary>
    public int InitiationInterval { get; }
    /// <summary>Earliest output-valid clock offset after acceptance.</summary>
    public int OutputValidOffset { get; }

    internal HlsSinglePortFirSchedule(int taps, List<HlsMemoryStep> steps)
    {
        Taps=taps;
        Steps=new ReadOnlyCollection<HlsMemoryStep>(steps);
        OutputValidOffset=steps[^1].Cycle;
        InitiationInterval=checked(OutputValidOffset+1);
    }
}

/// <summary>
/// Schedule a sequential FIR onto one synchronous read-before-write RAM port.
/// The newest sample is written on acceptance; each historical tap requires
/// one read-issue clock and a following accumulation clock.
/// </summary>
public static class HlsSinglePortFirPlanner
{
    /// <summary>
    /// Construct a no-stall schedule for 1..256 taps. A registered RAM read
    /// becomes available after its issue edge; the next edge consumes it.
    /// The output state remains occupied until an AXI ready/valid handshake.
    /// </summary>
    public static HlsSinglePortFirSchedule Plan(int taps)
    {
        if(taps<1 || taps>256)
            throw new ArgumentOutOfRangeException(nameof(taps),
                "Tap count must be between 1 and 256.");
        var steps=new List<HlsMemoryStep>(checked(2*taps));
        steps.Add(new HlsMemoryStep(0,HlsMemoryAction.AcceptAndWrite,0));
        for(int tap=1;tap<taps;tap++)
        {
            int issue=checked(2*tap-1);
            steps.Add(new HlsMemoryStep(issue,HlsMemoryAction.IssueRead,tap));
            steps.Add(new HlsMemoryStep(issue+1,HlsMemoryAction.Accumulate,tap));
        }
        steps.Add(new HlsMemoryStep(checked(2*taps-1),HlsMemoryAction.Output,-1));
        return new HlsSinglePortFirSchedule(taps,steps);
    }
}
