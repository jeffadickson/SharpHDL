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
        /// <summary>Export the supported design subset into the validated canonical Core IR.</summary>
        public CoreGraphExport ExportCoreGraph() { return CoreGraphLowering.Export(this); }
        public string DumpGraph() { Validate(); return GraphDump.Write(this); }
        public string Verilog()
        {
            Validate(); return new LegacyVerilogWriter(this).Write();
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

}
