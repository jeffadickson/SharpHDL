using System;
using System.Collections.Generic;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Independent FIR transaction-oracle tests including reset and stalls.</summary>
public sealed class AxiFirReferenceTests
{
    /// <summary>Compare every accepted result with an independently maintained sample history.</summary>
    [Fact]
    public void Random_stalls_and_coefficient_changes_preserve_results_and_last()
    {
        var dut = new SharpHDL.Examples.AxiFirReference(4);
        var rng = new Random(20261015);
        var history = new ushort[3];
        var expected = new Queue<(ushort Data, bool Last)>();
        int accepted = 0, completed = 0;
        for (int cycle = 0; cycle < 1200; cycle++)
        {
            bool drain = cycle >= 1050;
            bool valid = !drain && rng.Next(4) != 0;
            bool ready = drain || rng.Next(3) != 0;
            ushort input = (ushort)rng.Next(65536);
            bool last = rng.Next(13) == 0;
            ushort[] coefficients = {
                (ushort)rng.Next(65536), (ushort)rng.Next(65536),
                (ushort)rng.Next(65536), (ushort)rng.Next(65536)
            };
            bool outputValid = dut.OutputValid;
            bool inputReady = dut.InputReady || (outputValid && ready);
            if (outputValid && ready)
            {
                Assert.NotEmpty(expected);
                Assert.Equal(expected.Dequeue(), dut.Output);
                completed++;
            }
            if (valid && inputReady)
            {
                ulong sum = (ulong)input * coefficients[0]
                          + (ulong)history[0] * coefficients[1]
                          + (ulong)history[1] * coefficients[2]
                          + (ulong)history[2] * coefficients[3];
                expected.Enqueue(((ushort)sum, last));
                history[2] = history[1];
                history[1] = history[0];
                history[0] = input;
                accepted++;
            }
            dut.Tick(valid, input, last, ready, coefficients);
            Assert.InRange(dut.Pending, 0, 4);
        }
        Assert.Empty(expected);
        Assert.Equal(accepted, completed);
        Assert.False(dut.OutputValid);
    }

    /// <summary>Reset discards pending results and zeroes logical history.</summary>
    [Fact]
    public void Reset_drops_blocked_transactions_and_restarts_history()
    {
        var dut = new SharpHDL.Examples.AxiFirReference(1);
        ushort[] coefficients = { 1, 2, 3, 4 };
        dut.Tick(true, 11, true, false, coefficients);
        Assert.True(dut.OutputValid);
        Assert.Equal(((ushort)11, true), dut.Output);
        dut.Reset();
        Assert.False(dut.OutputValid);
        dut.Tick(true, 7, false, false, coefficients);
        Assert.Equal(((ushort)7, false), dut.Output);
        Assert.Equal(1, dut.Pending);
    }
}
