using System;
using System.Linq;
using SharpHDL.Examples;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Cycle-level acceptance of the structural AXI FIR against an independent oracle.</summary>
public sealed class AxiFirStructuralTests
{
    /// <summary>Protect the public AXI4-Stream signal naming and widths.</summary>
    [Fact]
    public void Structural_fir_exposes_standard_axi_stream_ports()
    {
        var ports=AxiFirStructural.Build().Ports.ToDictionary(p=>p.Name,p=>p);
        foreach(var prefix in new[]{"s_axis","m_axis"})
        {
            Assert.Equal(16,ports[prefix+"_tdata"].Width);
            Assert.Equal(2,ports[prefix+"_tkeep"].Width);
            Assert.Equal(8,ports[prefix+"_tuser"].Width);
            Assert.Equal(1,ports[prefix+"_tvalid"].Width);
            Assert.Equal(1,ports[prefix+"_tready"].Width);
            Assert.Equal(1,ports[prefix+"_tlast"].Width);
        }
    }

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
        bool pendingValid = false;
        ushort pendingSample = 0;
        bool pendingLast = false;
        int accepted = 0;
        for (int cycle = 0; cycle < 600; cycle++)
        {
            // The producer may offer a new transaction only when no previous
            // transaction is waiting. AXI requires stable VALID and payload
            // until the receiver completes a handshake.
            if (!pendingValid && cycle < 540 && rng.Next(4) != 0)
            {
                pendingValid = true;
                pendingSample = (ushort)rng.Next(65536);
                pendingLast = rng.Next(19) == 0;
            }
            bool ready = cycle >= 540 || rng.Next(3) != 0;
            Set("s_axis_tdata", pendingSample);
            Set("s_axis_tvalid", pendingValid ? 1 : 0);
            Set("s_axis_tlast", pendingLast ? 1 : 0);
            Set("s_axis_tkeep", 3);
            Set("s_axis_tuser", 0);
            Set("m_axis_tready", ready ? 1 : 0);
            for (int i = 0; i < 4; i++) Set("coefficient" + i, coefficients[i]);
            bool outputValid = reference.OutputValid;
            bool inputReady = reference.InputReady || (outputValid && ready);
            Assert.Equal(outputValid, Read("m_axis_tvalid") != 0);
            Assert.Equal(inputReady, Read("s_axis_tready") != 0);
            if (outputValid)
            {
                Assert.Equal(reference.Output.Data, (ushort)Read("m_axis_tdata"));
                Assert.Equal(reference.Output.Last, Read("m_axis_tlast") != 0);
                Assert.Equal(3, Read("m_axis_tkeep"));
                if (ready) completed++;
            }
            bool inputHandshake = pendingValid && inputReady;
            reference.Tick(pendingValid, pendingSample, pendingLast, ready, coefficients);
            sim.Tick();
            if (inputHandshake)
            {
                pendingValid = false;
                accepted++;
            }
        }
        Assert.False(pendingValid);
        Assert.Equal(accepted, completed);
        Assert.False(reference.OutputValid);
        Assert.True(completed > 100);
        Assert.Contains("module", module.Verilog());
    }
    /// <summary>
    /// Exercise coefficient changes only on accepted samples, including a
    /// blocked output that must retain its previous result and sidebands.
    /// </summary>
    [Fact]
    public void Coefficients_change_between_handshakes_without_corrupting_blocked_output()
    {
        var module=AxiFirStructural.Build();
        var sim=module.Simulate();
        var ports=module.Ports.ToDictionary(p=>p.Name,p=>p);
        void Set(string name,int value)=>sim.Set(ports[name],value);
        int Read(string name)=>(int)sim.Read(ports[name]);
        sim.Tick(true);
        Set("m_axis_tready",0);
        Set("s_axis_tvalid",1);
        Set("s_axis_tdata",10);
        Set("s_axis_tkeep",3);
        Set("s_axis_tlast",1);
        Set("s_axis_tuser",19);
        for(int i=0;i<4;i++)Set("coefficient"+i,i==0?2:0);
        sim.Tick();
        Assert.Equal(20,Read("m_axis_tdata"));
        Assert.Equal(1,Read("m_axis_tvalid"));
        Assert.Equal(1,Read("m_axis_tlast"));
        Assert.Equal(19,Read("m_axis_tuser"));
        // An offered second sample cannot advance while the first is blocked.
        Set("s_axis_tdata",7);
        Set("s_axis_tlast",0);
        Set("s_axis_tuser",23);
        for(int i=0;i<4;i++)Set("coefficient"+i,i==0?3:0);
        Assert.Equal(0,Read("s_axis_tready"));
        sim.Tick();
        Assert.Equal(20,Read("m_axis_tdata"));
        Assert.Equal(19,Read("m_axis_tuser"));
        // Consuming the old output and accepting the new input on one edge.
        Set("m_axis_tready",1);
        sim.Tick();
        Assert.Equal(21,Read("m_axis_tdata"));
        Assert.Equal(0,Read("m_axis_tlast"));
        Assert.Equal(23,Read("m_axis_tuser"));
    }

    /// <summary>Verify reset removes a blocked output and clears FIR history.</summary>
    [Fact]
    public void Reset_during_output_stall_discards_old_sample_and_history()
    {
        var module=AxiFirStructural.Build();
        var sim=module.Simulate();
        var ports=module.Ports.ToDictionary(p=>p.Name,p=>p);
        void Set(string name,int value)=>sim.Set(ports[name],value);
        int Read(string name)=>(int)sim.Read(ports[name]);
        sim.Tick(true);
        for(int i=0;i<4;i++)Set("coefficient"+i,i+1);
        Set("m_axis_tready",0);
        Set("s_axis_tvalid",1);
        Set("s_axis_tdata",123);
        Set("s_axis_tkeep",3);
        Set("s_axis_tlast",1);
        Set("s_axis_tuser",0);
        sim.Tick();
        Assert.Equal(1,Read("m_axis_tvalid"));
        Assert.Equal(123,Read("m_axis_tdata"));
        sim.Tick(true);
        Assert.Equal(0,Read("m_axis_tvalid"));
        Set("s_axis_tvalid",1);
        Set("s_axis_tdata",7);
        Set("s_axis_tlast",0);
        sim.Tick();
        Assert.Equal(1,Read("m_axis_tvalid"));
        Assert.Equal(7,Read("m_axis_tdata"));
    }

}
