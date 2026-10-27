using System;
using System.IO;
using System.Linq;
using System.Text;
using SharpHDL.Examples;
using SharpHDL.RTL;
using SharpHDL.Simulation;
using SharpHDL.Verification;

/// <summary>Cycle-exact RTL replay after checking Core execution against an independent array model.</summary>
internal static class ScheduledTableReplay
{
    internal static void Generate(string directory)
    {
        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
        foreach (int width in new[] { 1, 4, 8 })
        {
            var d = ScheduledTable.Build(width);
            var sim = new HardwareGraphMachine(d.Graph, d.Inputs, d.Outputs,
                d.NextState, d.ResetValues, d.Memories);
            var nodes = d.Graph.Nodes.ToDictionary(n => n.Id);
            var inputs = d.Inputs.Where(p => !d.ResetValues.ContainsKey(p.Key)).ToArray();
            var tb = new StringBuilder("`timescale 1ns/1ps\nmodule tb;\nreg clk=0, rst=0;\n");
            foreach (var port in inputs)
                tb.AppendLine($"reg [{nodes[port.Value].Type.Width - 1}:0] s_{port.Key}=0;");
            foreach (var port in d.Outputs)
                tb.AppendLine($"wire [{nodes[port.Value].Type.Width - 1}:0] s_{port.Key};");
            tb.Append($"h_{d.Name} dut(.clk(clk),.rst(rst)");
            foreach (var port in inputs.Concat(d.Outputs)) tb.Append($",.s_{port.Key}(s_{port.Key})");
            tb.AppendLine(");\ninitial begin");
            int edges = 0;
            void Drive(string name, int value)
            {
                sim.SetInput(name, value);
                tb.AppendLine($"s_{name}={nodes[d.Inputs[name]].Type.Width}'d{value};");
            }
            void Clock(bool reset)
            {
                if (reset) sim.Reset(); else sim.Tick();
                tb.AppendLine($"rst={(reset ? 1 : 0)}; #1; clk=1; #1;");
                foreach (var port in d.Outputs)
                    tb.AppendLine($"if(s_{port.Key} !== {nodes[port.Value].Type.Width}'d{sim.ReadOutput(port.Key)}) $fatal(1,\"edge {edges} {port.Key}\");");
                tb.AppendLine("clk=0; #1; rst=0;");
                edges++;
            }
            int completed = ScheduledTableCampaign.Run(width, Drive, name => (int)sim.ReadOutput(name), Clock);
            tb.AppendLine($"$display(\"PASS table {width}: {completed} completed\"); $finish; end endmodule");
            File.WriteAllText(Path.Combine(directory, d.Name + ".v"),
                CoreVerilogEmitter.Emit(d.Graph, d.Inputs, d.Outputs, d.NextState,
                    d.ResetValues, d.Memories, d.Name));
            File.WriteAllText(Path.Combine(directory, d.Name + "_tb.sv"), tb.ToString());
            Console.WriteLine($"Generated {d.Name}: {edges} edges, {completed} completed transactions.");
        }
    }
}
