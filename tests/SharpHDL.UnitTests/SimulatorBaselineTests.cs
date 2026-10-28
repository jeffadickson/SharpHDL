using SharpHDL.Examples;
using SharpHDL.HLS;
using SharpHDL.Verification;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Benchmark inputs and checksums remain independently qualified, not just consumed by a timer.</summary>
public sealed class SimulatorBaselineTests
{
    /// <summary>Both observation paths preserve the specified protocol and repeat on fresh machines.</summary>
    [Theory][InlineData(1)][InlineData(8)]
    public void Both_paths_match_independent_protocol_and_repeat(int width)
    {
        // Independently calculated unsigned 64-bit FNV recurrence over the specified ports.
        const ulong expected=8969665363863638565UL;
        Assert.Equal(expected,SimulatorObservationWorkload.Expected());
        var design=ScheduledTable.Build(width);
        Assert.Equal(expected,SimulatorObservationWorkload.Run(design,false));
        Assert.Equal(expected,SimulatorObservationWorkload.Run(design,true));
        Assert.Equal(expected,SimulatorObservationWorkload.Run(design,true));
    }
    /// <summary>A plausible final word must not hide a corrupt ready/valid history.</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void Oracle_rejects_corrupted_protocol_with_correct_final_word(bool snapshot)
    {
        var builder=new ScheduledMachineBuilder("InvalidBenchmarkProtocol");
        builder.Input("request_valid",1);builder.Input("request_update",1);
        builder.Input("request_delta",16);builder.Input("response_ready",1);
        builder.State("hold");
        builder.Output("request_ready",builder.Constant(1,0));
        builder.Output("response_valid",builder.Constant(1,0));
        builder.Output("response_data",builder.Constant(16,64));
        Assert.Throws<InvalidOperationException>(()=>SimulatorObservationWorkload.Run(builder.Lower(),snapshot));
    }
}
