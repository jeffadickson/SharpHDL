using System.Text;
using SharpHDL.HLS;

/// <summary>Generate an independent temporal contract over the actual emitted core.</summary>
internal static class ProtocolProof
{
    internal static void Write(ScheduledHardwareDesign design, string directory)
    {
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        var nodes = design.Graph.Nodes.ToDictionary(node => node.Id);
        var inputs = design.Inputs.Where(pair => !design.ResetValues.ContainsKey(pair.Key)).ToArray();
        var text = new StringBuilder("module Z80BusFormal(input clk, input rst");
        foreach (var pair in inputs) text.Append($", input [{nodes[pair.Value].Type.Width - 1}:0] s_{pair.Key}");
        text.AppendLine(");");
        foreach (var pair in design.Outputs) text.AppendLine($"wire [{nodes[pair.Value].Type.Width - 1}:0] s_{pair.Key};");
        text.Append("h_Z80TestCore dut(.clk(clk),.rst(rst)");
        foreach (string name in inputs.Select(pair => pair.Key).Concat(design.Outputs.Keys)) text.Append($",.s_{name}(s_{name})");
        text.AppendLine(");");
        text.AppendLine("reg past_valid=0, past_reset, past_request, past_ready;");
        string[] fields = { "bus_address", "bus_wdata", "bus_write", "bus_io", "bus_irq_ack" };
        foreach (string field in fields) text.AppendLine($"reg [{nodes[design.Outputs[field]].Type.Width - 1}:0] past_{field};");
        text.AppendLine("always @(posedge clk) begin");
        text.AppendLine("if (past_valid && !rst && !past_reset && past_request && !past_ready) begin");
        text.AppendLine("assert(s_bus_valid);");
        foreach (string field in fields) text.AppendLine($"assert(s_{field} == past_{field});");
        text.AppendLine("end");
        text.AppendLine("past_valid <= 1; past_reset <= rst; past_request <= s_bus_valid; past_ready <= s_bus_ready;");
        foreach (string field in fields) text.AppendLine($"past_{field} <= s_{field};");
        text.AppendLine("end\nendmodule");
        File.WriteAllText(Path.Combine(directory, "Z80BusFormal.v"), text.ToString());
    }
}
