using System;
using System.IO;
using System.Linq;
using System.Text;
using SharpHDL;
using SharpHDL.Examples;

/// <summary>
/// Emits an independent Icarus Verilog replay for the single-port RAM FIR.
/// Every public output is checked after every rising clock edge.
/// </summary>
internal static class RamFirReplay
{
    /// <summary>Generate 900 deterministic cycles with held AXI beats and stalls.</summary>
    internal static void Generate(string directory)
    {
        Directory.CreateDirectory(directory);
        var module=AxiFirRam.Build();
        var sim=module.Simulate();
        var ports=module.Ports.ToDictionary(p=>p.Name,p=>p);
        var tb=new StringBuilder();
        tb.AppendLine("`timescale 1ns/1ps");
        tb.AppendLine("module tb;");
        tb.AppendLine("reg clk=0, rst=0;");
        foreach(var port in module.Ports)
            tb.AppendLine($"{(port.IsInput?"reg":"wire")} [{port.Width-1}:0] s_{port.Name};");
        tb.Append("h_AxiFirRam dut(.clk(clk),.rst(rst)");
        foreach(var port in module.Ports)
            tb.Append($",.s_{port.Name}(s_{port.Name})");
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
        int edge=0;
        void Clock(bool reset=false)
        {
            sim.Tick(reset);
            tb.AppendLine($"rst={(reset?1:0)}; #1; clk=1; #1;");
            foreach(var port in module.Ports.Where(p=>!p.IsInput))
                tb.AppendLine($"if (s_{port.Name} !== {port.Width}'d{sim.Read(port)}) $fatal(1,\"edge {edge}: {port.Name} mismatch\");");
            tb.AppendLine("clk=0; #1; rst=0;");
            edge++;
        }
        Clock(true);
        var rng=new Random(20261015);
        ushort[] coeff={3,5,7,11};
        for(int i=0;i<4;i++)Drive("coefficient"+i,coeff[i]);
        bool pending=false;
        ushort sample=0;
        int last=0;
        int accepted=0,completed=0;
        for(int cycle=0;cycle<900;cycle++)
        {
            if(cycle==450)
            {
                Drive("s_axis_tvalid",0);
                Drive("m_axis_tready",0);
                Clock(true);
                pending=false;
                accepted=completed=0;
            }
            if(!pending && cycle<820 && rng.Next(4)!=0)
            {
                pending=true;
                sample=(ushort)rng.Next(65536);
                last=rng.Next(17)==0?1:0;
            }
            int ready=cycle>=820 || rng.Next(3)!=0?1:0;
            Drive("s_axis_tvalid",pending?1:0);
            Drive("s_axis_tdata",sample);
            Drive("s_axis_tlast",last);
            Drive("s_axis_tkeep",3);
            Drive("s_axis_tuser",0);
            Drive("m_axis_tready",ready);
            bool accept=pending && Read("s_axis_tready")!=0;
            bool retire=ready!=0 && Read("m_axis_tvalid")!=0;
            Clock();
            if(accept){pending=false;accepted++;}
            if(retire)completed++;
        }
        if(pending || accepted!=completed || Read("m_axis_tvalid")!=0)
            throw new InvalidOperationException($"RAM FIR not drained: accepted={accepted}, completed={completed}.");
        tb.AppendLine("$display(\"PASS RAM FIR RTL replay\"); $finish; end endmodule");
        File.WriteAllText(Path.Combine(directory,"AxiFirRam.v"),module.Verilog());
        File.WriteAllText(Path.Combine(directory,"AxiFirRam_tb.sv"),tb.ToString());
        Console.WriteLine($"Generated RAM FIR RTL replay: {edge} edges, {completed} post-reset transfers.");
    }
}
