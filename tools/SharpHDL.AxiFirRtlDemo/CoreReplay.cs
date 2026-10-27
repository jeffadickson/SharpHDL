using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Numerics;
using SharpHDL.HLS;
using SharpHDL.RTL;
using SharpHDL.Simulation;

/// <summary>Compact file-driven cycle replay for either independent HDL simulator.</summary>
internal sealed class CoreReplay
{
    private readonly ScheduledHardwareDesign design;
    private readonly Dictionary<string,int> values;
    private readonly StringBuilder vectors=new();
    private IReadOnlyDictionary<string,BigInteger>? snapshot;
    internal HardwareGraphMachine Machine { get; }
    internal CoreReplay(ScheduledHardwareDesign design)
    {
        this.design=design;
        Machine=new(design.Graph,design.Inputs,design.Outputs,design.NextState,design.ResetValues,design.Memories);
        values=design.Inputs.Where(p=>!design.ResetValues.ContainsKey(p.Key)).ToDictionary(p=>p.Key,p=>0);
    }
    internal void Drive(string name,int value){Machine.SetInput(name,unchecked((uint)value));values[name]=value;snapshot=null;}
    internal BigInteger Read(string name)=>(snapshot??=Machine.ReadOutputs())[name];
    internal void Clock(bool reset)
    {
        if(reset)Machine.Reset();else Machine.Tick();
        snapshot=Machine.ReadOutputs();
        vectors.Append(reset?"1":"0");
        foreach(var value in values.Values)vectors.Append(' ').Append(unchecked((uint)value).ToString("X"));
        foreach(var name in design.Outputs.Keys)vectors.Append(' ').Append(snapshot[name].ToString("X"));
        vectors.AppendLine();
    }
    internal void Save(string directory)
    {
        if(!Directory.Exists(directory))Directory.CreateDirectory(directory);
        var nodes=design.Graph.Nodes.ToDictionary(n=>n.Id);
        var tb=new StringBuilder("`timescale 1ns/1ps\nmodule tb;\nreg clk=0, rst=0; reg reset_value; integer fd,status,row;\n");
        foreach(var name in values.Keys)tb.AppendLine($"reg [{nodes[design.Inputs[name]].Type.Width-1}:0] s_{name}=0, scanned_{name};");
        foreach(var port in design.Outputs)
        {int width=nodes[port.Value].Type.Width;tb.AppendLine($"wire [{width-1}:0] s_{port.Key}; reg [{width-1}:0] expected_{port.Key};");}
        tb.Append($"h_{design.Name} dut(.clk(clk),.rst(rst)");
        foreach(var name in values.Keys.Concat(design.Outputs.Keys))tb.Append($",.s_{name}(s_{name})");
        tb.AppendLine(");\ninitial begin");
        tb.AppendLine($"fd=$fopen(\"{design.Name}.vec\",\"r\"); if(fd==0) $fatal(1,\"vectors missing\"); row=0;");
        tb.AppendLine("while(!$feof(fd)) begin");
        int fields=1+values.Count+design.Outputs.Count;
        tb.Append($"status=$fscanf(fd,\"{string.Join(" ",Enumerable.Repeat("%h",fields))}\\n\",reset_value");
        foreach(var name in values.Keys)tb.Append($",scanned_{name}");
        foreach(var name in design.Outputs.Keys)tb.Append($",expected_{name}");
        tb.AppendLine($"); if(status!={fields}) $fatal(1,\"malformed vector row %0d\",row);");
        // Explicit assignments make stimulus changes visible to HDL scheduling and
        // compiled simulators, rather than relying on $fscanf output side effects.
        foreach(var name in values.Keys)tb.AppendLine($"s_{name}=scanned_{name};");
        tb.AppendLine("rst=reset_value; #1; clk=1; #1;");
        foreach(var name in design.Outputs.Keys)tb.AppendLine($"if(s_{name} !== expected_{name}) $fatal(1,\"row %0d {name}: actual=%h expected=%h\",row,s_{name},expected_{name});");
        tb.AppendLine("clk=0; #1; rst=0; row=row+1; end");
        tb.AppendLine($"$fclose(fd); $display(\"PASS {design.Name} %0d edges\",row); $finish; end endmodule");
        File.WriteAllText(Path.Combine(directory,design.Name+".v"),CoreVerilogEmitter.Emit(design.Graph,design.Inputs,design.Outputs,design.NextState,design.ResetValues,design.Memories,design.Name));
        File.WriteAllText(Path.Combine(directory,design.Name+"_tb.sv"),tb.ToString());
        File.WriteAllText(Path.Combine(directory,design.Name+".vec"),vectors.ToString());
    }
}
