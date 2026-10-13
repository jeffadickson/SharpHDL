using System;
using System.Numerics;

namespace SharpHDL
{
    // Custom finite floating-point, not IEEE 754 or historical MBF binary compatibility.
    public sealed class FloatFormat
    {
        public readonly int ExponentBits,FractionBits;
        public int Width { get { return 1+ExponentBits+FractionBits; } }
        public int Bias { get { return (1<<(ExponentBits-1))-1; } }
        public int MaxExponent { get { return (1<<ExponentBits)-1; } }
        public static readonly FloatFormat Tiny16=new FloatFormat(5,10);
        public static readonly FloatFormat Compact24=new FloatFormat(7,16);
        public FloatFormat(int exponentBits,int fractionBits)
        {
            if(exponentBits<3||exponentBits>7||fractionBits<2||fractionBits>24)throw new ArgumentException("Supported exponent 3..7, fraction 2..24.");
            ExponentBits=exponentBits;FractionBits=fractionBits;
        }
        public BigInteger NaR { get { return (BigInteger)MaxExponent<<FractionBits; } }
        public BigInteger MaxFinite { get { return ((BigInteger)(MaxExponent-1)<<FractionBits)|Expr.Mask(FractionBits); } }
        public string Layout { get { return "Custom float: SIGN ["+(Width-1)+"], EXP ["+(Width-2)+":"+FractionBits+"] bias "+Bias+", FRACTION ["+(FractionBits-1)+":0] with hidden leading 1. EXP=0 flushes to zero; EXP=all ones is NaR. Round nearest even; finite overflow saturates."; } }
        public bool Same(FloatFormat b) { return b!=null&&b.ExponentBits==ExponentBits&&b.FractionBits==FractionBits; }
        public BigInteger Encode(double x)
        {
            if(double.IsNaN(x)||double.IsInfinity(x))return NaR;
            if(x==0)return 0;
            BigInteger sign=x<0?BigInteger.One<<(Width-1):BigInteger.Zero;x=Math.Abs(x);
            int e=(int)Math.Floor(Math.Log(x,2));
            double m=x/Math.Pow(2,e);if(m<1){m*=2;e--;}if(m>=2){m/=2;e++;}
            var sig=new BigInteger(Math.Round(m*Math.Pow(2,FractionBits),MidpointRounding.ToEven));
            if(sig==(BigInteger.One<<(FractionBits+1))){sig>>=1;e++;}
            int biased=e+Bias;
            if(biased<=0)return 0;if(biased>=MaxExponent)return sign|MaxFinite;
            return sign|((BigInteger)biased<<FractionBits)|(sig&Expr.Mask(FractionBits));
        }
        public double Decode(BigInteger bits)
        {
            bits&=Expr.Mask(Width);int e=(int)((bits>>FractionBits)&MaxExponent);
            if(e==0)return 0;if(e==MaxExponent)return double.NaN;
            double x=(1+(double)(bits&Expr.Mask(FractionBits))/Math.Pow(2,FractionBits))*Math.Pow(2,e-Bias);
            return (bits&(BigInteger.One<<(Width-1)))!=0?-x:x;
        }
        public FloatValue Constant(double x) { return new FloatValue(this,Expr.Const(Width,Encode(x))); }
    }
    public sealed class FloatValue
    {
        public readonly FloatFormat Format;
        public readonly Expr Bits;
        public FloatValue(FloatFormat format,Expr bits)
        { if(format==null||bits==null||bits.Width!=format.Width)throw new ArgumentException("Float shape mismatch.");Format=format;Bits=bits;if(bits is Signal && bits.Description==null)bits.Describe(format.Layout); }
        public static FloatValue operator +(FloatValue a,FloatValue b) { return FloatMath.Add(a,b); }
        public static FloatValue operator -(FloatValue a,FloatValue b) { return FloatMath.Add(a,FloatMath.Negate(b)); }
        public static FloatValue operator *(FloatValue a,FloatValue b) { return FloatMath.Multiply(a,b); }
        public static FloatValue operator /(FloatValue a,FloatValue b) { return FloatMath.Divide(a,b); }
        public static FloatValue operator -(FloatValue a) { return FloatMath.Negate(a); }
    }
    public static class FloatMath
    {
        static Expr C(int n) { return Expr.SignedConst(16,n); }
        internal static Expr Sign(FloatValue a) { return ~Zero(a)&a.Bits.Slice(a.Format.Width-1,a.Format.Width-1); }
        internal static Expr Exponent(FloatValue a) { return a.Bits.Slice(a.Format.Width-2,a.Format.FractionBits); }
        internal static Expr Zero(FloatValue a) { return Exponent(a).Eq(Expr.Const(a.Format.ExponentBits,0)); }
        internal static Expr Bad(FloatValue a) { return Exponent(a).Eq(Expr.Const(a.Format.ExponentBits,a.Format.MaxExponent)); }
        static Expr Sig(FloatValue a) { return Expr.Concat(Expr.Const(1,1),a.Bits.Slice(a.Format.FractionBits-1,0)); }
        static void Same(FloatValue a,FloatValue b) { if(!a.Format.Same(b.Format))throw new ArgumentException("Float formats must match."); }
        static FloatValue Result(FloatFormat f,Expr bits) { return new FloatValue(f,bits.Describe(f.Layout)); }
        internal static FloatValue Select(Expr condition,FloatValue yes,FloatValue no) { Same(yes,no);return Result(yes.Format,Expr.Mux(condition,yes.Bits,no.Bits)); }
        static FloatValue InvalidIf(Expr bad,FloatValue value) { return Select(bad,value.Format.Constant(double.NaN),value); }
        // Static barrel network. Excess shift yields zero, as for ordinary bit vectors.
        internal static Expr Shift(Expr x,Expr amount,bool left)
        {
            for(int i=0;i<amount.Width;i++)
            {
                int n=1<<i;
                x=Expr.Mux(amount.Slice(i,i),n>=x.Width?Expr.Const(x.Width,0):left?x<<n:x>>n,x);
            }
            return x;
        }
        // Pack an exact unsigned integer magnitude * 2^scale. Round once, nearest even.
        internal static FloatValue Pack(FloatFormat f,Expr sign,Expr magnitude,Expr scale)
        {
            int p=f.FractionBits;Expr rounded=Expr.Const(p+2,0),position=C(0);
            for(int i=0;i<magnitude.Width;i++)
            {
                Expr candidate;
                if(i<=p)candidate=magnitude.Resize(p+2)<<(p-i);
                else
                {
                    int n=i-p;candidate=(magnitude>>n).Resize(p+2);
                    var guard=magnitude.Slice(n-1,n-1);
                    var sticky=n>1?~magnitude.Slice(n-2,0).Eq(Expr.Const(n-1,0)):Expr.Const(1,0);
                    candidate=candidate+(guard&(sticky|candidate.Slice(0,0))).Resize(p+2);
                }
                var bit=magnitude.Slice(i,i);rounded=Expr.Mux(bit,candidate,rounded);position=Expr.Mux(bit,C(i),position);
            }
            var carry=rounded.Slice(p+1,p+1);
            var exponent=scale+position+C(f.Bias)+carry.Resize(16);
            var fraction=Expr.Mux(carry,rounded>>1,rounded).Resize(p);
            var normal=Expr.Concat(sign,Expr.Concat(exponent.Resize(f.ExponentBits),fraction));
            var maximum=Expr.Concat(sign,Expr.Const(f.Width-1,f.MaxFinite));
            var overflow=~exponent.SignedLt(C(f.MaxExponent));
            var underflow=exponent.SignedLt(C(1))|magnitude.Eq(Expr.Const(magnitude.Width,0));
            return Result(f,Expr.Mux(underflow,Expr.Const(f.Width,0),Expr.Mux(overflow,maximum,normal)));
        }
        public static FloatValue FromFixed(FloatFormat f,Expr signedBits,int fractionalBits)
        {
            if(fractionalBits<0||fractionalBits>=signedBits.Width)throw new ArgumentException("Invalid fixed format.");
            var sign=signedBits.Slice(signedBits.Width-1,signedBits.Width-1);
            return Pack(f,sign,Expr.Mux(sign,Expr.Const(signedBits.Width,0)-signedBits,signedBits),C(-fractionalBits));
        }
        // Exact integer on a common binary grid; enough width for the format's entire finite range.
        static Expr Exact(FloatValue a)
        {
            int w=(1<<a.Format.ExponentBits)+a.Format.FractionBits+1;
            var shift=Exponent(a).Resize(16)-C(1);
            var mag=Shift(Sig(a).Resize(w),shift,true);
            mag=Expr.Mux(Zero(a),Expr.Const(w,0),mag);
            return Expr.Mux(Sign(a),Expr.Const(w,0)-mag,mag);
        }
        public static Expr ToFixed(FloatValue a,int width,int fractionalBits,Rounding rounding,Overflow overflow)
        {
            // NaR maps to zero at this explicit conversion boundary.
            int sourceFraction=a.Format.Bias+a.Format.FractionBits-1;
            return Expr.Mux(Bad(a),Expr.Const(width,0),FixedArithmetic.Convert(Exact(a),sourceFraction,width,fractionalBits,rounding,overflow));
        }
        public static FloatValue Convert(FloatValue a,FloatFormat target)
        {
            if(target==null)throw new ArgumentNullException("target");
            var value=Pack(target,Sign(a),Sig(a),Exponent(a).Resize(16)-C(a.Format.Bias+a.Format.FractionBits));
            return InvalidIf(Bad(a),Select(Zero(a),target.Constant(0),value));
        }
        public static FloatValue Negate(FloatValue a)
        { return InvalidIf(Bad(a),Result(a.Format,Expr.Mux(Zero(a),Expr.Const(a.Format.Width,0),a.Bits^Expr.Const(a.Format.Width,BigInteger.One<<(a.Format.Width-1))))); }
        public static FloatValue Abs(FloatValue a)
        { return InvalidIf(Bad(a),Result(a.Format,Expr.Mux(Zero(a),Expr.Const(a.Format.Width,0),a.Bits&Expr.Const(a.Format.Width,Expr.Mask(a.Format.Width-1))))); }
        public static Expr LessThan(FloatValue a,FloatValue b)
        { Same(a,b);return ~Bad(a)&~Bad(b)&Exact(a).SignedLt(Exact(b)); }
        public static FloatValue Add(FloatValue a,FloatValue b)
        {
            Same(a,b);var f=a.Format;var ax=Exact(a);var bx=Exact(b);int w=ax.Width+1;
            var sum=ax.SignExtend(w)+bx.SignExtend(w);
            return InvalidIf(Bad(a)|Bad(b),FromFixed(f,sum,f.Bias+f.FractionBits-1));
        }
        public static FloatValue Multiply(FloatValue a,FloatValue b)
        {
            Same(a,b);var f=a.Format;int w=2*(f.FractionBits+1);
            var mag=Sig(a).Resize(w)*Sig(b).Resize(w);
            var scale=Exponent(a).Resize(16)+Exponent(b).Resize(16)-C(2*f.Bias+2*f.FractionBits);
            var value=Pack(f,Sign(a)^Sign(b),mag,scale);
            value=Select(Zero(a)|Zero(b),f.Constant(0),value);
            return InvalidIf(Bad(a)|Bad(b),value);
        }
        static Expr Quotient(Expr numerator,Expr denominator,out Expr remainder)
        {
            int w=numerator.Width+1;Expr r=Expr.Const(w,0),q=Expr.Const(numerator.Width,0);var d=denominator.Resize(w);
            for(int i=numerator.Width-1;i>=0;i--)
            {
                r=(r<<1)|numerator.Slice(i,i).Resize(w);var take=~r.Lt(d);
                r=Expr.Mux(take,r-d,r);q=q|(take.Resize(q.Width)<<i);
            }
            remainder=r;return q;
        }
        public static FloatValue Divide(FloatValue a,FloatValue b)
        {
            Same(a,b);var f=a.Format;int n=f.FractionBits+4,w=f.FractionBits+1+n;
            Expr rem;var q=Quotient(Sig(a).Resize(w)<<n,Sig(b),out rem);
            // Jam all unrepresented quotient bits into the low guard bit.
            q=q|(~rem.Eq(Expr.Const(rem.Width,0))).Resize(q.Width);
            var value=Pack(f,Sign(a)^Sign(b),q,Exponent(a).Resize(16)-Exponent(b).Resize(16)-C(n));
            return InvalidIf(Bad(a)|Bad(b)|Zero(b),Select(Zero(a),f.Constant(0),value));
        }
        public static FloatValue Reciprocal(FloatValue a) { return a.Format.Constant(1)/a; }
        public static FloatValue Sqrt(FloatValue a)
        {
            var f=a.Format;int p=f.FractionBits;
            var e=Exponent(a).Resize(16)-C(f.Bias);var odd=e.Slice(0,0);
            int w=2*p+10;if(w%2!=0)w++;
            var rad=Sig(a).Resize(w)<<(p+6);rad=Expr.Mux(odd,rad<<1,rad);
            Expr root=Expr.Const(w,0),rem=Expr.Const(w,0);
            for(int i=w/2-1;i>=0;i--)
            {
                rem=(rem<<2)|rad.Slice(2*i+1,2*i).Resize(w);
                var trial=(root<<2)|Expr.Const(w,1);var take=~rem.Lt(trial);
                rem=Expr.Mux(take,rem-trial,rem);root=(root<<1)|take.Resize(w);
            }
            root=root|(~rem.Eq(Expr.Const(w,0))).Resize(w);
            var value=Pack(f,Expr.Const(1,0),root,e.ArithmeticShift(1)-C(p+3));
            return InvalidIf(Bad(a)|(Sign(a)&~Zero(a)),Select(Zero(a),f.Constant(0),value));
        }
        public static Stream Unary(Module m,Stream input,string name,FloatFormat format,Func<FloatValue,FloatValue> function)
        {
            if(input.Width!=format.Width)throw new ArgumentException("Float stream width mismatch.");
            input.Data.Describe(format.Layout);
            var result=StreamBlocks.Map(m,input,name,format.Width,x=>{
                var value=function(new FloatValue(format,x));if(!format.Same(value.Format))throw new ArgumentException("Function changes float format.");return value.Bits;
            });result.Data.Describe(format.Layout);return result;
        }
        // Binary operands are one atomic beat: A in low bits, B in high bits.
        public static Stream Binary(Module m,Stream input,string name,FloatFormat format,Func<FloatValue,FloatValue,FloatValue> function)
        {
            if(input.Width!=2*format.Width)throw new ArgumentException("Binary float stream needs packed {B,A}.");
            input.Data.Describe("Packed float operands: A ["+(format.Width-1)+":0], B ["+(2*format.Width-1)+":"+format.Width+"]. "+format.Layout);
            var result=StreamBlocks.Map(m,input,name,format.Width,x=>{
                var value=function(new FloatValue(format,x.Slice(format.Width-1,0)),new FloatValue(format,x.Slice(2*format.Width-1,format.Width)));
                if(!format.Same(value.Format))throw new ArgumentException("Function changes float format.");return value.Bits;
            });result.Data.Describe(format.Layout);return result;
        }
        // Piecewise linear coefficient ROM, 1024 intervals, output unsigned Q8.32.
        // Math.* executes only now, during elaboration, to construct ROM constants.
        static Expr Table(Expr fraction,Func<double,double> function,string description)
        {
            const int count=1024;var bases=new BigInteger[count];var slopes=new BigInteger[count];
            for(int i=0;i<count;i++)
            {
                var a=new BigInteger(Math.Round(function((double)i/count)*4294967296.0));
                var b=new BigInteger(Math.Round(function((double)(i+1)/count)*4294967296.0));
                bases[i]=a&Expr.Mask(40);slopes[i]=(b-a)&Expr.Mask(41);
            }
            var index=fraction.Slice(31,22);var offset=fraction.Slice(21,0);
            var basis=Expr.Lookup(40,index,bases).Describe(description+": base coefficients, Q8.32; address = fraction [31:22].");
            var slope=Expr.Lookup(41,index,slopes).Describe(description+": signed interval differences, 32 fractional bits.");
            var delta=(slope.SignExtend(64)*offset.Resize(64)).ArithmeticShift(22).Resize(40);
            return (basis+delta).Describe(description+": interpolated Q8.32 value. Low 22 fraction bits select position inside interval.");
        }
        public static FloatValue Log2(FloatValue a)
        {
            var f=a.Format;var fraction=a.Bits.Slice(f.FractionBits-1,0).Resize(32)<<(32-f.FractionBits);
            var mantissa=Table(fraction,x=>Math.Log(1+x,2),"log2 mantissa ROM").Resize(64);
            var exponent=(Exponent(a).Resize(16)-C(f.Bias)).SignExtend(64)<<32;
            return InvalidIf(Bad(a)|Sign(a)|Zero(a),FromFixed(f,exponent+mantissa,32));
        }
        public static FloatValue Log(FloatValue a) { return Log2(a)*a.Format.Constant(Math.Log(2)); }
        public static FloatValue Log10(FloatValue a) { return Log2(a)*a.Format.Constant(Math.Log10(2)); }
        public static FloatValue Exp2(FloatValue a)
        {
            var f=a.Format;var fixedValue=ToFixed(a,64,32,Rounding.Floor,Overflow.Saturate);
            var mantissa=Table(fixedValue.Slice(31,0),x=>Math.Pow(2,x),"exp2 fraction ROM");
            var scale=fixedValue.ArithmeticShift(32).Resize(16)-C(32);
            var value=Pack(f,Expr.Const(1,0),mantissa,scale);
            var max=Result(f,Expr.Const(f.Width,f.MaxFinite));
            value=Select(~LessThan(a,f.Constant(f.MaxExponent-f.Bias)),max,value);
            value=Select(LessThan(a,f.Constant(1-f.Bias)),f.Constant(0),value);
            return InvalidIf(Bad(a),value);
        }
        public static FloatValue Exp(FloatValue a) { return Exp2(a*a.Format.Constant(1/Math.Log(2))); }
        static FloatValue Trig(FloatValue a,bool cosine)
        {
            var f=a.Format;
            // Bounded radians -> modulo turns. A 48-fraction-bit constant reduces conversion error.
            var radians=ToFixed(a,56,32,Rounding.NearestEven,Overflow.Saturate);
            var reciprocalTau=Expr.Const(112,new BigInteger(Math.Round(Math.Pow(2,48)/(2*Math.PI))));
            var phase=((radians.SignExtend(112)*reciprocalTau).ArithmeticShift(48)).Resize(32);
            var outside=phase.Slice(31,31)^phase.Slice(30,30);
            Expr z=Expr.Mux(outside,phase-Expr.Const(32,BigInteger.One<<31),phase);
            double gain=1;for(int i=0;i<30;i++)gain*=Math.Sqrt(1+Math.Pow(2,-2*i));
            var k=new BigInteger(Math.Round(Math.Pow(2,32)/gain));
            Expr x=Expr.Mux(outside,Expr.SignedConst(40,-k),Expr.SignedConst(40,k)),y=Expr.Const(40,0);
            for(int i=0;i<30;i++)
            {
                var negative=z.Slice(31,31);var dx=y.ArithmeticShift(i);var dy=x.ArithmeticShift(i);
                var xn=Expr.Mux(negative,x+dx,x-dx);var yn=Expr.Mux(negative,y-dy,y+dy);
                var angle=Expr.Const(32,new BigInteger(Math.Round(Math.Atan(Math.Pow(2,-i))/(2*Math.PI)*Math.Pow(2,32))));
                z=Expr.Mux(negative,z+angle,z-angle);x=xn;y=yn;
            }
            var value=FromFixed(f,cosine?x:y,32);
            // Avoid residual CORDIC phase error dominating very small arguments.
            value=Select(LessThan(Abs(a),f.Constant(Math.Pow(2,-12)))|Zero(a),cosine?f.Constant(1):Select(Zero(a),f.Constant(0),a),value);
            var rangeError=f.Decode(f.MaxFinite)<4096?Expr.Const(1,0):~LessThan(Abs(a),f.Constant(4096));
            return InvalidIf(Bad(a)|rangeError,value);
        }
        public static FloatValue Sin(FloatValue a) { return Trig(a,false); }
        public static FloatValue Cos(FloatValue a) { return Trig(a,true); }
        public static FloatValue Tan(FloatValue a) { return Sin(a)/Cos(a); }
        public static FloatValue Atan(FloatValue a)
        {
            var f=a.Format;var magnitude=Abs(a);var inverse=LessThan(f.Constant(1),magnitude);
            var reduced=Select(inverse,Reciprocal(magnitude),magnitude);
            var fixedValue=ToFixed(reduced,40,32,Rounding.NearestEven,Overflow.Saturate);
            var table=Table(fixedValue.Slice(31,0),x=>Math.Atan(x),"atan unit interval ROM");
            var angle=FromFixed(f,table,32);
            angle=Select(~LessThan(reduced,f.Constant(1)),f.Constant(Math.PI/4),angle);
            angle=Select(inverse,f.Constant(Math.PI/2)-angle,angle);
            return InvalidIf(Bad(a),Select(LessThan(magnitude,f.Constant(Math.Pow(2,-12)))|Zero(a),Select(Zero(a),f.Constant(0),a),Select(Sign(a),-angle,angle)));
        }
        public static FloatValue Atan2(FloatValue y,FloatValue x)
        {
            Same(y,x);var f=x.Format;var angle=Atan(y/x);
            angle=Select(Sign(x),Select(Sign(y),angle-f.Constant(Math.PI),angle+f.Constant(Math.PI)),angle);
            angle=Select(Zero(x),Select(Sign(y),f.Constant(-Math.PI/2),f.Constant(Math.PI/2)),angle);
            return InvalidIf(Bad(x)|Bad(y)|(Zero(x)&Zero(y)),angle);
        }
        public static FloatValue Pow(FloatValue a,FloatValue b)
        {
            Same(a,b);var f=a.Format;
            var integer=ToFixed(b,256,0,Rounding.Floor,Overflow.Saturate);
            var integral=FromFixed(f,integer,0).Bits.Eq(b.Bits)|Zero(b);
            var value=Exp2(b*Log2(Abs(a)));
            value=Select(Sign(a)&integer.Slice(0,0),-value,value);
            value=InvalidIf(Sign(a)&~integral,value);
            value=Select(Zero(a),Select(Sign(b),f.Constant(double.NaN),f.Constant(0)),value);
            value=Select(Zero(b),f.Constant(1),value);
            return InvalidIf(Bad(a)|Bad(b),value);
        }
    }
}
