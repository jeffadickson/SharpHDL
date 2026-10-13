using System;
using System.Linq;
using System.Collections.Generic;
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
            if(ports.Any(s=>s.Name=="aclk" || s.Name=="aresetn"))
                throw new ArgumentException("AXI wrapper reserves port names aclk and aresetn.");
            var used=new HashSet<string>(ports.Select(s=>s.Name));
            used.Add("aclk");used.Add("aresetn");
            Func<string,string> fresh=stem=>{var n=stem;int i=1;while(!used.Add(n))n=stem+"_"+(i++);return n;};
            var coreNames=ports.Where(s=>s.Kind=="output" && Handshake(s.Name)).ToDictionary(s=>s.Name,s=>fresh("core_"+s.Name));
            var instance=fresh("core");
            var b=new StringBuilder("// AXI4-Stream subset: TDATA/TKEEP/TLAST/TUSER/TVALID/TREADY.\n");
            b.AppendLine("// aclk: rising-edge clock. aresetn: active-low; assert through a clock edge to reset the synchronous core.\n// Reset masks handshakes immediately; no reset synchronizer is included.");
            b.AppendLine("module h_"+m.Name+"_axi(\n  input wire aclk,\n  input wire aresetn");
            // Complete the header's final comma-list before closing.
            foreach(var s in ports)b.AppendLine("  // "+(s.Annotation ?? (s.Width+"-bit port, bit 0 least significant.")).Replace("\n"," ").Replace("\r"," ")+"\n  ,"+s.Kind+" wire ["+(s.Width-1)+":0] "+Id(s.Name).TrimEnd());
            b.AppendLine(");");
            foreach(var s in ports.Where(s=>s.Kind=="output" && Handshake(s.Name)))
            {b.AppendLine("wire "+coreNames[s.Name]+";");b.AppendLine("assign "+Id(s.Name)+" = aresetn & "+coreNames[s.Name]+";");}
            b.Append("h_"+m.Name+" "+instance+"(.clk(aclk), .rst(~aresetn)");
            foreach(var s in ports)
            {
                string value=s.Kind=="output"&&Handshake(s.Name)?coreNames[s.Name]:s.Kind=="input"&&Handshake(s.Name)?"("+Id(s.Name)+" & aresetn)":Id(s.Name);
                b.Append(",\n  ."+Module.H(s)+"("+value+")");
            }
            b.AppendLine(");\nendmodule");return b.ToString();
        }
        static string Id(string name){return "\\"+name+" ";} // Escaped identifiers retain the public port name, including keywords.
        static bool Handshake(string n){return n.EndsWith("_tvalid")||n.EndsWith("_tready");}
    }
}
