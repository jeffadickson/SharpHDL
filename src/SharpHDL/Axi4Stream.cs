using System;
using System.Linq;
using System.Text;
namespace SharpHDL
{
    public static class Axi4Stream
    {
        // Stream ports already use AXI4-Stream field names. Adapter adds standard
        // clock/reset names and immediate reset masking of handshake outputs.
        public static string Wrapper(Module m)
        {
            m.Validate();var ports=m.Signals.Where(s=>s.Kind=="input"||s.Kind=="output").ToList();
            var b=new StringBuilder("// AXI4-Stream subset: TDATA/TKEEP/TLAST/TUSER/TVALID/TREADY.\n");
            b.AppendLine("// aclk: rising-edge clock. aresetn: active-low; assert through a clock edge to reset the synchronous core.\n// Reset masks handshakes immediately; no reset synchronizer is included.");
            b.AppendLine("module h_"+m.Name+"_axi(\n  input wire aclk,\n  input wire aresetn");
            // Complete the header's final comma-list before closing.
            foreach(var s in ports)b.AppendLine("  // "+(s.Annotation ?? (s.Width+"-bit port, bit 0 least significant.")).Replace("\n"," ").Replace("\r"," ")+"\n  ,"+s.Kind+" wire ["+(s.Width-1)+":0] "+s.Name);
            b.AppendLine(");");
            foreach(var s in ports.Where(s=>s.Kind=="output" && Handshake(s.Name)))
            {b.AppendLine("wire core_"+s.Name+";");b.AppendLine("assign "+s.Name+" = aresetn & core_"+s.Name+";");}
            b.Append("h_"+m.Name+" core(.clk(aclk), .rst(~aresetn)");
            foreach(var s in ports)
            {
                string value=s.Kind=="output"&&Handshake(s.Name)?"core_"+s.Name:s.Kind=="input"&&Handshake(s.Name)?"("+s.Name+" & aresetn)":s.Name;
                b.Append(",\n  ."+Module.H(s)+"("+value+")");
            }
            b.AppendLine(");\nendmodule");return b.ToString();
        }
        static bool Handshake(string n){return n.EndsWith("_tvalid")||n.EndsWith("_tready");}
    }
}
