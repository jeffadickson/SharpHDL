using SharpHDL.Examples;
using SharpHDL.Simulation;
using SharpHDL.Verification;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Independent non-FIR scheduled memory/control verification.</summary>
public sealed class ScheduledTableTests
{
    /// <summary>Check reset epochs, stalls, boundaries, repeated addresses and conditional updates.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public void Table_matches_independent_array_and_protocol_model(int addressWidth)
    {
        var d = ScheduledTable.Build(addressWidth);
        // The replay command and documented HDL artifact names are public tooling contracts.
        Assert.Equal("ScheduledTable"+addressWidth,d.Name);
        var machine = new HardwareGraphMachine(d.Graph, d.Inputs, d.Outputs,
            d.NextState, d.ResetValues, d.Memories);
        int count = ScheduledTableCampaign.Run(addressWidth,
            (name, value) => machine.SetInput(name, value),
            name => (int)machine.ReadOutput(name),
            reset => { if (reset) machine.Reset(); else machine.Tick(); });
        Assert.True(count > 100);
        Assert.Single(d.Memories);
    }

    /// <summary>Reject table sizes outside the explicitly supported example range.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void Invalid_table_size_is_rejected(int width)
        => Assert.Throws<ArgumentOutOfRangeException>(() => ScheduledTable.Build(width));
}
