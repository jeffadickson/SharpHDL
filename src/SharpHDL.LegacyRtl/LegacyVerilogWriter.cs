using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpHDL
{
    /// <summary>
    /// Frozen compatibility RTL backend. Kept separate from the Core IR
    /// backend during migration so existing Verilog and downstream tests
    /// remain byte-for-byte stable.
    /// </summary>
    internal sealed class LegacyVerilogWriter
    {
        readonly Module module;
        readonly Dictionary<Expr,string> names=new Dictionary<Expr,string>();
        readonly StringBuilder declarations=new StringBuilder();
        readonly StringBuilder assignments=new StringBuilder();
        int serial;
        readonly HashSet<string> usedNames=new HashSet<string>(StringComparer.Ordinal);
        static string Identifier(string hint)
        {
            string safe=Regex.Replace(hint, "[^A-Za-z0-9_]", "_");
            return safe.Substring(0,Math.Min(64,safe.Length));
        }
        static string Operation(Expr e)
        {
            switch(e.Op) {
                case "+": return "add"; case "-": return "sub"; case "*": return "mul";
                case "&": return "and"; case "|": return "or"; case "^": return "xor";
                case "~": return "not"; case "==": return "eq"; case "<": return "lt";
                case "<<": return "shl"; case ">>": return "shr";
                case "slice": return "bits_"+(e.Offset+e.Width-1)+"_"+e.Offset;
                default: return Identifier(e.Op);
            }
        }
        string NetName(Expr e)
        {
            string stem="n_"+(e.NameHint==null ? Operation(e)+"_"+(serial++) : Identifier(e.NameHint));
            string name=stem; int suffix=2;
            while(!usedNames.Add(name)) name=stem+"_"+(suffix++);
            return name;
        }
        internal LegacyVerilogWriter(Module m) { module=m; }
        static string Comment(string text) { return (text ?? "").Replace("\r", " ").Replace("\n", " ").Replace("\u2028", " ").Replace("\u2029", " "); }
        static string Meaning(Expr e)
        {
            string detail=e.Annotation;
            if(string.IsNullOrEmpty(detail)) {
                if(e.Op=="concat") detail="Packed bus: ["+(e.Width-1)+":"+e.Args[1].Width+"] = high operand; ["+(e.Args[1].Width-1)+":0] = low operand.";
                else if(e.Op=="slice") detail="Extract source bits ["+(e.Offset+e.Width-1)+":"+e.Offset+"], rebase to bit 0.";
                else if(e.Op=="resize") detail=e.Width<e.Args[0].Width?"Narrow: retain low bits; upper bits discarded.":"Unsigned resize: zero extend (or preserve width).";
                else if(e.Op=="signextend") detail="Signed two's-complement extension.";
                else if(e.Op=="lookup") detail="Constant ROM: "+e.Table.Length+" entries; unsigned address; out-of-range reads return zero.";
                else if(e.Op=="mux") detail="Select true operand when condition is 1; false operand otherwise.";
                else detail="Operation '"+e.Op+"'; result limited to "+e.Width+" bits. Signed interpretation only for slt/asr/signextend.";
            }
            return Comment(detail);
        }
        static string SignalMeaning(Signal s) { return Comment(s.Annotation ?? (s.Kind=="reg" ? "State register; synchronous active-high reset value "+s.ResetValue+"; updates at rising clk." : "Unsigned "+s.Width+"-bit "+s.Kind+"; bit 0 is least significant.")); }
        static string Range(int width) { return "["+(width-1)+":0] "; }
        string Emit(Expr e)
        {
            var s=e as Signal;
            if(s!=null) return Module.H(s);
            if(e.Op=="const") return e.Width+"'d"+e.Literal;
            string name;
            if(names.TryGetValue(e,out name)) return name;
            var args=e.Args.Select(Emit).ToArray();
            name=NetName(e); names[e]=name;
            if(e.Op=="dsp48e2")
            {
                declarations.AppendLine("// "+Meaning(e));
                declarations.AppendLine("wire "+Range(e.Width)+name+";");
                assignments.AppendLine("sharphdl_dsp48e2_mul_unsigned #(.A_WIDTH("+
                    e.Args[0].Width+"), .B_WIDTH("+e.Args[1].Width+")) dsp_"+name+
                    " (.clk(clk), .a("+args[0]+"), .b("+args[1]+"), .product("+name+"));");
                return name;
            }
            string rhs;
            switch(e.Op)
            {
                case "mux": rhs=args[0]+" ? "+args[1]+" : "+args[2]; break;
                case "resize": rhs=args[0]; break;
                case "signextend": rhs="$signed("+args[0]+")"; break;
                case "asr": rhs="$signed("+args[0]+") >>> "+e.Offset; break;
                case "slt": rhs="$signed("+args[0]+") < $signed("+args[1]+")"; break;
                case "memory": rhs="mem_"+e.Memory.Name+"["+args[0]+"]"; break;
                case "lookup":
                    declarations.AppendLine("// "+Meaning(e));
                    declarations.AppendLine("wire "+Range(e.Width)+name+";");
                    declarations.AppendLine("function "+Range(e.Width)+"rom_"+name+";");
                    declarations.AppendLine("  input "+Range(e.Args[0].Width)+"address;");
                    declarations.AppendLine("  begin\n    case (address)");
                    for(int i=0;i<e.Table.Length;i++)
                        declarations.AppendLine("      "+e.Args[0].Width+"'d"+i+": rom_"+name+" = "+e.Width+"'d"+e.Table[i]+";");
                    declarations.AppendLine("      default: rom_"+name+" = "+e.Width+"'d0;\n    endcase\n  end\nendfunction");
                    assignments.AppendLine("assign "+name+" = rom_"+name+"("+args[0]+");");
                    return name;
                // Shift avoids invalid part-selects of numeric literals.
                case "slice": case ">>": rhs=args[0]+" >> "+e.Offset; break;
                case "<<": rhs=args[0]+" << "+e.Offset; break;
                case "~": rhs="~"+args[0]; break;
                case "concat": rhs="{"+args[0]+", "+args[1]+"}"; break;
                default: rhs=args[0]+" "+e.Op+" "+args[1]; break;
            }
            // Each node gets its own sized wire: preserve intermediate truncation.
            declarations.AppendLine("// "+Meaning(e));
            if(e.Op=="*" && e.MultiplierHint=="yes")
                declarations.AppendLine("(* use_dsp = \"yes\" *)");
            declarations.AppendLine("wire "+Range(e.Width)+name+";");
            assignments.AppendLine("assign "+name+" = "+rhs+";");
            return name;
        }
        internal string Write()
        {
            var ports=module.Signals.Where(s=>s.Kind=="input" || s.Kind=="output").ToList();
            var b=new StringBuilder("// Generated by SharpHDL 0.6; explicit bit vectors, single clock.\n// clk: rising-edge clock. rst: synchronous active-high reset.\n// Stream transfer occurs only when TVALID && TREADY at a rising edge.\n// Hold data, keep, last and user stable while valid && !ready.\n// Numeric temporaries are explicitly sized to preserve truncation semantics.\n");
            b.AppendLine("module h_"+module.Name+"(");
            b.Append("  input wire clk,\n  input wire rst");
            foreach(var s in ports) b.Append(",\n  // "+SignalMeaning(s)+"\n  "+s.Kind+" wire "+Range(s.Width)+Module.H(s));
            b.AppendLine("\n);");
            foreach(var s in module.Signals.Where(s=>s.Kind=="reg" || s.Kind=="wire"))
                b.AppendLine("// "+SignalMeaning(s)+"\n"+(s.Kind=="reg"?"reg ":"wire ")+Range(s.Width)+Module.H(s)+";");
            var driverNames=new Dictionary<Signal,string>();
            foreach(var s in module.Signals.Where(s=>s.Kind!="input")) driverNames[s]=Emit(s.Driver);
            var memoryPorts=new Dictionary<SynchronousRam,string[]>();
            foreach(var ram in module.Memories)
            {
                memoryPorts[ram]=new[]{Emit(ram.Address),Emit(ram.WriteEnable),Emit(ram.WriteData)};
                b.AppendLine("// RAM "+ram.Name+": "+ram.Depth+" words x "+ram.Width+" bits; synchronous read-before-write; independent enables.");
                b.AppendLine("// Configuration initialization below; reset retains contents and suppresses writes. Out-of-range writes ignored.");
                if(ram.Options.Target!=MemoryTarget.Generic)
                {
                    b.AppendLine("// Memory target: "+ram.Options+"; old-data collisions remain required. No no_rw_check relaxation.");
                    if(ram.Options.Attribute!=null)b.AppendLine(ram.Options.Attribute);
                }
                b.AppendLine("reg "+Range(ram.Width)+"mem_"+ram.Name+" [0:"+(ram.Depth-1)+"];\ninitial begin");
                for(int i=0;i<ram.Depth;i++)b.AppendLine("  mem_"+ram.Name+"["+i+"] = "+ram.Width+"'d"+ram.Initial[i]+";");
                b.AppendLine("end");
            }
            b.Append(declarations); b.Append(assignments);
            foreach(var ram in module.Memories)
            {
                var p=memoryPorts[ram];
                b.AppendLine("always @(posedge clk) begin\n  if (!rst && "+p[1]+" && ({1'b0,"+p[0]+"} < "+(ram.Address.Width+1)+"'d"+ram.Depth+"))\n    mem_"+ram.Name+"["+p[0]+"] <= "+p[2]+";\nend");
            }
            foreach(var s in module.Signals.Where(s=>s.Kind=="output" || s.Kind=="wire"))
                b.AppendLine("assign "+Module.H(s)+" = "+driverNames[s]+";");
            var regs=module.Signals.Where(s=>s.Kind=="reg").ToList();
            if(regs.Count>0)
            {
                b.AppendLine("always @(posedge clk) begin\n  if (rst) begin");
                foreach(var s in regs) b.AppendLine("    "+Module.H(s)+" <= "+s.Width+"'d"+s.ResetValue+";");
                b.AppendLine("  end else begin");
                foreach(var s in regs) b.AppendLine("    "+Module.H(s)+" <= "+driverNames[s]+";");
                b.AppendLine("  end\nend");
            }
            b.AppendLine("endmodule"); return b.ToString();
        }
    }
}
