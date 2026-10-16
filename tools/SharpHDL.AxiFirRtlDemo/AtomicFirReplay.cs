using System;
using System.IO;
using System.Linq;
using System.Text;
using SharpHDL;
using SharpHDL.Examples;

/// <summary>
/// Generates a deterministic SystemVerilog replay for the atomic AXI4-Lite
/// coefficient FIR, comparing every observable output after every clock.
/// The independent arithmetic oracle is exercised by the xUnit FIR tests.
/// </summary>
internal static class AtomicFirReplay
{
    /// <summary>Generate RTL and its cycle-accurate AXI bus replay.</summary>
    internal static void Generate(string directory)
    {
        Directory.CreateDirectory(directory);
        var module=AxiFirStructural.BuildWithAtomicAxiLite();
        var sim=module.Simulate();
        var ports=module.Ports.ToDictionary(p=>p.Name,p=>p);
        var tb=new StringBuilder();
        tb.AppendLine("`timescale 1ns/1ps");
        tb.AppendLine("module tb;");
        tb.AppendLine("reg clk=0, rst=0;");
        foreach(var port in module.Ports)
            tb.AppendLine($"{(port.IsInput?"reg":"wire")} [{port.Width-1}:0] s_{port.Name};");
        tb.Append("h_AxiFirAtomicConfigured dut(.clk(clk),.rst(rst)");
        foreach(var port in module.Ports)tb.Append($",.s_{port.Name}(s_{port.Name})");
        tb.AppendLine(");");
        tb.AppendLine("initial begin");
        foreach(var port in module.Ports.Where(p=>p.IsInput))
            tb.AppendLine($"s_{port.Name}=0;");

        void Drive(string name,int value)
        {
            sim.Set(ports[name],value);
            tb.AppendLine($"s_{name}={ports[name].Width}'d{value};");
        }
        int Read(string name)=>(int)sim.Read(ports[name]);
        int cycle=0;
        void Clock(bool reset=false)
        {
            sim.Tick(reset);
            tb.AppendLine($"rst={(reset?1:0)}; #1; clk=1; #1;");
            foreach(var port in module.Ports.Where(p=>!p.IsInput))
                tb.AppendLine($"if (s_{port.Name} !== {port.Width}'d{sim.Read(port)}) $fatal(1,\"cycle {cycle}: {port.Name} mismatch\");");
            tb.AppendLine("clk=0; #1; rst=0;");
            cycle++;
        }
        Clock(true);
        Drive("m_axis_tready",1);
        Drive("s_axis_tkeep",3);
        Drive("s_axis_tuser",0);
        Drive("s_axi_bready",0);
        Drive("s_axi_rready",0);

        void Write(int address,int value,int strobe)
        {
            Drive("s_axi_awaddr",address);
            Drive("s_axi_awvalid",1);
            if(Read("s_axi_awready")!=1)throw new InvalidOperationException("AW not ready");
            Clock();
            Drive("s_axi_awvalid",0);
            Drive("s_axi_wdata",value);
            Drive("s_axi_wstrb",strobe);
            Drive("s_axi_wvalid",1);
            if(Read("s_axi_wready")!=1)throw new InvalidOperationException("W not ready");
            Clock();
            Drive("s_axi_wvalid",0);
            Clock();
            if(Read("s_axi_bvalid")!=1 || Read("s_axi_bresp")!=0)
                throw new InvalidOperationException("AXI write failed");
            Drive("s_axi_bready",1);
            Clock();
            Drive("s_axi_bready",0);
        }
        void Sample(int value,int expected)
        {
            Drive("s_axis_tvalid",1);
            Drive("s_axis_tdata",value);
            if(Read("s_axis_tready")!=1)throw new InvalidOperationException("Input stalled");
            Clock();
            if(Read("m_axis_tvalid")!=1 || Read("m_axis_tdata")!=expected)
                throw new InvalidOperationException($"FIR output mismatch: expected {expected}");
            Drive("s_axis_tvalid",0);
            Clock();
        }

        // Staging must not affect live coefficients until the APPLY doorbell.
        Write(0,5,15);
        Write(4,7,15);
        Sample(3,3); // Active bank remains [1,0,0,0].
        Write(16,0,0); // Zero strobes cannot apply.
        Sample(4,4);
        Write(16,1,1);
        // History is [4,3,...] from the two previous samples.
        Sample(2,5*2+7*4);
        Sample(6,5*6+7*2);
        Clock(true);
        Sample(9,9); // Reset restores pass-through coefficient bank.
        tb.AppendLine("$display(\"PASS atomic AXI FIR RTL replay\"); $finish; end endmodule");
        File.WriteAllText(Path.Combine(directory,"AxiFirAtomicConfigured.v"),module.Verilog());
        File.WriteAllText(Path.Combine(directory,"AxiFirAtomicConfigured_tb.sv"),tb.ToString());
        Console.WriteLine($"Generated atomic FIR replay with {cycle} clock edges.");
    }
}
