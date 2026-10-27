using System;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpHDL
{
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
    public sealed class PortWidthAttribute : Attribute
    {
        public int Width { get; private set; }
        public PortWidthAttribute(int width) { Width=width; }
    }
    public sealed class CilOptions
    {
        public int MaxInstructions=20000;
        public int MaxBranchDepth=128;
        public int MaxCallDepth=16;
    }
    public sealed class CilResult
    {
        public Expr Value { get; internal set; }
        // Value is unspecified when Fault is high. No CLR exception handler is synthesized.
        public Expr Fault { get; internal set; }
        public string Report { get; internal set; }
        public Expr AssertionFailed { get; internal set; }
    }
    public static partial class CilCompiler
    {
        sealed class Instruction { public int Offset,Next;public string Name;public object Operand; }
        sealed class State
        {
            public Expr[] Args,Locals;public List<Expr> Stack=new List<Expr>();public Expr Fault=K(1,0);public Expr AssertionFailed=K(1,0);
            public State Copy() { return new State{Args=(Expr[])Args.Clone(),Locals=(Expr[])Locals.Clone(),Stack=new List<Expr>(Stack),Fault=Fault,AssertionFailed=AssertionFailed}; }
            public Expr Pop(){if(Stack.Count==0)throw new NotSupportedException("Invalid empty CIL stack.");var v=Stack[Stack.Count-1];Stack.RemoveAt(Stack.Count-1);return v;}
            public void Push(Expr x){Stack.Add(x);}
        }
        sealed class Context
        {
            public CilOptions Options;public int Steps,Branches,Calls;public HashSet<MethodInfo> Active=new HashSet<MethodInfo>();
            public StringBuilder Report=new StringBuilder();
        }
        static readonly Dictionary<ushort,OpCode> Codes=typeof(OpCodes).GetFields(BindingFlags.Static|BindingFlags.Public)
            .Where(x=>x.FieldType==typeof(OpCode)).Select(x=>(OpCode)x.GetValue(null)).ToDictionary(x=>(ushort)x.Value);
        static Expr K(int w,BigInteger x){return Expr.Const(w,x&Expr.Mask(w));}
        static bool Constant(Expr x){return x.Op=="const";}
        static bool Supported(Type t){return t==typeof(int)||t==typeof(uint)||t==typeof(bool)||PackedLayout.IsBits(t)||PackedLayout.IsRecord(t);}
        static bool Signed(Type t){return t==typeof(int);}
        static Exception Error(MethodInfo m,int offset,string reason)
        {return CilDiagnostic.CreateException(m,offset,reason);}
        static int PortWidth(ParameterInfo p)
        {
            if(!Supported(p.ParameterType))throw new NotSupportedException("CIL port type must be Int32, UInt32 or Boolean: "+p.Name);
            var a=(PortWidthAttribute)Attribute.GetCustomAttribute(p,typeof(PortWidthAttribute));
            if(PackedLayout.IsBits(p.ParameterType)||PackedLayout.IsRecord(p.ParameterType)){if(a!=null)throw new ArgumentException("Packed types define their own width.");return PackedLayout.Width(p.ParameterType);}
            int width=a==null?(p.ParameterType==typeof(bool)?1:32):a.Width;
            if(width<1||width>32 || p.ParameterType==typeof(bool)&&width!=1)throw new ArgumentException("PortWidth must be 1..32, and Boolean width must be 1.");
            return width;
        }
        static Expr Resize(Expr x,int width,bool signed=false)
        {
            if(width==x.Width)return x;
            if(Constant(x))return K(width,signed?Expr.SignedValue(x.Literal,x.Width):x.Literal);
            return signed&&width>x.Width?x.SignExtend(width):x.Resize(width);
        }
        static Expr Unary(string op,Expr x)
        {
            if(Constant(x))return K(x.Width,op=="not"?~x.Literal:-x.Literal);
            return op=="not"?~x:K(x.Width,0)-x;
        }
        static Expr Bin(string op,Expr a,Expr b)
        {
            if(a.Width!=b.Width)throw new NotSupportedException("Mismatched CIL value widths.");
            if(Constant(a)&&Constant(b))
            {
                BigInteger av=a.Literal,bv=b.Literal,v;
                switch(op){case "add":v=av+bv;break;case "sub":v=av-bv;break;case "mul":v=av*bv;break;
                    case "and":v=av&bv;break;case "or":v=av|bv;break;default:v=av^bv;break;}
                return K(a.Width,v);
            }
            if(op=="mul")
            {
                var constant=Constant(a)?a:Constant(b)?b:null;
                if(constant!=null && constant.Literal>0 && (constant.Literal&(constant.Literal-1))==0)
                {int n=0;var v=constant.Literal;while(v>1){v>>=1;n++;}return (Constant(a)?b:a)<<n;}
            }
            switch(op){case "add":return a+b;case "sub":return a-b;case "mul":return a*b;case "and":return a&b;case "or":return a|b;default:return a^b;}
        }
        static Expr Test(string op,Expr a,Expr b,bool unsigned=false)
        {
            if(Constant(a)&&Constant(b))
            {
                var av=unsigned?a.Literal:Expr.SignedValue(a.Literal,a.Width);
                var bv=unsigned?b.Literal:Expr.SignedValue(b.Literal,b.Width);
                return K(1,(op=="eq"?av==bv:op=="lt"?av<bv:av>bv)?1:0);
            }
            return op=="eq"?a.Eq(b):op=="gt"?Test("lt",b,a,unsigned):unsigned?a.Lt(b):a.SignedLt(b);
        }
        static Expr Not(Expr x){return Unary("not",x);}
        static Expr Or(Expr a,Expr b)
        {if(Constant(a))return a.Literal.IsZero?b:a;if(Constant(b))return b.Literal.IsZero?a:b;return a|b;}
        static Expr Select(Expr c,Expr a,Expr b)
        {if(Constant(c))return c.Literal.IsZero?b:a;if(Object.ReferenceEquals(a,b))return a;return Expr.Mux(c,a,b);}
        static Expr Shift(Expr a,Expr b,string op)
        {
            if(Constant(b))
            {
                int n=(int)(b.Literal&31);
                if(Constant(a))return K(32,op=="shl"?a.Literal<<n:op=="shr"?Expr.SignedValue(a.Literal,32)>>n:a.Literal>>n);
                return op=="shl"?a<<n:op=="shr"?a.ArithmeticShift(n):a>>n;
            }
            Expr value=a;
            for(int i=0;i<5;i++)value=Select(b.Slice(i,i),op=="shl"?value<<(1<<i):op=="shr"?value.ArithmeticShift(1<<i):value>>(1<<i),value);
            return value;
        }
        static List<Instruction> Decode(MethodInfo method)
        {
            var body=method.GetMethodBody();if(body==null)throw Error(method,0,"Method has no CIL body.");
            var bytes=body.GetILAsByteArray();var list=new List<Instruction>();int i=0;
            while(i<bytes.Length)
            {
                var ins=new Instruction{Offset=i};ushort value=bytes[i++];if(value==0xfe)value=(ushort)(0xfe00|bytes[i++]);
                OpCode code;if(!Codes.TryGetValue(value,out code))throw Error(method,ins.Offset,"Unknown opcode.");ins.Name=code.Name;
                switch(code.OperandType)
                {
                    case OperandType.InlineNone:break;
                    case OperandType.ShortInlineI:ins.Operand=(int)(sbyte)bytes[i++];break;
                    case OperandType.ShortInlineVar:ins.Operand=(int)bytes[i++];break;
                    case OperandType.InlineVar:ins.Operand=(int)BitConverter.ToUInt16(bytes,i);i+=2;break;
                    case OperandType.InlineI:ins.Operand=BitConverter.ToInt32(bytes,i);i+=4;break;
                    case OperandType.ShortInlineBrTarget:ins.Operand=i+1+(sbyte)bytes[i];i++;break;
                    case OperandType.InlineBrTarget:ins.Operand=i+4+BitConverter.ToInt32(bytes,i);i+=4;break;
                    case OperandType.InlineSwitch:
                        int count=BitConverter.ToInt32(bytes,i);i+=4;int origin=i+4*count;var targets=new int[count];
                        for(int n=0;n<count;n++){targets[n]=origin+BitConverter.ToInt32(bytes,i);i+=4;}ins.Operand=targets;break;
                    case OperandType.InlineField:
                        ins.Operand=method.Module.ResolveField(BitConverter.ToInt32(bytes,i));i+=4;break;
                    case OperandType.InlineMethod:
                        ins.Operand=method.Module.ResolveMethod(BitConverter.ToInt32(bytes,i));i+=4;break;
                    default:throw Error(method,ins.Offset,"Unsupported operand/opcode "+ins.Name+" ("+code.OperandType+").");
                }
                ins.Next=i;list.Add(ins);
            }
            return list;
        }
        public static string Disassemble(MethodInfo method)
        {
            var text=new StringBuilder(method.DeclaringType.FullName+"."+method.Name+"\n");
            foreach(var i in Decode(method))text.AppendLine("IL_"+i.Offset.ToString("x4")+": "+i.Name+" "+(i.Operand is int[]?String.Join(",",(int[])i.Operand):i.Operand));
            return text.ToString();
        }
        public static CilResult Compile(MethodInfo method,Expr[] arguments,CilOptions options=null)
        {
            var ctx=new Context{Options=options??new CilOptions()};
            if(ctx.Options.MaxInstructions<1||ctx.Options.MaxBranchDepth<1||ctx.Options.MaxCallDepth<1)throw new ArgumentException("CIL limits must be positive.");
            var ps=method.GetParameters();if(arguments.Length!=ps.Length)throw new ArgumentException("CIL argument count mismatch.");
            var args=new Expr[ps.Length];
            for(int i=0;i<args.Length;i++)
            {int w=PortWidth(ps[i]);if(arguments[i].Width!=w)throw new ArgumentException("CIL argument width mismatch: "+ps[i].Name);args[i]=PackedLayout.IsRecord(ps[i].ParameterType)?arguments[i]:Resize(arguments[i],32,Signed(ps[i].ParameterType));}
            var result=Method(ctx,method,args,0);
            result.Value=Resize(result.Value,PortWidth(method.ReturnParameter));
            result.Value.Describe("Behavioral CIL result from "+method.DeclaringType.FullName+"."+method.Name+"; native 32-bit scalars, explicit UBits operations wrap to their declared width; output explicitly narrowed to "+result.Value.Width+" bits. Undefined when fault=1.");
            result.Fault.Describe("CIL arithmetic fault: checked overflow, divide exception, or assertion failure on the selected execution path. Reset/exception handlers are not synthesized.");
            result.Report=ctx.Report+"Instructions visited: "+ctx.Steps+"; symbolic branches: "+ctx.Branches+"; method bodies: "+ctx.Calls+"\n";
            return result;
        }
        public static Module BuildModule(MethodInfo method,string name,CilOptions options=null)
        {
            var m=new Module(name);var args=method.GetParameters().Select((p,i)=>(Expr)m.Input("arg"+i,PortWidth(p)).Describe(p.Name+": "+PackedLayout.Describe(p.ParameterType))).ToArray();
            var result=Compile(method,args,options);var output=m.Output("result",result.Value.Width);var fault=m.Output("fault",1);
            output.Describe("Return value; invalid when fault=1.");fault.Describe("Selected-path arithmetic or assertion fault; no exception unwinding.");m.Drive(output,result.Value);m.Drive(fault,result.Fault);var assertion=m.Output("assertion_failed",1);assertion.Describe("Selected Hardware.Assert failed; also sets fault. Combinational status, not latched.");m.Drive(assertion,result.AssertionFailed);return m;
        }
        static CilResult Method(Context ctx,MethodInfo method,Expr[] args,int callDepth)
        {
            if(!method.IsStatic||method.ContainsGenericParameters||!Supported(method.ReturnType)||method.GetParameters().Any(p=>!Supported(p.ParameterType)))
                throw Error(method,0,"Only static methods with supported scalar, UBits, or explicit packed-record types are supported.");
            if(callDepth>=ctx.Options.MaxCallDepth||!ctx.Active.Add(method))throw Error(method,0,"Recursive call or call depth limit.");
            try
            {
                var body=method.GetMethodBody();if(body==null||body.ExceptionHandlingClauses.Count!=0)throw Error(method,0,"Missing body or exception handlers are unsupported.");
                if(body.LocalVariables.Any(v=>!Supported(v.LocalType)))throw Error(method,0,"Unsupported local type.");
                var code=Decode(method).ToDictionary(i=>i.Offset);ctx.Report.AppendLine(Disassemble(method));ctx.Calls++;
                var state=new State{Args=(Expr[])args.Clone(),Locals=body.LocalVariables.Select(v=>body.InitLocals?K(PackedLayout.IsRecord(v.LocalType)?PackedLayout.Width(v.LocalType):32,0):null).ToArray()};
                return Run(ctx,method,code,state,0,0,callDepth);
            }
            finally{ctx.Active.Remove(method);}
        }
        static CilResult Run(Context ctx,MethodInfo method,Dictionary<int,Instruction> code,State state,int pc,int depth,int callDepth)
        {
            if(depth>ctx.Options.MaxBranchDepth)throw Error(method,pc,"Symbolic branch depth limit; simplify or explicitly pipeline the design.");
            while(true)
            {
                if(++ctx.Steps>ctx.Options.MaxInstructions)throw Error(method,pc,"Instruction budget exceeded; loop may depend on runtime data or graph may be too large.");
                Instruction ins;if(!code.TryGetValue(pc,out ins))throw Error(method,pc,"Invalid branch destination.");
                string op=ins.Name;int at=pc;pc=ins.Next;
                if(op=="nop")continue;
                if(op.StartsWith("ldc.i4"))
                {state.Push(K(32,op=="ldc.i4.m1"?-1:op=="ldc.i4"||op=="ldc.i4.s"?(int)ins.Operand:int.Parse(op.Substring(7))));continue;}
                if(op=="ldarga"||op=="ldarga.s"){state.Push(state.Args[(int)ins.Operand]);continue;}
                if(op=="ldloca"||op=="ldloca.s"){var local=state.Locals[(int)ins.Operand];if(local==null)throw Error(method,at,"Uninitialized local.");state.Push(local);continue;}
                if(op=="ldfld"){var f=(FieldInfo)ins.Operand;if(!PackedLayout.IsRecord(f.DeclaringType))throw Error(method,at,"Field is not a hardware record field.");state.Push(PackedLayout.Read(state.Pop(),f));continue;}
                if(op=="ldarg"||op=="ldarg.s"||op.StartsWith("ldarg."))
                {int i=op=="ldarg"||op=="ldarg.s"?(int)ins.Operand:int.Parse(op.Substring(6));state.Push(state.Args[i]);continue;}
                if(op=="starg"||op=="starg.s"){state.Args[(int)ins.Operand]=state.Pop();continue;}
                if(op=="ldloc"||op=="ldloc.s"||op.StartsWith("ldloc."))
                {int i=op=="ldloc"||op=="ldloc.s"?(int)ins.Operand:int.Parse(op.Substring(6));if(state.Locals[i]==null)throw Error(method,at,"Uninitialized local.");state.Push(state.Locals[i]);continue;}
                if(op=="stloc"||op=="stloc.s"||op.StartsWith("stloc."))
                {int i=op=="stloc"||op=="stloc.s"?(int)ins.Operand:int.Parse(op.Substring(6));state.Locals[i]=state.Pop();continue;}
                if(op=="dup"){var x=state.Pop();state.Push(x);state.Push(x);continue;}
                if(op=="pop"){state.Pop();continue;}
                if(op=="ret")
                {var value=state.Pop();if(state.Stack.Count!=0)throw Error(method,at,"Nonempty return stack.");return new CilResult{Value=value,Fault=state.Fault,AssertionFailed=state.AssertionFailed};}
                if(op=="not"||op=="neg"){state.Push(Unary(op,state.Pop()));continue;}
                if(op=="conv.i4"||op=="conv.u4")continue;
                if(op=="conv.i1"||op=="conv.u1"||op=="conv.i2"||op=="conv.u2")
                {var x=state.Pop();int w=op.EndsWith("1")?8:16;state.Push(Resize(Resize(x,w),32,op[5]=='i'));continue;}
                if(op=="ceq"||op=="clt"||op=="clt.un"||op=="cgt"||op=="cgt.un")
                {var b=state.Pop();var a=state.Pop();state.Push(Resize(Test(op.Substring(1,2),a,b,op.EndsWith(".un")),32));continue;}
                if(op=="shl"||op=="shr"||op=="shr.un")
                {var b=state.Pop();var a=state.Pop();state.Push(Shift(a,b,op));continue;}
                if(op=="and"||op=="or"||op=="xor"||op.StartsWith("add")||op.StartsWith("sub")||op.StartsWith("mul"))
                {
                    string basic=op.Split('.')[0];var b=state.Pop();var a=state.Pop();var value=Bin(basic,a,b);
                    if(op.Contains("ovf"))
                    {bool signed=!op.EndsWith(".un");var wide=Bin(basic,Resize(a,64,signed),Resize(b,64,signed));var fits=Test("eq",wide,Resize(value,64,signed));state.Fault=Or(state.Fault,Not(fits));}
                    state.Push(value.Named(method.Name+"_IL_"+at.ToString("x4")+"_"+op).Describe(method.Name+" IL_"+at.ToString("x4")+" "+op+"; native 32-bit arithmetic."));continue;
                }
                if(op=="div"||op=="div.un"||op=="rem"||op=="rem.un")
                {
                    var b=state.Pop();var a=state.Pop();bool signed=!op.EndsWith(".un");
                    if(Constant(b)&&b.Literal>0&&(!signed||b.Literal<(BigInteger.One<<31))&&(b.Literal&(b.Literal-1))==0)
                    {
                        int n=0;var divisor=b.Literal;while(divisor>1){divisor>>=1;n++;}
                        Expr q=signed?(a+Select(a.Slice(31,31),K(32,b.Literal-1),K(32,0))).ArithmeticShift(n):a>>n;
                        state.Push(op.StartsWith("div")?q:a-(q<<n));
                        ctx.Report.AppendLine("Strength reduction IL_"+at.ToString("x4")+": "+op+" by "+b.Literal+(signed?" -> bias plus arithmetic shift (toward zero).":" -> logical shift."));continue;
                    }
                    var result=IntegerDivide.Evaluate(a,b,DivisionRounding.TowardZero,signed);
                    state.Fault=Or(state.Fault,Or(result.DivideByZero,result.Overflowed));
                    state.Push(op.StartsWith("div")?result.Quotient:Resize(result.Remainder,32));continue;
                }
                if(op=="call")
                {
                    var callee=ins.Operand as MethodInfo;
                    if(callee!=null&&callee.DeclaringType==typeof(Hardware)&&callee.Name=="Assert")
                    {var failed=Test("eq",state.Pop(),K(32,0));state.AssertionFailed=Or(state.AssertionFailed,failed);state.Fault=Or(state.Fault,failed);continue;}
                    Expr intrinsic;
                    if(callee!=null&&PackedLayout.IsBits(callee.DeclaringType))
                    {var values=new Expr[callee.GetParameters().Length];for(int i=values.Length-1;i>=0;i--)values[i]=state.Pop();intrinsic=BitsCall(callee,values);state.Push(intrinsic);continue;}
                    if(callee==null||callee.Module.Assembly!=method.Module.Assembly)throw Error(method,at,"Only static helpers in the same assembly may be inlined.");
                    var args=new Expr[callee.GetParameters().Length];for(int i=args.Length-1;i>=0;i--)args[i]=state.Pop();
                    var result=Method(ctx,callee,args,callDepth+1);state.Push(result.Value);state.Fault=Or(state.Fault,result.Fault);state.AssertionFailed=Or(state.AssertionFailed,result.AssertionFailed);continue;
                }
                if(op=="br"||op=="br.s"){pc=(int)ins.Operand;continue;}
                if(op=="switch")
                {
                    var value=state.Pop();var targets=(int[])ins.Operand;
                    if(Constant(value)){pc=value.Literal<targets.Length?targets[(int)value.Literal]:pc;continue;}
                    if(targets.Any(t=>t<=at))throw Error(method,at,"Runtime-dependent backward switch is unsupported.");
                    var result=Run(ctx,method,code,state.Copy(),pc,depth+1,callDepth);
                    for(int i=targets.Length-1;i>=0;i--)
                    {var branch=Run(ctx,method,code,state.Copy(),targets[i],depth+1,callDepth);var c=Test("eq",value,K(32,i));result=new CilResult{Value=Select(c,branch.Value,result.Value),Fault=Select(c,branch.Fault,result.Fault),AssertionFailed=Select(c,branch.AssertionFailed,result.AssertionFailed)};}
                    ctx.Branches+=targets.Length;return result;
                }
                if(op.StartsWith("brtrue")||op.StartsWith("brfalse")||op.StartsWith("beq")||op.StartsWith("bne")||op.StartsWith("blt")||op.StartsWith("bgt")||op.StartsWith("ble")||op.StartsWith("bge"))
                {
                    Expr c;
                    if(op.StartsWith("brtrue")||op.StartsWith("brfalse"))
                    {c=Test("eq",state.Pop(),K(32,0));if(op.StartsWith("brtrue"))c=Not(c);}
                    else
                    {var b=state.Pop();var a=state.Pop();bool u=op.Contains(".un");string cond=op.Substring(0,3);c=Test(cond=="beq"||cond=="bne"?"eq":cond=="blt"||cond=="bge"?"lt":"gt",a,b,u);if(cond=="bne"||cond=="ble"||cond=="bge")c=Not(c);}
                    int target=(int)ins.Operand;
                    if(Constant(c)){if(!c.Literal.IsZero)pc=target;continue;}
                    if(target<=at)throw Error(method,at,"Runtime-dependent backward branch is unsupported; use a static bound or BuildSequential.");
                    ctx.Branches++;var yes=Run(ctx,method,code,state.Copy(),target,depth+1,callDepth);var no=Run(ctx,method,code,state.Copy(),pc,depth+1,callDepth);
                    return new CilResult{Value=Select(c,yes.Value,no.Value),Fault=Select(c,yes.Fault,no.Fault),AssertionFailed=Select(c,yes.AssertionFailed,no.AssertionFailed)};
                }
                throw Error(method,at,"Unsupported opcode "+op+". No host execution fallback.");
            }
        }
    }
}
