using System;
using System.Linq;
using SharpHDL.Examples;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Cycle-level acceptance of the structural AXI FIR against an independent oracle.</summary>
public sealed class AxiFirStructuralTests
{
    /// <summary>Check output transactions and sidebands through randomized AXI stalls.</summary>
    [Fact]
    public void Structural_fir_matches_reference_on_accepted_axi_transfers()
    {
        var module = AxiFirStructural.Build();
        var sim = module.Simulate();
        var ports = module.Ports.ToDictionary(p => p.Name, p => p);
        void Set(string name, int value) => sim.Set(ports[name], value);
        int Read(string name) => (int)sim.Read(ports[name]);
        var reference = new AxiFirReference(capacity: 1);
        var rng = new Random(45110);
        ushort[] coefficients = { 3, 5, 7, 11 };
        sim.Tick(true);
        int completed = 0;
        for (int cycle = 0; cycle < 600; cycle++)
        {
            bool valid = cycle < 540 && rng.Next(4) != 0;
            bool ready = cycle >= 540 || rng.Next(3) != 0;
            ushort sample = (ushort)rng.Next(65536);
            bool last = rng.Next(19) == 0;
            Set("s_axis_data", sample);
            Set("s_axis_valid", valid ? 1 : 0);
            Set("s_axis_last", last ? 1 : 0);
            Set("s_axis_keep", 3);
            Set("s_axis_user", 0);
            Set("m_axis_ready", ready ? 1 : 0);
            for (int i = 0; i < 4; i++) Set("coefficient" + i, coefficients[i]);
            Assert.Equal(reference.OutputValid, Read("m_axis_valid") != 0);
            Assert.Equal(reference.InputReady || (reference.OutputValid && ready),
                Read("s_axis_ready") != 0);
            if (reference.OutputValid)
            {
                Assert.Equal(reference.Output.Data, (ushort)Read("m_axis_data"));
                Assert.Equal(reference.Output.Last, Read("m_axis_last") != 0);
                Assert.Equal(3, Read("m_axis_keep"));
                if (ready) completed++;
            }
            reference.Tick(valid, sample, last, ready, coefficients);
            sim.Tick();
        }
        Assert.False(reference.OutputValid);
        Assert.True(completed > 100);
        Assert.Contains("module", module.Verilog());
    }
}
