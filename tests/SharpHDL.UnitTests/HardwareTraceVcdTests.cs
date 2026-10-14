using System;
using System.Collections.Generic;
using System.Numerics;
using SharpHDL;
using Xunit;

namespace SharpHDL.UnitTests;

public sealed class HardwareTraceVcdTests
{
    [Fact]
    public void Watch_samples_export_valid_width_and_time_markers()
    {
        var samples = new List<HardwareTraceSample> {
            new(1, "count", 0),
            new(2, "count", 3),
            new(3, "count", 255)
        };
        string vcd = HardwareTraceVcd.Export(samples, new Dictionary<string, int> { ["count"] = 8 });
        Assert.Contains("$timescale 1ns $end", vcd);
        Assert.Contains("$var wire 8 v0 count $end", vcd);
        Assert.Contains("#2\nb00000011 v0", vcd.Replace("\r\n", "\n"));
        Assert.Contains("#3\nb11111111 v0", vcd.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Rejects_out_of_range_sample()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HardwareTraceVcd.Export(new[] { new HardwareTraceSample(1, "sig", 256) },
                new Dictionary<string, int> { ["sig"] = 8 }));
    }
}
