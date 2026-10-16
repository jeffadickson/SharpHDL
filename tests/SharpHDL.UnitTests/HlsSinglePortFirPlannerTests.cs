using System;
using System.Linq;
using SharpHDL.HLS;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Validates physical clock slots for single-port RAM FIR scheduling.</summary>
public sealed class HlsSinglePortFirPlannerTests
{
    /// <summary>
    /// The four-tap schedule must match the verified structural RAM FIR:
    /// accept/write at 0, read issues at 1/3/5, accumulation at 2/4/6,
    /// output at 7, and earliest next acceptance at 8.
    /// </summary>
    [Fact]
    public void Four_tap_schedule_matches_structural_ram_fir()
    {
        var schedule=HlsSinglePortFirPlanner.Plan(4);
        Assert.Equal(4,schedule.Taps);
        Assert.Equal(7,schedule.OutputValidOffset);
        Assert.Equal(8,schedule.InitiationInterval);
        Assert.Equal(new[]{
            new HlsMemoryStep(0,HlsMemoryAction.AcceptAndWrite,0),
            new HlsMemoryStep(1,HlsMemoryAction.IssueRead,1),
            new HlsMemoryStep(2,HlsMemoryAction.Accumulate,1),
            new HlsMemoryStep(3,HlsMemoryAction.IssueRead,2),
            new HlsMemoryStep(4,HlsMemoryAction.Accumulate,2),
            new HlsMemoryStep(5,HlsMemoryAction.IssueRead,3),
            new HlsMemoryStep(6,HlsMemoryAction.Accumulate,3),
            new HlsMemoryStep(7,HlsMemoryAction.Output,-1)
        },schedule.Steps);
    }

    /// <summary>
    /// Every historical read consumes exactly one memory port in its issue
    /// cycle, and each result is consumed one cycle later.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(16)]
    [InlineData(256)]
    public void All_tap_counts_have_nonconflicting_clock_slots(int taps)
    {
        var schedule=HlsSinglePortFirPlanner.Plan(taps);
        Assert.Equal(2*taps,schedule.Steps.Count);
        Assert.Equal(2*taps,schedule.InitiationInterval);
        Assert.Equal(2*taps-1,schedule.OutputValidOffset);
        Assert.Equal(Enumerable.Range(0,2*taps),
            schedule.Steps.Select(s=>s.Cycle));
        Assert.Single(schedule.Steps.Where(s=>s.Action==HlsMemoryAction.AcceptAndWrite));
        Assert.Equal(taps-1,schedule.Steps.Count(s=>s.Action==HlsMemoryAction.IssueRead));
        for(int tap=1;tap<taps;tap++)
        {
            Assert.Contains(new HlsMemoryStep(2*tap-1,HlsMemoryAction.IssueRead,tap),
                schedule.Steps);
            Assert.Contains(new HlsMemoryStep(2*tap,HlsMemoryAction.Accumulate,tap),
                schedule.Steps);
        }
    }

    /// <summary>Unsupported tap counts must be rejected before hardware generation.</summary>
    [Fact]
    public void Invalid_tap_counts_are_rejected()
    {
        foreach(var count in new[]{-1,0,257,int.MaxValue})
            Assert.Throws<ArgumentOutOfRangeException>(()=>
                HlsSinglePortFirPlanner.Plan(count));
    }
}
