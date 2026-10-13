using System;
using System.Numerics;

namespace SharpHDL
{
    public enum RomFunction { Exp2Fraction, Log2Mantissa, ReciprocalMantissa, SqrtMantissa }
    public static class HwMath
    {
        public const int PhaseWidth=24;
        public const int ResultWidth=32;
        public const int FractionBits=24;
        public static BigInteger EncodePhase(double radians)
        {
            if(double.IsNaN(radians)||double.IsInfinity(radians))throw new ArgumentException("Finite angle required.");
            double turns=radians/(2*Math.PI);turns-=Math.Floor(turns);
            return new BigInteger(Math.Round(turns*Math.Pow(2,PhaseWidth))) & Expr.Mask(PhaseWidth);
        }
        public static double DecodeSigned(BigInteger bits,int width,int fractionBits)
        {return (double)Expr.SignedValue(bits,width)/Math.Pow(2,fractionBits);}
        // 24-bit turns modulo one revolution -> packed {sin,cos}, each signed Q8.24.
        // 21 elastic stages by default: range reduction + 20 micro-rotations.
        public static Stream SinCos(Module m,Stream angle,string name,int iterations=20)
        {
            if(angle.Width!=PhaseWidth || iterations<1 || iterations>22)throw new ArgumentException("CORDIC requires 24-bit phase and 1..22 iterations.");
            const int w=ResultWidth,p=PhaseWidth;
            double gain=1;for(int i=0;i<iterations;i++)gain*=Math.Sqrt(1+Math.Pow(2,-2*i));
            var k=new BigInteger(Math.Round(Math.Pow(2,FractionBits)/gain));
            var s=StreamBlocks.Map(m,angle,name+"_reduce",2*w+p,a=>{
                var outside=a.Slice(p-1,p-1)^a.Slice(p-2,p-2);
                var z=Expr.Mux(outside,a-Expr.Const(p,BigInteger.One<<(p-1)),a);
                var x=Expr.Mux(outside,Expr.SignedConst(w,-k),Expr.SignedConst(w,k));
                return Expr.Concat(z,Expr.Concat(Expr.Const(w,0),x)).Describe("CORDIC bundle: phase [87:64] signed modulo turns / 2^24; Y [63:32] and X [31:0] signed Q8.24.");
            });
            for(int i=0;i<iterations;i++)
            {
                int shift=i;var atan=new BigInteger(Math.Round(Math.Atan(Math.Pow(2,-i))/(2*Math.PI)*Math.Pow(2,p)));
                s=StreamBlocks.Map(m,s,name+"_rotate"+i,2*w+p,bits=>{
                    var x=bits.Slice(w-1,0);var y=bits.Slice(2*w-1,w);var z=bits.Slice(2*w+p-1,2*w);
                    var negative=z.Slice(p-1,p-1);
                    var dx=y.ArithmeticShift(shift);var dy=x.ArithmeticShift(shift);
                    var xn=Expr.Mux(negative,x+dx,x-dx);var yn=Expr.Mux(negative,y-dy,y+dy);
                    var zn=Expr.Mux(negative,z+Expr.Const(p,atan),z-Expr.Const(p,atan));
                    return Expr.Concat(zn,Expr.Concat(yn,xn)).Describe("CORDIC rotation bundle: residual phase [87:64] signed turns / 2^24; Y [63:32], X [31:0], signed Q8.24.");
                });
            }
            return Project(m,s,name+"_result",2*w,b=>b.Slice(2*w-1,0));
        }
        public static Stream Cos(Module m,Stream angle,string name,int iterations=20)
        {return Project(m,SinCos(m,angle,name+"_cordic",iterations),name,ResultWidth,b=>b.Slice(ResultWidth-1,0));}
        public static Stream Sin(Module m,Stream angle,string name,int iterations=20)
        {return Project(m,SinCos(m,angle,name+"_cordic",iterations),name,ResultWidth,b=>b.Slice(2*ResultWidth-1,ResultWidth));}
        static Stream Project(Module m,Stream input,string name,int width,Func<Expr,Expr> map)
        {
            var output=Stream.Internal(m,name,width,input.UserWidth);
            m.Drive(input.Ready,output.Ready);m.Drive(output.Valid,input.Valid);m.Drive(output.Data,map(input.Data));
            m.Drive(output.Keep,Expr.Const(width/8,Expr.Mask(width/8)));m.Drive(output.Last,input.Last);m.Drive(output.User,input.User);return output;
        }
        public static double Reference(RomFunction function,double fraction)
        {
            switch(function)
            {
                case RomFunction.Exp2Fraction:return Math.Pow(2,fraction);
                case RomFunction.Log2Mantissa:return Math.Log(1+fraction,2);
                case RomFunction.ReciprocalMantissa:return 1/(1+fraction);
                case RomFunction.SqrtMantissa:return Math.Sqrt(1+fraction);
                default:throw new ArgumentOutOfRangeException("function");
            }
        }
        // Coefficient-table approach: centered range reduction, fixed-point Horner.
        // These are fresh Taylor coefficients, not copied TRS-80 floating-point routines.
        public static Stream PolynomialMath(Module m,Stream fraction,string name,RomFunction function,int degree=10)
        {
            if(fraction.Width!=16 || degree<2 || degree>16)throw new ArgumentException("Polynomial requires 16-bit fraction, degree 2..16.");
            const int w=32,frac=28;
            var coefficients=new BigInteger[degree+1];
            double binomial=1,factorial=1;
            for(int k=0;k<=degree;k++)
            {
                double value;
                if(k>0){binomial*= (0.5-(k-1))/k;factorial*=k;}
                switch(function)
                {
                    case RomFunction.Exp2Fraction:value=Math.Sqrt(2)*Math.Pow(Math.Log(2),k)/factorial;break;
                    case RomFunction.Log2Mantissa:value=k==0?Math.Log(1.5,2):((k%2==1?1:-1)/(k*Math.Pow(1.5,k)*Math.Log(2)));break;
                    case RomFunction.ReciprocalMantissa:value=(k%2==0?1:-1)/Math.Pow(1.5,k+1);break;
                    case RomFunction.SqrtMantissa:value=Math.Sqrt(1.5)*binomial/Math.Pow(1.5,k);break;
                    default:throw new ArgumentOutOfRangeException("function");
                }
                coefficients[k]=new BigInteger(Math.Round(value*Math.Pow(2,frac)));
            }
            var s=StreamBlocks.Map(m,fraction,name+"_reduce",64,a=>{
                var centered=(a.Resize(w)<<12)-Expr.Const(w,BigInteger.One<<(frac-1));
                return Expr.Concat(Expr.SignedConst(w,coefficients[degree]),centered).Describe("Horner bundle: accumulator [63:32] signed Q4.28; centered argument [31:0] signed Q4.28.");
            });
            for(int k=degree-1;k>=0;k--)
            {
                var coefficient=coefficients[k];
                s=StreamBlocks.Map(m,s,name+"_horner"+k,64,bits=>{
                    var x=bits.Slice(31,0);var accumulator=bits.Slice(63,32);
                    var product=(accumulator.SignExtend(64)*x.SignExtend(64)).ArithmeticShift(frac).Resize(w);
                    var result=product+Expr.SignedConst(w,coefficient);
                    return Expr.Concat(result,x).Describe("Horner bundle: updated accumulator [63:32] signed Q4.28; centered argument [31:0] signed Q4.28.");
                });
            }
            return Project(m,s,name+"_result",24,b=>b.Slice(63,32).ArithmeticShift(8).Resize(24));
        }
        // A normalized mantissa/fraction, not unrestricted floating point.
        // 16-bit fraction -> 24-bit unsigned Q4.20; two elastic pipeline stages.
        public static Stream RomMath(Module m,Stream fraction,string name,RomFunction function,int addressBits=8)
        {
            if(fraction.Width!=16 || addressBits<2 || addressBits>12)throw new ArgumentException("ROM requires 16-bit fraction, 2..12 address bits.");
            int count=1<<addressBits,fb=16-addressBits;const int ow=24,dw=26;
            var bases=new BigInteger[count];var slopes=new BigInteger[count];
            for(int i=0;i<count;i++)
            {
                var lo=new BigInteger(Math.Round(Reference(function,(double)i/count)*Math.Pow(2,20)));
                var hi=new BigInteger(Math.Round(Reference(function,(double)(i+1)/count)*Math.Pow(2,20)));
                bases[i]=lo;slopes[i]=(hi-lo)&Expr.Mask(dw);
            }
            // Pad bundle width to a byte boundary; fields reside in low bits.
            int bw=((ow+dw+fb+7)/8)*8;
            var tableStage=StreamBlocks.Map(m,fraction,name+"_rom",bw,x=>{
                var address=x.Slice(15,fb);var offset=x.Slice(fb-1,0);
                return Expr.Concat(Expr.Lookup(dw,address,slopes),Expr.Concat(Expr.Lookup(ow,address,bases),offset)).Resize(bw).Describe("ROM interpolation bundle: offset ["+(fb-1)+":0], base ["+(fb+ow-1)+":"+fb+"] unsigned Q4.20, slope ["+(fb+ow+dw-1)+":"+(fb+ow)+"] signed with 20 fractional bits; any upper padding bits zero.");
            });
            return StreamBlocks.Map(m,tableStage,name+"_interpolate",ow,bits=>{
                var offset=bits.Slice(fb-1,0);var basis=bits.Slice(fb+ow-1,fb);
                var slope=bits.Slice(fb+ow+dw-1,fb+ow);
                int mw=dw+fb+1;
                var delta=(slope.SignExtend(mw)*offset.Resize(mw)).ArithmeticShift(fb);
                return (basis.Resize(mw)+delta).Resize(ow);
            });
        }
    }
}
