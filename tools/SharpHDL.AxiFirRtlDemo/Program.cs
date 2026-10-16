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
bool pending=false;
ushort sample=0;
int last=0;
for(int cycle=0;cycle<600;cycle++)
{
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
    int[] coefficients={3,5,7,11};
    for(int i=0;i<4;i++)Drive("coefficient"+i,coefficients[i]);
    bool accepted=pending && simulator.Read(ports["s_axis_ready"])!=0;
    Clock();
    if(accepted)pending=false;
}
tb.AppendLine("$display(\"PASS AXI FIR RTL cycle replay\"); $finish; end endmodule");
File.WriteAllText(Path.Combine(directory,"AxiFirStructural.v"),module.Verilog());
File.WriteAllText(Path.Combine(directory,"AxiFirStructural_tb.sv"),tb.ToString());
Console.WriteLine("Generated 600-cycle AXI FIR RTL replay.");
return 0;
