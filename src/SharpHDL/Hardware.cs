using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpHDL
{
    // Unsigned, immutable hardware expression. C# executes construction only.
    public class Expr
    {
        public int Width { get; private set; }
        internal string Op;
        internal Expr[] Args;
        internal BigInteger Literal;
        internal int Offset;
        internal BigInteger[] Table;
        internal string Annotation;
        internal string MultiplierHint;
        public string NameHint { get; private set; }
        // Metadata only; shared expressions share the hint. Last call wins.
        public Expr Named(string name)
        {
            if(string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A net name hint must not be empty.", "name");
            NameHint=name; return this;
        }
        internal SynchronousRam Memory;
        public string Description { get { return Annotation; } }
        public Expr Describe(string text) { Annotation=text; return this; }
        internal Expr(int width, string op, params Expr[] args)
        {
            if (width < 1 || width > 4096) throw new ArgumentOutOfRangeException("width");
            Width = width; Op = op; Args = args;
        }
        public static BigInteger Mask(int width) { return (BigInteger.One << width) - 1; }
        public static Expr Const(int width, BigInteger value)
        {
            var e = new Expr(width, "const");
            if (value < 0 || value > Mask(width)) throw new ArgumentOutOfRangeException("value");
            e.Literal = value; return e;
        }
        static Expr Binary(string op, Expr a, Expr b, bool predicate = false)
        {
            if (a == null || b == null) throw new ArgumentNullException();
            if (a.Width != b.Width) throw new ArgumentException("Widths differ; use Resize explicitly.");
            return new Expr(predicate ? 1 : a.Width, op, a, b);
        }
        public static Expr operator +(Expr a, Expr b) { return Binary("+", a, b); }
        public static Expr operator -(Expr a, Expr b) { return Binary("-", a, b); }
        public static Expr operator *(Expr a, Expr b) { return Binary("*", a, b); }
        // Unsigned, combinational, toward-zero division; use Evaluate to retain status flags.
        public static Expr operator /(Expr a, Expr b) { return IntegerDivide.Evaluate(a,b).Quotient; }
        public static Expr operator %(Expr a, Expr b) { return IntegerDivide.Evaluate(a,b).Remainder.Resize(a.Width); }
        public static Expr operator &(Expr a, Expr b) { return Binary("&", a, b); }
        public static Expr operator |(Expr a, Expr b) { return Binary("|", a, b); }
        public static Expr operator ^(Expr a, Expr b) { return Binary("^", a, b); }
        public static Expr operator ~(Expr a) { return new Expr(a.Width, "~", a); }
        public static Expr operator <<(Expr a, int n) { return Shift(a, n, "<<"); }
        public static Expr operator >>(Expr a, int n) { return Shift(a, n, ">>"); }
        static Expr Shift(Expr a, int n, string op)
        {
            if (n < 0) throw new ArgumentOutOfRangeException("n");
            var e = new Expr(a.Width, op, a); e.Offset = n; return e;
        }
        public Expr Eq(Expr b) { return Binary("==", this, b, true); }
        public Expr Lt(Expr b) { return Binary("<", this, b, true); }
        public static BigInteger SignedValue(BigInteger bits, int width)
        { bits &= Mask(width); return (bits & (BigInteger.One << (width-1))) != 0 ? bits-(BigInteger.One << width) : bits; }
        public static Expr SignedConst(int width, BigInteger value)
        {
            if(value < -(BigInteger.One << (width-1)) || value >= (BigInteger.One << (width-1)))
                throw new ArgumentOutOfRangeException("value");
            return Const(width,value & Mask(width));
        }
        public Expr SignExtend(int width)
        {
            if(width < Width) throw new ArgumentException("SignExtend cannot narrow; use Resize.");
            return new Expr(width,"signextend",this);
        }
        public Expr ArithmeticShift(int bits) { return Shift(this,bits,"asr"); }
        public Expr SignedLt(Expr b) { return Binary("slt",this,b,true); }
        public static Expr Lookup(int width, Expr address, IEnumerable<BigInteger> values)
        {
            var table=values.ToArray();
            if(table.Length==0 || table.Length > 65536 || table.Length > (BigInteger.One << address.Width))
                throw new ArgumentException("Invalid table size.");
            foreach(var value in table) Const(width,value);
            var e=new Expr(width,"lookup",address); e.Table=table; return e;
        }
        public Expr Resize(int width) { return new Expr(width, "resize", this); }
        public Expr Slice(int high, int low)
        {
            if (low < 0 || high < low || high >= Width) throw new ArgumentOutOfRangeException();
            var e = new Expr(high-low+1, "slice", this); e.Offset=low; return e;
        }
        public static Expr Concat(Expr high, Expr low) { return new Expr(high.Width+low.Width, "concat", high, low); }
        public static Expr Mux(Expr condition, Expr yes, Expr no)
        {
            if (condition.Width != 1 || yes.Width != no.Width) throw new ArgumentException("Invalid mux widths.");
            return new Expr(yes.Width, "mux", condition, yes, no);
        }
    }

    public sealed class Signal : Expr
    {
        public string Name { get; private set; }
        public bool IsInput { get { return Kind=="input"; } }
        public bool IsOutput { get { return Kind=="output"; } }
        internal string Kind;
        internal Module Owner;
        internal Expr Driver;
        internal BigInteger ResetValue;
        internal Signal(Module owner, string name, int width, string kind) : base(width, "signal")
        { Owner=owner; Name=name; Kind=kind; }
    }

    public sealed class Module
    {
        public string Name { get; private set; }
        internal readonly List<Signal> Signals = new List<Signal>();
        internal readonly List<SynchronousRam> Memories = new List<SynchronousRam>();
        internal readonly List<PipelineDataflowGraph> SourceGraphs = new List<PipelineDataflowGraph>();
        internal readonly Dictionary<PipelineDataflowGraph,string> SourceGraphPrefixes = new Dictionary<PipelineDataflowGraph,string>();
        internal bool Frozen;
        public IEnumerable<Signal> Ports { get { return Signals.Where(s=>s.IsInput||s.IsOutput); } }
        // Prefix avoids HDL keywords while preserving readable generated names.
        internal static string H(Signal s) { var name="s_"+s.Name;
            // SystemVerilog strong-property keywords already begin with s_.
            return name=="s_always" || name=="s_eventually" || name=="s_nexttime" || name=="s_until" || name=="s_until_with" ? "\\"+name+" " : name; }
        static void CheckName(string n)
        {
            if (n == null || !Regex.IsMatch(n, @"\A[A-Za-z][A-Za-z0-9_]*\z"))
                throw new ArgumentException("Use a letter followed by letters, digits or underscores.");
        }
        public MemoryOptions MemoryOptions { get; }
        public Module(string name) : this(name,null) { }
        public Module(string name,MemoryOptions memoryOptions) { CheckName(name); Name=name; MemoryOptions=memoryOptions??new MemoryOptions(); }
        void Editable() { if (Frozen) throw new InvalidOperationException("Design has been frozen."); }
        Signal Add(string name, int width, string kind)
        {
            Editable(); CheckName(name);
            if (Signals.Any(s=>s.Name==name)) throw new ArgumentException("Duplicate signal name.");
            var snew=new Signal(this,name,width,kind); Signals.Add(snew); return snew;
        }
        public Signal Input(string name, int width) { return Add(name,width,"input"); }
        public Signal Output(string name, int width) { return Add(name,width,"output"); }
        public Signal Wire(string name, int width) { return Add(name,width,"wire"); }
        public Signal Register(string name, int width, BigInteger resetValue)
        {
            Expr.Const(width,resetValue);
            var s=Add(name,width,"reg"); s.ResetValue=resetValue; return s;
        }
        void Assign(Signal target, Expr value, bool sequential)
        {
            Editable();
            if (target.Owner != this || value == null || target.Width != value.Width)
                throw new ArgumentException("Invalid assignment ownership or width.");
            if (sequential ? target.Kind!="reg" : (target.Kind!="wire" && target.Kind!="output"))
                throw new ArgumentException("Wrong assignment kind.");
            if (target.Driver != null) throw new ArgumentException("Multiple drivers.");
            target.Driver=value;
        }
        public void Drive(Signal target, Expr value) { Assign(target,value,false); }
        public void Next(Signal register, Expr value) { Assign(register,value,true); }
        public void NextWhen(Signal register, Expr enable, Expr value)
        { Next(register,Expr.Mux(enable,value,register)); }
        void Visit(Expr e, HashSet<Expr> active, HashSet<Expr> done)
        {
            if (done.Contains(e)) return;
            if (!active.Add(e)) throw new InvalidOperationException("Combinational cycle.");
            var s=e as Signal;
            if (s!=null)
            {
                if (s.Owner!=this) throw new InvalidOperationException("Foreign-module signal.");
                if (s.Kind=="wire" || s.Kind=="output") Visit(s.Driver,active,done);
            }
            else foreach(var a in e.Args) Visit(a,active,done);
            active.Remove(e); done.Add(e);
        }
        public void Validate()
        {
            foreach(var s in Signals)
                if (s.Kind!="input" && s.Driver==null) throw new InvalidOperationException("Undriven signal: "+s.Name);
            var done=new HashSet<Expr>();
            foreach(var s in Signals)
            {
                Visit(s,new HashSet<Expr>(),done);
                if(s.Kind=="reg") Visit(s.Driver,new HashSet<Expr>(),done);
            }
            foreach(var memory in Memories)
                foreach(var input in new[]{memory.Address,memory.WriteEnable,memory.WriteData})Visit(input,new HashSet<Expr>(),done);
            Frozen=true;
        }
        public Simulator Simulate() { Validate(); return new Simulator(this); }
        public string DumpGraph() { Validate(); return GraphDump.Write(this); }
        public string Verilog()
        {
            Validate(); return new VerilogWriter(this).Write();
        }
    }

    public sealed partial class Simulator
    {
        readonly Module module;
        readonly Dictionary<Signal,BigInteger> state=new Dictionary<Signal,BigInteger>();
        readonly Dictionary<SynchronousRam,BigInteger[]> memories=new Dictionary<SynchronousRam,BigInteger[]>();
        internal Simulator(Module m)
        {
            module=m;
            foreach(var ram in m.Memories)memories[ram]=(BigInteger[])ram.Initial.Clone();
            foreach(var s in m.Signals) if(s.Kind=="input") state[s]=0;
            // Registers deliberately remain uninitialized until a reset tick.
        }
        public void Set(Signal input, BigInteger value)
        {
            if(input.Owner!=module || input.Kind!="input") throw new ArgumentException("Not a local input.");
            Expr.Const(input.Width,value); state[input]=value;
        }
        BigInteger Eval(Expr e, Dictionary<Expr,BigInteger> cache)
        {
            BigInteger v;
            if(cache.TryGetValue(e,out v)) return v;
            var s=e as Signal;
            if(s!=null)
            {
                if(s.Owner!=module) throw new ArgumentException("Foreign simulator signal.");
                if(s.Kind=="input" || s.Kind=="reg")
                {
                    if(!state.TryGetValue(s,out v)) throw new InvalidOperationException("Reset before reading registers.");
                }
                else v=Eval(s.Driver,cache);
            }
            else if(e.Op=="const") v=e.Literal;
            else
            {
                var a=Eval(e.Args[0],cache);
                if(e.Op=="mux") v=Eval(e.Args[a.IsZero?2:1],cache);
                else if(e.Op=="~") v=~a;
                else if(e.Op=="resize") v=a;
                else if(e.Op=="signextend") v=Expr.SignedValue(a,e.Args[0].Width);
                else if(e.Op=="asr") v=Expr.SignedValue(a,e.Args[0].Width) >> e.Offset;
                else if(e.Op=="lookup") v=a<e.Table.Length?e.Table[(int)a]:BigInteger.Zero;
                else if(e.Op=="memory") v=a<e.Memory.Depth?memories[e.Memory][(int)a]:BigInteger.Zero;
                else if(e.Op=="slice" || e.Op==">>") v=a >> e.Offset;
                else if(e.Op=="<<") v=a << e.Offset;
                else
                {
                    var b=Eval(e.Args[1],cache);
                    switch(e.Op)
                    {
                        case "+": v=a+b; break; case "-": v=a-b; break; case "*": case "dsp48e2": v=a*b; break;
                        case "&": v=a&b; break; case "|": v=a|b; break; case "^": v=a^b; break;
                        case "slt": v=Expr.SignedValue(a,e.Args[0].Width)<Expr.SignedValue(b,e.Args[1].Width)?1:0; break;
                        case "==": v=a==b?1:0; break; case "<": v=a<b?1:0; break;
                        case "concat": v=(a << e.Args[1].Width)|b; break;
                        default: throw new InvalidOperationException("Unknown expression.");
                    }
                }
            }
            v &= Expr.Mask(e.Width); cache[e]=v; return v;
        }
        public BigInteger Read(Expr expression)
        {
            // Expressions passed here should belong to this module; foreign signals fail on state lookup.
            return Eval(expression,new Dictionary<Expr,BigInteger>());
        }
        public void Tick(bool reset=false)
        {
            var pending=new Dictionary<Signal,BigInteger>();
            var cache=new Dictionary<Expr,BigInteger>();
            foreach(var s in module.Signals.Where(s=>s.Kind=="reg"))
                pending[s]=reset?s.ResetValue:Eval(s.Driver,cache);
            // Read drivers and all write inputs see pre-edge state, including other RAM outputs.
            var writes=new List<Tuple<SynchronousRam,int,BigInteger>>();
            if(!reset)foreach(var ram in module.Memories)
            {
                var address=Eval(ram.Address,cache);
                if(!Eval(ram.WriteEnable,cache).IsZero && address<ram.Depth)
                    writes.Add(Tuple.Create(ram,(int)address,Eval(ram.WriteData,cache)));
            }
            foreach(var write in writes)memories[write.Item1][write.Item2]=write.Item3;
            foreach(var pair in pending) state[pair.Key]=pair.Value;
        }
    }

    internal sealed class VerilogWriter
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
        internal VerilogWriter(Module m) { module=m; }
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
