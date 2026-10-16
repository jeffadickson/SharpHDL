using System;
using System.IO;
using System.Linq;
using System.Text;
using SharpHDL;
using SharpHDL.Examples;

/// <summary>
/// Creates independently checked, cycle-exact SystemVerilog replays for
/// schedule-generated 2-, 4-, 8-, and 16-tap FIR hardware.
/// </summary>
internal static class ScheduledFirReplay
{
    /// <summary>Generate one Verilog design and testbench per tap count.</summary>
    internal static void Generate(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach(int taps in new[]{2,4,8,16})
            GenerateCase(directory,taps);
    }

    /// <summary>
    /// Exercise circular history, AXI stalls, reset, and arithmetic independently
    /// of the SharpHDL simulation used for the generated RTL waveform oracle.
    /// </summary>
    private static void GenerateCase(string directory,int taps)
    {
        var module=HlsScheduledFir.Build(taps);
        var sim=module.Simulate();
        var ports=module.Ports.ToDictionary(p=>p.Name,p=>p);
        var tb=new StringBuilder();
        tb.AppendLine("`timescale 1ns/1ps");
        tb.AppendLine("module tb;");
        tb.AppendLine("reg clk=0, rst=0;");
        foreach(var port in module.Ports)
            tb.AppendLine($"{(port.IsInput?"reg":"wire")} [{port.Width-1}:0] s_{port.Name};");
        tb.Append($"h_HlsScheduledFir{taps} dut(.clk(clk),.rst(rst)");
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
                tb.AppendLine($"if (s_{port.Name} !== {port.Width}'d{sim.Read(port)}) $fatal(1,\"tap count {taps}, edge {edge}: {port.Name} mismatch\");");
            tb.AppendLine("clk=0; #1; rst=0;");
            edge++;
        }

        Clock(true);
        var coefficients=Enumerable.Range(0,taps).Select(i=>(ushort)(3+7*i)).ToArray();
        for(int i=0;i<taps;i++)Drive("coefficient"+i,coefficients[i]);
        var history=new ushort[taps-1];
        var expected=new System.Collections.Generic.Queue<(ushort Data,bool Last)>();
        var rng=new Random(20261015+taps);
        bool pending=false;
        ushort sample=0;
        bool last=false;
        int accepted=0,completed=0;
        int cycles=1200+160*taps;
        for(int cycle=0;cycle<cycles;cycle++)
        {
            // Reset all in-flight transactions and logical history at a
            // deterministic boundary, including any blocked producer beat.
            if(cycle==cycles/2)
            {
                Drive("s_axis_tvalid",0);
                Drive("m_axis_tready",0);
                Clock(true);
                pending=false;
                Array.Clear(history,0,history.Length);
                expected.Clear();
                accepted=completed=0;
            }
            if(!pending && cycle<cycles-120 && rng.Next(4)!=0)
            {
                pending=true;
                sample=(ushort)rng.Next(65536);
                last=rng.Next(13)==0;
            }
            bool ready=cycle>=cycles-120 || rng.Next(3)!=0;
            Drive("s_axis_tvalid",pending?1:0);
            Drive("s_axis_tdata",sample);
            Drive("s_axis_tlast",last?1:0);
            Drive("s_axis_tkeep",3);
            Drive("s_axis_tuser",0);
            Drive("m_axis_tready",ready?1:0);
            bool inputReady=Read("s_axis_tready")!=0;
            bool outputValid=Read("m_axis_tvalid")!=0;
            if(outputValid)
            {
                if(expected.Count==0)throw new InvalidOperationException($"Unexpected FIR output, taps={taps}, cycle={cycle}");
                var oracle=expected.Peek();
                if(Read("m_axis_tdata")!=oracle.Data ||
                    (Read("m_axis_tlast")!=0)!=oracle.Last ||
                    Read("m_axis_tkeep")!=3 || Read("m_axis_tuser")!=0)
                    throw new InvalidOperationException($"FIR oracle mismatch, taps={taps}, cycle={cycle}");
                if(ready)
                {
                    expected.Dequeue();
                    completed++;
                }
            }
            bool take=pending && inputReady;
            if(take)
            {
                ulong sum=(ulong)sample*coefficients[0];
                for(int i=1;i<taps;i++)
                    sum+=(ulong)history[i-1]*coefficients[i];
                expected.Enqueue(((ushort)sum,last));
                for(int i=history.Length-1;i>0;i--)history[i]=history[i-1];
                history[0]=sample;
                accepted++;
            }
            Clock();
            if(take)pending=false;
        }
        if(pending || expected.Count!=0 || accepted!=completed || Read("m_axis_tvalid")!=0)
            throw new InvalidOperationException($"FIR not drained, taps={taps}, accepted={accepted}, completed={completed}");
        tb.AppendLine($"$display(\"PASS scheduled FIR {taps} taps\"); $finish; end endmodule");
        File.WriteAllText(Path.Combine(directory,$"HlsScheduledFir{taps}.v"),module.Verilog());
        File.WriteAllText(Path.Combine(directory,$"HlsScheduledFir{taps}_tb.sv"),tb.ToString());
        Console.WriteLine($"Generated {taps}-tap scheduled FIR: {edge} clock edges, {completed} post-reset transfers.");
    }
}
