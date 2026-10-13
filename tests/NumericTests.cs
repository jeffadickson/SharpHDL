using System;
using System.IO;
using System.Numerics;
using System.Text;
using SharpHDL;

static class NumericTests
{
    static int count;static double worst;
    static void Check(bool okay,string message) { count++;if(!okay)throw new Exception(message); }
    // Independent exact rational oracle for basic arithmetic; does not use the hardware packer.
    struct Rational
    {
        public BigInteger N,D;
        public Rational(BigInteger n,BigInteger d) { if(d<0){n=-n;d=-d;}N=n;D=d; }
    }
    static Rational Decode(FloatFormat f,BigInteger bits)
    {
        int e=(int)((bits>>f.FractionBits)&f.MaxExponent);
        if(e==0)return new Rational(0,1);
        var n=(bits&Expr.Mask(f.FractionBits))+(BigInteger.One<<f.FractionBits);
        if((bits&(BigInteger.One<<(f.Width-1)))!=0)n=-n;
        int shift=e-f.Bias-f.FractionBits;
        return shift<0?new Rational(n,BigInteger.One<<-shift):new Rational(n<<shift,1);
    }
    static BigInteger Encode(FloatFormat f,Rational r)
    {
        if(r.D.IsZero)return f.NaR;if(r.N.IsZero)return 0;
        var sign=r.N.Sign<0?BigInteger.One<<(f.Width-1):BigInteger.Zero;
        var n=BigInteger.Abs(r.N);var d=r.D;int exponent=0;
        while(n<d){n<<=1;exponent--;}
        while(n>=2*d){d<<=1;exponent++;}
        BigInteger remainder;var q=BigInteger.DivRem(n<<f.FractionBits,d,out remainder);
        if(2*remainder>d || 2*remainder==d&&!q.IsEven)q++;
        if(q==(BigInteger.One<<(f.FractionBits+1))){q>>=1;exponent++;}
        int biased=exponent+f.Bias;
        if(biased<=0)return 0;if(biased>=f.MaxExponent)return sign|f.MaxFinite;
        return sign|((BigInteger)biased<<f.FractionBits)|(q&Expr.Mask(f.FractionBits));
    }
    static BigInteger Oracle(FloatFormat f,BigInteger aa,BigInteger bb,string op)
    {
        if(((aa>>f.FractionBits)&f.MaxExponent)==f.MaxExponent||((bb>>f.FractionBits)&f.MaxExponent)==f.MaxExponent)return f.NaR;
        var a=Decode(f,aa);var b=Decode(f,bb);
        if(op=="add")return Encode(f,new Rational(a.N*b.D+b.N*a.D,a.D*b.D));
        if(op=="sub")return Encode(f,new Rational(a.N*b.D-b.N*a.D,a.D*b.D));
        if(op=="mul")return Encode(f,new Rational(a.N*b.N,a.D*b.D));
        return Encode(f,new Rational(a.N*b.D,a.D*b.N));
    }
    sealed class Bench
    {
        readonly Module m;public Simulator Sim;readonly Signal a,b,y;readonly StringBuilder tb=new StringBuilder();
        public Bench(string name,int aw,int bw,Func<Expr,Expr,Expr> function)
        {
            m=new Module(name);a=m.Input("a",aw);b=m.Input("b",bw);var e=function(a,b);y=m.Output("result",e.Width);y.Describe(e.Description ?? "Numeric result, bit 0 least significant.");m.Drive(y,e);
            Sim=m.Simulate();Sim.Tick(true);File.WriteAllText("generated/"+name+".v",m.Verilog());
            tb.AppendLine("module tb; reg clk=0,rst=0; reg ["+(aw-1)+":0] a; reg ["+(bw-1)+":0] b; wire ["+(e.Width-1)+":0] result;");
            tb.AppendLine("h_"+name+" dut(.clk(clk),.rst(rst),.s_a(a),.s_b(b),.s_result(result)); initial begin");
        }
        public BigInteger Eval(BigInteger av,BigInteger bv)
        {
            Sim.Set(a,av);Sim.Set(b,bv);var result=Sim.Read(y);
            tb.AppendLine("a="+a.Width+"'d"+av+"; b="+b.Width+"'d"+bv+"; #1; if(result !== "+y.Width+"'d"+result+") $fatal(1,\""+m.Name+" mismatch\");");
            return result;
        }
        public void Save() { tb.AppendLine("$display(\"PASS HDL "+m.Name+"\");$finish;end endmodule");File.WriteAllText("generated/"+m.Name+"_tb.v",tb.ToString()); }
    }
    public static void Run()
    {
        Fixed();
        foreach(bool widen in new[]{true,false})
        {
            var source=widen?FloatFormat.Tiny16:FloatFormat.Compact24;var target=widen?FloatFormat.Compact24:FloatFormat.Tiny16;
            var conversion=new Bench("FloatConvert"+source.Width+"to"+target.Width,source.Width,1,(x,y)=>FloatMath.Convert(new FloatValue(source,x),target).Bits);
            var rng=new Random(82);
            for(int i=0;i<300;i++){var bits=new BigInteger(rng.Next())&Expr.Mask(source.Width);Check(conversion.Eval(bits,0)==target.Encode(source.Decode(bits)),"Float format conversion");}
            conversion.Save();
        }
        foreach(var f in new[]{FloatFormat.Tiny16,FloatFormat.Compact24})
        {
            var rng=new Random(193+f.Width);
            foreach(var op in new[]{"add","sub","mul","div"})
            {
                var bench=new Bench("Float"+f.Width+"_"+op,f.Width,f.Width,(x,y)=>{
                    var a=new FloatValue(f,x);var b=new FloatValue(f,y);return (op=="add"?a+b:op=="sub"?a-b:op=="mul"?a*b:a/b).Bits;
                });
                var edges=new[]{BigInteger.Zero,BigInteger.One<<(f.Width-1),(BigInteger.One<<(f.Width-1))|17,BigInteger.One<<f.FractionBits,f.MaxFinite,f.NaR,f.Encode(1),f.Encode(-1),f.Encode(.5),f.Encode(-.5),f.Encode(2)};
                foreach(var a in edges)foreach(var b in edges)Check(bench.Eval(a,b)==Oracle(f,a,b,op),"Float edge "+f.Width+op+" "+a+","+b);
                for(int i=0;i<300;i++)
                {
                    var a=new BigInteger(rng.Next())&Expr.Mask(f.Width);var b=new BigInteger(rng.Next())&Expr.Mask(f.Width);
                    Check(bench.Eval(a,b)==Oracle(f,a,b,op),"Float random "+f.Width+op+" "+a+","+b);
                }
                bench.Save();
            }
            MathFunctions(f);
        }
        Console.WriteLine("PASS: "+count+" numeric assertions; max normalized transcendental error "+worst.ToString("G6"));
        File.WriteAllText("generated/numeric_accuracy.txt","Numeric assertions: "+count+"\nMax |actual-reference|/max(1,|reference|): "+worst.ToString("G17")+"\nBasic arithmetic uses independent exact rational oracle. Transcendentals use quantized-input double references. Finite samples, not error proofs.\n");
    }
    static void Fixed()
    {
        var bench=new Bench("FixedRounding",8,8,(xa,xb)=>FixedArithmetic.Convert(xa,4,4,1,Rounding.NearestEven,Overflow.Saturate));
        for(int i=0;i<256;i++)
        {
            double x=(double)Expr.SignedValue(i,8)/8;
            int expected=Math.Max(-8,Math.Min(7,(int)Math.Round(x,MidpointRounding.ToEven)));
            Check(bench.Eval(i,0)==(expected&15),"Fixed rounding "+i);
        }
        bench.Save();
        var m=new Module("TypedWidths");var a=m.Input("a",12);var b=m.Input("b",12);
        var sum=new UInt<W12>(a)+new UInt<W12>(b);Check(sum.Bits.Width==13,"Widened addition");
        var product=SFixed<W24,F16>.Constant(1.5)*SFixed<W24,F16>.Constant(-2);
        Check(product.Bits.Width==48,"Widened product");
        var narrowed=product.Convert<W24,F16>(Rounding.NearestEven,Overflow.Saturate);
        var y=m.Output("sum",13);m.Drive(y,sum.Bits);var sim=m.Simulate();sim.Tick(true);sim.Set(a,4095);sim.Set(b,1);
        Check(sim.Read(y)==4096,"Carry preserved");Check(Expr.SignedValue(sim.Read(narrowed.Bits),24)==-3*65536,"Fixed product scale");
        bool rejected=false;try{new UInt<W12>(Expr.Const(11,0));}catch(ArgumentException){rejected=true;}Check(rejected,"Typed mismatch");
    }
    static void MathFunctions(FloatFormat f)
    {
        string[] names={"sqrt","reciprocal","log2","log","log10","exp2","exp","sin","cos","tan","atan","atan2","pow"};
        foreach(string name in names)
        {
            var bench=new Bench("Float"+f.Width+"_"+name,f.Width,f.Width,(x,y)=>{
                var a=new FloatValue(f,x);var b=new FloatValue(f,y);
                switch(name){case "sqrt":return FloatMath.Sqrt(a).Bits;case "reciprocal":return FloatMath.Reciprocal(a).Bits;case "log2":return FloatMath.Log2(a).Bits;case "log":return FloatMath.Log(a).Bits;case "log10":return FloatMath.Log10(a).Bits;case "exp2":return FloatMath.Exp2(a).Bits;case "exp":return FloatMath.Exp(a).Bits;case "sin":return FloatMath.Sin(a).Bits;case "cos":return FloatMath.Cos(a).Bits;case "tan":return FloatMath.Tan(a).Bits;case "atan":return FloatMath.Atan(a).Bits;case "atan2":return FloatMath.Atan2(a,b).Bits;default:return FloatMath.Pow(a,b).Bits;}
            });
            var rng=new Random(441);double max=0;
            for(int i=0;i<70;i++)
            {
                double x=(i-35)/9.0,y=.2+rng.NextDouble()*3;
                if(name=="sqrt"||name=="log"||name=="log2"||name=="log10"||name=="pow"||name=="reciprocal")x=.03125+rng.NextDouble()*32;
                if(name=="tan")x=(i-35)/40.0;
                if(name=="atan2")y=i%2==0?y:-y;
                var xb=f.Encode(x);var yb=f.Encode(y);x=f.Decode(xb);y=f.Decode(yb);double reference;
                switch(name){case "sqrt":reference=Math.Sqrt(x);break;case "reciprocal":reference=1/x;break;case "log2":reference=Math.Log(x,2);break;case "log":reference=Math.Log(x);break;case "log10":reference=Math.Log10(x);break;case "exp2":reference=Math.Pow(2,x);break;case "exp":reference=Math.Exp(x);break;case "sin":reference=Math.Sin(x);break;case "cos":reference=Math.Cos(x);break;case "tan":reference=Math.Tan(x);break;case "atan":reference=Math.Atan(x);break;case "atan2":reference=Math.Atan2(x,y);break;default:reference=Math.Pow(x,y);break;}
                reference=Math.Max(-f.Decode(f.MaxFinite),Math.Min(f.Decode(f.MaxFinite),reference));
                double actual=f.Decode(bench.Eval(xb,yb));double error=Math.Abs(actual-reference)/Math.Max(1,Math.Abs(reference));
                max=Math.Max(max,error);Check(!double.IsNaN(actual)&&error<(f.Width==16?.018:.0003),name+f.Width+" x="+x+" y="+y+" actual="+actual+" reference="+reference+" error="+error);
            }
            Check(bench.Eval(f.NaR,f.Encode(1))==f.NaR,"NaR propagation "+name);
            if(name=="sqrt"||name=="log2"||name=="log"||name=="log10")Check(bench.Eval(f.Encode(-1),0)==f.NaR,"Negative domain "+name);
            if(name=="sin"||name=="cos"||name=="tan")Check(bench.Eval(f.Encode(4096),0)==f.NaR,"Trig range bound");
            if(name=="pow")Check(bench.Eval(f.Encode(-2),f.Encode(3))==f.Encode(-8),"Negative integer power");
            if(name=="atan2") { Check(bench.Eval(0,0)==f.NaR,"atan2 origin");Check(bench.Eval((BigInteger.One<<(f.Width-1))|17,f.Encode(-1))==f.Encode(Math.PI),"No signed-zero quadrant"); }
            if(name=="sin"||name=="cos"||name=="atan") {
                Check(bench.Eval(0,0)==f.Encode(name=="cos"?1:0),"Zero exact "+name);
                Check(bench.Eval((BigInteger.One<<(f.Width-1))|17,0)==f.Encode(name=="cos"?1:0),"Canonical zero "+name);
                var tiny=BigInteger.One<<f.FractionBits;
                Check(bench.Eval(tiny,0)==(name=="cos"?f.Encode(1):tiny),"Tiny argument "+name);
            }
            if(name=="sqrt"||name=="reciprocal"||name=="log2") {
                for(int i=0;i<100;i++) {
                    var bits=new BigInteger(rng.Next())&Expr.Mask(f.Width-1);
                    if(((bits>>f.FractionBits)&f.MaxExponent)==0 || ((bits>>f.FractionBits)&f.MaxExponent)==f.MaxExponent)continue;
                    double x=f.Decode(bits);double refValue=name=="sqrt"?Math.Sqrt(x):name=="reciprocal"?1/x:Math.Log(x,2);
                    var result=bench.Eval(bits,0);
                    if(name=="sqrt"||name=="reciprocal")Check(result==f.Encode(refValue),"Full exponent range "+name+f.Width);
                    else Check(Math.Abs(f.Decode(result)-refValue)/Math.Max(1,Math.Abs(refValue))<(f.Width==16?.001:.00002),"Log full range");
                }
            }
            worst=Math.Max(worst,max);Console.WriteLine("  "+name+f.Width+" max normalized error "+max.ToString("G5"));bench.Save();
        }
    }
}
