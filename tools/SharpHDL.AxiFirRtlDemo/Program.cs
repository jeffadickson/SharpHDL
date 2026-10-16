using System;
using System.IO;
using System.Linq;
using System.Text;
using SharpHDL;
using SharpHDL.Examples;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: AxiFirRtlDemo <output-directory>");
    return 2;
}
var directory=Path.GetFullPath(args[0]);
Directory.CreateDirectory(directory);
var module=AxiFirStructural.Build();
var simulator=module.Simulate();
var ports=module.Ports.ToDictionary(p=>p.Name,p=>p);
void Set(string name,int value)=>simulator.Set(ports[name],value);
var tb=new StringBuilder();
tb.AppendLine("`timescale 1ns/1ps");
tb.AppendLine("module tb;");
tb.AppendLine("reg clk=0, rst=0;");
foreach(var port in module.Ports)
    tb.AppendLine($"{(port.IsInput?"reg":"wire")} [{port.Width-1}:0] s_{port.Name};");
tb.Append("h_AxiFirStructural dut(.clk(clk),.rst(rst)");
foreach(var port in module.Ports)tb.Append($",.s_{port.Name}(s_{port.Name})");
tb.AppendLine(");");
tb.AppendLine("initial begin");
foreach(var port in module.Ports.Where(p=>p.IsInput))
    tb.AppendLine($"s_{port.Name}=0;");
void Drive(string name,int value)
{
    Set(name,value);
    tb.AppendLine($"s_{name}={ports[name].Width}'d{value};");
}
void Clock(bool reset=false)
{
    // Settle the driven inputs before the edge, then compare post-edge outputs.
    simulator.Tick(reset);
    tb.AppendLine($"rst={(reset?1:0)}; #1; clk=1; #1;");
    foreach(var port in module.Ports.Where(p=>!p.IsInput))
        tb.AppendLine($"if (s_{port.Name} !== {port.Width}'d{simulator.Read(port)}) $fatal(1,\"{port.Name} mismatch\");");
    tb.AppendLine("clk=0; #1; rst=0;");
}
Clock(true);
var random=new Random(45110);
var oracle=new AxiFirReference(capacity:1);
int acceptedCount=0, completedCount=0;
bool pending=false;
ushort sample=0;
int last=0;
for(int cycle=0;cycle<600;cycle++)
{
    // Reset under traffic, including any stalled transaction. AXI reset
    // discards outstanding transfers and clears the FIR's logical history.
    if(cycle==300)
    {
        Drive("s_axis_valid",0);
        Drive("m_axis_ready",0);
        Clock(true);
        oracle.Reset();
        pending=false;
        acceptedCount=0;
        completedCount=0;
    }
    if(!pending && cycle<540 && random.Next(4)!=0)
    {
        pending=true;
        sample=(ushort)random.Next(65536);
        last=random.Next(19)==0?1:0;
    }
    int ready=cycle>=540 || random.Next(3)!=0?1:0;
    Drive("s_axis_data",sample);
    Drive("s_axis_valid",pending?1:0);
    Drive("s_axis_last",last);
    Drive("s_axis_keep",3);
    Drive("s_axis_user",0);
    Drive("m_axis_ready",ready);
    ushort[] coefficients={3,5,7,11};
    for(int i=0;i<4;i++)Drive("coefficient"+i,coefficients[i]);
    bool observedValid=simulator.Read(ports["m_axis_valid"])!=0;
    bool observedReady=simulator.Read(ports["s_axis_ready"])!=0;
    bool expectedReady=oracle.InputReady || (oracle.OutputValid && ready!=0);
    if(observedValid!=oracle.OutputValid || observedReady!=expectedReady)
        throw new InvalidOperationException($"AXI handshake mismatch at cycle {cycle}.");
    if(observedValid)
    {
        var expected=oracle.Output;
        if(simulator.Read(ports["m_axis_data"])!=(int)expected.Data ||
           (simulator.Read(ports["m_axis_last"])!=0)!=expected.Last ||
           simulator.Read(ports["m_axis_keep"])!=3 ||
           simulator.Read(ports["m_axis_user"])!=0)
            throw new InvalidOperationException($"FIR payload mismatch at cycle {cycle}.");
        if(ready!=0)completedCount++;
    }
    bool accepted=pending && observedReady;
    oracle.Tick(pending,sample,last!=0,ready!=0,coefficients);
    Clock();
    if(accepted){pending=false;acceptedCount++;}
}
if(pending || oracle.OutputValid || acceptedCount!=completedCount)
    throw new InvalidOperationException($"Undrained FIR transactions: accepted={acceptedCount}, completed={completedCount}.");
tb.AppendLine("$display(\"PASS AXI FIR RTL cycle replay\"); $finish; end endmodule");
File.WriteAllText(Path.Combine(directory,"AxiFirStructural.v"),module.Verilog());
File.WriteAllText(Path.Combine(directory,"AxiFirStructural_tb.sv"),tb.ToString());
Console.WriteLine("Generated 600-cycle AXI FIR RTL replay with midstream reset and independent oracle.");
return 0;
