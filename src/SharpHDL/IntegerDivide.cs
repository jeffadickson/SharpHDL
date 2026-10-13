using System;
using System.Numerics;

namespace SharpHDL
{
    public enum DivideArchitecture { Parallel, Iterative }
    public enum DivisionRounding { TowardZero, Floor, Ceiling, NearestEven, AwayFromZero }

    public sealed class DivisionResult
    {
        public Expr Quotient { get; private set; }
        // Always a signed W+1-bit residual, including unsigned rounded division.
        public Expr Remainder { get; private set; }
        public Expr DivideByZero { get; private set; }
        public Expr Overflowed { get; private set; }
        internal DivisionResult(Expr q,Expr r,Expr z,Expr o)
        { Quotient=q;Remainder=r;DivideByZero=z;Overflowed=o; }
        public Expr Pack()
        {
            int w=Quotient.Width;
            return Expr.Concat(Overflowed,Expr.Concat(DivideByZero,Expr.Concat(Remainder,Quotient)))
                .Resize(IntegerDivide.ResultWidth(w)).Describe(IntegerDivide.Layout(w));
        }
        public static DivisionResult Unpack(Expr data,int operandWidth)
        {
            int w=operandWidth;
            if(data.Width!=IntegerDivide.ResultWidth(w))throw new ArgumentException("Division result bus width mismatch.");
            return new DivisionResult(data.Slice(w-1,0),data.Slice(2*w,w),data.Slice(2*w+1,2*w+1),data.Slice(2*w+2,2*w+2));
        }
    }

    public static class IntegerDivide
    {
        static Expr Zero(int w) { return Expr.Const(w,0); }
        static Expr Negate(Expr x) { return Zero(x.Width)-x; }
        static void Width(int w)
        { if(w<1 || w>256)throw new ArgumentException("Division operand width must be 1..256."); }
        static void Policies(DivisionRounding rounding,Overflow overflow)
        {
            if(!Enum.IsDefined(typeof(DivisionRounding),rounding)||!Enum.IsDefined(typeof(Overflow),overflow))
                throw new ArgumentException("Unknown division policy.");
        }
        public static int ResultWidth(int w) { Width(w);return ((2*w+3+7)/8)*8; }
        internal static string Layout(int w)
        {
            return "Division result: QUOTIENT ["+(w-1)+":0], signed RESIDUAL ["+(2*w)+":"+w+"], DIVIDE_BY_ZERO ["+(2*w+1)+"], OVERFLOW ["+(2*w+2)+"], upper padding zero. Residual = A-Q*B except flagged overflow. TKEEP all ones.";
        }
        static Expr Sign(Expr x,bool signed) { return signed?x.Slice(x.Width-1,x.Width-1):Zero(1); }
        static Expr Magnitude(Expr x,Expr sign) { return Expr.Mux(sign,Negate(x),x); }
        // One restoring division step. Compare at W+1 bits so the trial carry is retained.
        static void Step(Expr dividend,Expr remainder,Expr divisor,Expr quotient,
            out Expr nextR,out Expr nextQ)
        {
            int w=dividend.Width;
            var trial=(remainder<<1)|dividend.Slice(w-1,w-1).Resize(w+1);
            var take=~trial.Lt(divisor.Resize(w+1));
            nextR=Expr.Mux(take,trial-divisor.Resize(w+1),trial);
            nextQ=(quotient<<1)|take.Resize(w);
        }
        static DivisionResult Finish(Expr q,Expr r,Expr divisor,Expr signA,Expr negative,
            Expr originalA,bool signed,DivisionRounding rounding,Overflow overflow)
        {
            int w=q.Width;
            var nz=~r.Eq(Zero(w+1));Expr increment=Zero(1);
            switch(rounding)
            {
                case DivisionRounding.Floor:increment=negative & nz;break;
                case DivisionRounding.Ceiling:increment=~negative & nz;break;
                case DivisionRounding.AwayFromZero:increment=nz;break;
                case DivisionRounding.NearestEven:
                    var twice=r<<1;var d=divisor.Resize(w+1);
                    increment=d.Lt(twice)|(d.Eq(twice)&q.Slice(0,0));break;
            }
            var qm=q.Resize(w+1)+increment.Resize(w+1);
            var signedQ=Expr.Mux(negative,Negate(qm),qm);
            var rm=Expr.Mux(increment,r-divisor.Resize(w+1),r);
            var residual=Expr.Mux(signA,Negate(rm),rm);
            var zero=divisor.Eq(Zero(w));
            Expr ov=signed?(~negative & Expr.Const(w+1,(BigInteger.One<<(w-1))-1).Lt(qm) & ~zero):Zero(1);
            Expr resultQ=signedQ.Resize(w);
            if(overflow==Overflow.Saturate && signed)
                resultQ=Expr.Mux(ov,Expr.Const(w,(BigInteger.One<<(w-1))-1),resultQ);
            resultQ=Expr.Mux(zero,Zero(w),resultQ);
            residual=Expr.Mux(zero,signed?originalA.SignExtend(w+1):originalA.Resize(w+1),Expr.Mux(ov,Zero(w+1),residual));
            return new DivisionResult(resultQ.Describe("Rounded quotient; "+rounding+"; overflow policy "+overflow+"."),
                residual.Describe("Signed residual A-Q*B, W+1 bits; overflow gives zero; divide-by-zero returns A."),zero,ov);
        }
        // Combinational implementation: W copies of the compare/subtract step.
        public static DivisionResult Evaluate(Expr a,Expr b,DivisionRounding rounding=DivisionRounding.TowardZero,
            bool signed=false,Overflow overflow=Overflow.Wrap)
        {
            if(a==null||b==null||a.Width!=b.Width)throw new ArgumentException("Division requires equal-width operands.");
            int w=a.Width;Width(w);Policies(rounding,overflow);
            var sa=Sign(a,signed);var sb=Sign(b,signed);
            Expr dividend=Magnitude(a,sa), divisor=Magnitude(b,sb), q=Zero(w), r=Zero(w+1);
            for(int i=0;i<w;i++) { Expr nr,nq;Step(dividend,r,divisor,q,out nr,out nq);r=nr;q=nq;dividend=dividend<<1; }
            return Finish(q,r,divisor,sa,sa^sb,a,signed,rounding,overflow);
        }
        public static Stream Build(Module m,Stream input,string name,int operandWidth,
            DivideArchitecture architecture,DivisionRounding rounding=DivisionRounding.TowardZero,
            bool signed=false,Overflow overflow=Overflow.Wrap)
        {
            int w=operandWidth;Width(w);Policies(rounding,overflow);
            if(w%4!=0 || input.Width!=2*w)throw new ArgumentException("Stream division requires W divisible by four and packed {B,A} input of 2W bits.");
            if(!Enum.IsDefined(typeof(DivideArchitecture),architecture))throw new ArgumentException("Unknown divide architecture.");
            int p=ResultWidth(w);
            input.Data.Describe("Division operands: A dividend ["+(w-1)+":0], B divisor ["+(2*w-1)+":"+w+"]. "+(signed?"Signed two's complement.":"Unsigned.")+" TKEEP must be all ones.");
            string description=Layout(w)+" Architecture "+architecture+", rounding "+rounding+", overflow "+overflow+". TLAST/TUSER preserved.";
            if(architecture==DivideArchitecture.Parallel)
                return StreamBlocks.Map(m,input,name,p,x=>Evaluate(x.Slice(w-1,0),x.Slice(2*w-1,w),rounding,signed,overflow).Pack().Describe(description));
            var output=Stream.Internal(m,name,p,input.UserWidth);output.Data.Describe(description);
            var busy=m.Register(name+"_busy",1,0);var valid=m.Register(name+"_valid",1,0);
            var dividend=m.Register(name+"_shift_a",w,0);var divisor=m.Register(name+"_abs_b",w,0);
            var quotient=m.Register(name+"_q",w,0);var remainder=m.Register(name+"_r",w+1,0);
            var original=m.Register(name+"_original_a",w,0);
            var signA=m.Register(name+"_sign_a",1,0);var negative=m.Register(name+"_negative_q",1,0);
            int cw=1;while((1<<cw)<w)cw++;
            var index=m.Register(name+"_index",cw,0);var data=m.Register(name+"_result",p,0);
            var last=m.Register(name+"_last",1,0);var user=m.Register(name+"_user",input.UserWidth,0);
            dividend.Describe("Dividend magnitude shift register; highest bit feeds the next trial remainder.");
            divisor.Describe("Unsigned divisor magnitude, captured at input acceptance.");
            quotient.Describe("W-bit quotient magnitude under construction, one bit per compute edge.");
            remainder.Describe("W+1-bit nonnegative partial remainder; extra bit preserves trial shift carry.");
            original.Describe("Original A for deterministic divide-by-zero residual.");
            data.Describe(description+" Registered result holds throughout output stalls.");
            m.Drive(input.Ready,~busy & (~valid|output.Ready));var start=input.Fire;
            var finish=busy & index.Eq(Expr.Const(cw,w-1));
            var a=input.Data.Slice(w-1,0);var b=input.Data.Slice(2*w-1,w);
            var sa=Sign(a,signed);var sb=Sign(b,signed);Expr nr,nq;
            Step(dividend,remainder,divisor,quotient,out nr,out nq);
            var packed=Finish(nq,nr,divisor,signA,negative,original,signed,rounding,overflow).Pack();
            m.Next(busy,Expr.Mux(start,Expr.Const(1,1),Expr.Mux(finish,Zero(1),busy)));
            m.Next(valid,Expr.Mux(finish,Expr.Const(1,1),Expr.Mux(output.Fire,Zero(1),valid)));
            m.Next(dividend,Expr.Mux(start,Magnitude(a,sa),Expr.Mux(busy,dividend<<1,dividend)));
            m.NextWhen(divisor,start,Magnitude(b,sb));m.NextWhen(original,start,a);
            m.NextWhen(signA,start,sa);m.NextWhen(negative,start,sa^sb);
            m.Next(quotient,Expr.Mux(start,Zero(w),Expr.Mux(busy,nq,quotient)));
            m.Next(remainder,Expr.Mux(start,Zero(w+1),Expr.Mux(busy,nr,remainder)));
            m.Next(index,Expr.Mux(start,Zero(cw),Expr.Mux(busy,index+Expr.Const(cw,1),index)));
            m.NextWhen(data,finish,packed);m.NextWhen(last,start,input.Last);m.NextWhen(user,start,input.User);
            m.Drive(output.Data,data);m.Drive(output.Valid,valid);m.Drive(output.Keep,Expr.Const(p/8,Expr.Mask(p/8)));
            m.Drive(output.Last,last);m.Drive(output.User,user);return output;
        }
    }
}
