using System;
using System.Numerics;

namespace SharpHDL
{
    public interface IBitCount { int Value { get; } }
    public struct Sum<A,B> : IBitCount where A:struct,IBitCount where B:struct,IBitCount
    { public int Value { get { return checked(default(A).Value+default(B).Value); } } }
    public enum Rounding { Floor, NearestEven }
    public enum Overflow { Wrap, Saturate }

    // These wrappers construct the same hardware Expr graph; they are not CPU integers.
    public sealed class UInt<W> where W:struct,IBitCount
    {
        public Expr Bits { get; private set; }
        public static int Width { get { return default(W).Value; } }
        public UInt(Expr bits) { if(bits==null || bits.Width!=Width)throw new ArgumentException("Unsigned width mismatch."); Bits=bits; }
        public static UInt<W> Constant(BigInteger value) { return new UInt<W>(Expr.Const(Width,value)); }
        public static UInt<Sum<W,W1>> operator +(UInt<W> a,UInt<W> b)
        { return new UInt<Sum<W,W1>>(a.Bits.Resize(Width+1)+b.Bits.Resize(Width+1)); }
        public static UInt<Sum<W,W>> operator *(UInt<W> a,UInt<W> b)
        { return new UInt<Sum<W,W>>(a.Bits.Resize(2*Width)*b.Bits.Resize(2*Width)); }
        public static UInt<W> operator /(UInt<W> a,UInt<W> b)
        { return new UInt<W>(IntegerDivide.Evaluate(a.Bits,b.Bits).Quotient); }
        public static UInt<W> operator %(UInt<W> a,UInt<W> b)
        { return new UInt<W>(IntegerDivide.Evaluate(a.Bits,b.Bits).Remainder.Resize(Width)); }
        public static SInt<Sum<W,W1>> operator -(UInt<W> a,UInt<W> b)
        { return new SInt<Sum<W,W1>>(a.Bits.Resize(Width+1)-b.Bits.Resize(Width+1)); }
        public UInt<T> Resize<T>(Overflow overflow) where T:struct,IBitCount
        {
            if(!Enum.IsDefined(typeof(Overflow),overflow))throw new ArgumentException("Invalid overflow policy.");
            int w=default(T).Value;Expr x=Bits;
            if(w<Width && overflow==Overflow.Saturate)
                x=Expr.Mux(Expr.Const(Width,Expr.Mask(w)).Lt(x),Expr.Const(Width,Expr.Mask(w)),x);
            return new UInt<T>(x.Resize(w));
        }
    }
    public sealed class SInt<W> where W:struct,IBitCount
    {
        public Expr Bits { get; private set; }
        public static int Width { get { return default(W).Value; } }
        public SInt(Expr bits) { if(bits==null || bits.Width!=Width)throw new ArgumentException("Signed width mismatch."); Bits=bits; }
        public static SInt<W> Constant(BigInteger value) { return new SInt<W>(Expr.SignedConst(Width,value)); }
        public static SInt<Sum<W,W1>> operator +(SInt<W> a,SInt<W> b)
        { return new SInt<Sum<W,W1>>(a.Bits.SignExtend(Width+1)+b.Bits.SignExtend(Width+1)); }
        public static SInt<Sum<W,W1>> operator -(SInt<W> a,SInt<W> b)
        { return new SInt<Sum<W,W1>>(a.Bits.SignExtend(Width+1)-b.Bits.SignExtend(Width+1)); }
        public static SInt<Sum<W,W>> operator *(SInt<W> a,SInt<W> b)
        { return new SInt<Sum<W,W>>(a.Bits.SignExtend(2*Width)*b.Bits.SignExtend(2*Width)); }
        public static SInt<W> operator /(SInt<W> a,SInt<W> b)
        { return new SInt<W>(IntegerDivide.Evaluate(a.Bits,b.Bits,DivisionRounding.TowardZero,true).Quotient); }
        public static SInt<W> operator %(SInt<W> a,SInt<W> b)
        { return new SInt<W>(IntegerDivide.Evaluate(a.Bits,b.Bits,DivisionRounding.TowardZero,true).Remainder.Resize(Width)); }
        public SInt<T> Resize<T>(Overflow overflow) where T:struct,IBitCount
        { return new SInt<T>(FixedArithmetic.Convert(Bits,0,default(T).Value,0,Rounding.Floor,overflow)); }
    }
    public sealed class SFixed<W,F> where W:struct,IBitCount where F:struct,IBitCount
    {
        public Expr Bits { get; private set; }
        public static int Width { get { return default(W).Value; } }
        public static int FractionBits { get { return default(F).Value; } }
        public SFixed(Expr bits)
        {
            if(bits==null || bits.Width!=Width || FractionBits<0 || FractionBits>=Width)
                throw new ArgumentException("Signed fixed-point shape mismatch.");
            Bits=bits;
        }
        public static SFixed<W,F> Constant(double value)
        {
            if(double.IsNaN(value)||double.IsInfinity(value))throw new ArgumentException("Finite constant required.");
            return new SFixed<W,F>(Expr.SignedConst(Width,new BigInteger(Math.Round(value*Math.Pow(2,FractionBits),MidpointRounding.ToEven))));
        }
        public static SFixed<Sum<W,W1>,F> operator +(SFixed<W,F> a,SFixed<W,F> b)
        { return new SFixed<Sum<W,W1>,F>(a.Bits.SignExtend(Width+1)+b.Bits.SignExtend(Width+1)); }
        public static SFixed<Sum<W,W1>,F> operator -(SFixed<W,F> a,SFixed<W,F> b)
        { return new SFixed<Sum<W,W1>,F>(a.Bits.SignExtend(Width+1)-b.Bits.SignExtend(Width+1)); }
        public static SFixed<Sum<W,W>,Sum<F,F>> operator *(SFixed<W,F> a,SFixed<W,F> b)
        { return new SFixed<Sum<W,W>,Sum<F,F>>(a.Bits.SignExtend(2*Width)*b.Bits.SignExtend(2*Width)); }
        // Scale the value, not the binary-point position. Width and fraction stay fixed.
        static void CheckShift(int n) { if(n<0||n>Width)throw new ArgumentOutOfRangeException("n", "Fixed-point shift must be 0..Width."); }
        public static SFixed<W,F> operator <<(SFixed<W,F> a,int n)
        { CheckShift(n);return new SFixed<W,F>((a.Bits<<n).Describe("Fixed-point left shift: same format, low bits retained; overflow wraps.")); }
        public static SFixed<W,F> operator >>(SFixed<W,F> a,int n)
        { return a.ShiftRight(n,Rounding.Floor); }
        public SFixed<W,F> ShiftRight(int n,Rounding rounding)
        {
            CheckShift(n);
            if(!Enum.IsDefined(typeof(Rounding),rounding))throw new ArgumentException("Invalid rounding policy.");
            Expr q=Bits.ArithmeticShift(n);
            if(n>0 && rounding==Rounding.NearestEven)
            {
                var rem=Bits.Slice(n-1,0);var half=Expr.Const(n,BigInteger.One<<(n-1));
                var increment=half.Lt(rem)|(half.Eq(rem)&q.Slice(0,0));
                q=q+increment.Resize(Width);
            }
            return new SFixed<W,F>(q.Describe("Fixed-point right shift: same format; rounding "+rounding+"."));
        }
        public SFixed<T,G> Convert<T,G>(Rounding rounding,Overflow overflow) where T:struct,IBitCount where G:struct,IBitCount
        { return new SFixed<T,G>(FixedArithmetic.Convert(Bits,FractionBits,default(T).Value,default(G).Value,rounding,overflow)); }
    }
    public static class FixedArithmetic
    {
        public static Expr Convert(Expr bits,int sourceFraction,int targetWidth,int targetFraction,Rounding rounding,Overflow overflow)
        {
            if(sourceFraction<0 || sourceFraction>=bits.Width || targetFraction<0 || targetFraction>=targetWidth || targetWidth<1)
                throw new ArgumentException("Invalid fixed-point format.");
            if(!Enum.IsDefined(typeof(Rounding),rounding)||!Enum.IsDefined(typeof(Overflow),overflow))throw new ArgumentException("Invalid numeric policy.");
            int shift=sourceFraction-targetFraction;
            int work=Math.Max(bits.Width+Math.Max(0,-shift)+1,targetWidth+1);
            Expr x=bits.SignExtend(work);
            if(shift>0)
            {
                var q=x.ArithmeticShift(shift);
                if(rounding==Rounding.NearestEven)
                {
                    var rem=x.Slice(shift-1,0);var half=Expr.Const(shift,BigInteger.One<<(shift-1));
                    var increment=half.Lt(rem) | (half.Eq(rem)&q.Slice(0,0));
                    q=q+increment.Resize(work);
                }
                x=q;
            }
            else if(shift<0) x=x<<(-shift);
            if(overflow==Overflow.Saturate)
            {
                var lo=Expr.SignedConst(work,-(BigInteger.One<<(targetWidth-1)));
                var hi=Expr.SignedConst(work,(BigInteger.One<<(targetWidth-1))-1);
                x=Expr.Mux(x.SignedLt(lo),lo,Expr.Mux(hi.SignedLt(x),hi,x));
            }
            return x.Resize(targetWidth).Describe("Signed fixed-point result: "+targetWidth+" total bits, "+targetFraction+" fractional bits; rounding "+rounding+", overflow "+overflow+".");
        }
    }
    public struct W1 : IBitCount { public int Value { get { return 1; } } }
    public struct W2 : IBitCount { public int Value { get { return 2; } } }
    public struct W3 : IBitCount { public int Value { get { return 3; } } }
    public struct W4 : IBitCount { public int Value { get { return 4; } } }
    public struct W5 : IBitCount { public int Value { get { return 5; } } }
    public struct W6 : IBitCount { public int Value { get { return 6; } } }
    public struct W7 : IBitCount { public int Value { get { return 7; } } }
    public struct W8 : IBitCount { public int Value { get { return 8; } } }
    public struct W9 : IBitCount { public int Value { get { return 9; } } }
    public struct W10 : IBitCount { public int Value { get { return 10; } } }
    public struct W11 : IBitCount { public int Value { get { return 11; } } }
    public struct W12 : IBitCount { public int Value { get { return 12; } } }
    public struct W13 : IBitCount { public int Value { get { return 13; } } }
    public struct W14 : IBitCount { public int Value { get { return 14; } } }
    public struct W15 : IBitCount { public int Value { get { return 15; } } }
    public struct W16 : IBitCount { public int Value { get { return 16; } } }
    public struct W17 : IBitCount { public int Value { get { return 17; } } }
    public struct W18 : IBitCount { public int Value { get { return 18; } } }
    public struct W19 : IBitCount { public int Value { get { return 19; } } }
    public struct W20 : IBitCount { public int Value { get { return 20; } } }
    public struct W21 : IBitCount { public int Value { get { return 21; } } }
    public struct W22 : IBitCount { public int Value { get { return 22; } } }
    public struct W23 : IBitCount { public int Value { get { return 23; } } }
    public struct W24 : IBitCount { public int Value { get { return 24; } } }
    public struct W25 : IBitCount { public int Value { get { return 25; } } }
    public struct W26 : IBitCount { public int Value { get { return 26; } } }
    public struct W27 : IBitCount { public int Value { get { return 27; } } }
    public struct W28 : IBitCount { public int Value { get { return 28; } } }
    public struct W29 : IBitCount { public int Value { get { return 29; } } }
    public struct W30 : IBitCount { public int Value { get { return 30; } } }
    public struct W31 : IBitCount { public int Value { get { return 31; } } }
    public struct W32 : IBitCount { public int Value { get { return 32; } } }
    public struct W33 : IBitCount { public int Value { get { return 33; } } }
    public struct W34 : IBitCount { public int Value { get { return 34; } } }
    public struct W35 : IBitCount { public int Value { get { return 35; } } }
    public struct W36 : IBitCount { public int Value { get { return 36; } } }
    public struct W37 : IBitCount { public int Value { get { return 37; } } }
    public struct W38 : IBitCount { public int Value { get { return 38; } } }
    public struct W39 : IBitCount { public int Value { get { return 39; } } }
    public struct W40 : IBitCount { public int Value { get { return 40; } } }
    public struct W41 : IBitCount { public int Value { get { return 41; } } }
    public struct W42 : IBitCount { public int Value { get { return 42; } } }
    public struct W43 : IBitCount { public int Value { get { return 43; } } }
    public struct W44 : IBitCount { public int Value { get { return 44; } } }
    public struct W45 : IBitCount { public int Value { get { return 45; } } }
    public struct W46 : IBitCount { public int Value { get { return 46; } } }
    public struct W47 : IBitCount { public int Value { get { return 47; } } }
    public struct W48 : IBitCount { public int Value { get { return 48; } } }
    public struct W49 : IBitCount { public int Value { get { return 49; } } }
    public struct W50 : IBitCount { public int Value { get { return 50; } } }
    public struct W51 : IBitCount { public int Value { get { return 51; } } }
    public struct W52 : IBitCount { public int Value { get { return 52; } } }
    public struct W53 : IBitCount { public int Value { get { return 53; } } }
    public struct W54 : IBitCount { public int Value { get { return 54; } } }
    public struct W55 : IBitCount { public int Value { get { return 55; } } }
    public struct W56 : IBitCount { public int Value { get { return 56; } } }
    public struct W57 : IBitCount { public int Value { get { return 57; } } }
    public struct W58 : IBitCount { public int Value { get { return 58; } } }
    public struct W59 : IBitCount { public int Value { get { return 59; } } }
    public struct W60 : IBitCount { public int Value { get { return 60; } } }
    public struct W61 : IBitCount { public int Value { get { return 61; } } }
    public struct W62 : IBitCount { public int Value { get { return 62; } } }
    public struct W63 : IBitCount { public int Value { get { return 63; } } }
    public struct W64 : IBitCount { public int Value { get { return 64; } } }
    public struct F0 : IBitCount { public int Value { get { return 0; } } }
    public struct F1 : IBitCount { public int Value { get { return 1; } } }
    public struct F2 : IBitCount { public int Value { get { return 2; } } }
    public struct F3 : IBitCount { public int Value { get { return 3; } } }
    public struct F4 : IBitCount { public int Value { get { return 4; } } }
    public struct F5 : IBitCount { public int Value { get { return 5; } } }
    public struct F6 : IBitCount { public int Value { get { return 6; } } }
    public struct F7 : IBitCount { public int Value { get { return 7; } } }
    public struct F8 : IBitCount { public int Value { get { return 8; } } }
    public struct F9 : IBitCount { public int Value { get { return 9; } } }
    public struct F10 : IBitCount { public int Value { get { return 10; } } }
    public struct F11 : IBitCount { public int Value { get { return 11; } } }
    public struct F12 : IBitCount { public int Value { get { return 12; } } }
    public struct F13 : IBitCount { public int Value { get { return 13; } } }
    public struct F14 : IBitCount { public int Value { get { return 14; } } }
    public struct F15 : IBitCount { public int Value { get { return 15; } } }
    public struct F16 : IBitCount { public int Value { get { return 16; } } }
    public struct F17 : IBitCount { public int Value { get { return 17; } } }
    public struct F18 : IBitCount { public int Value { get { return 18; } } }
    public struct F19 : IBitCount { public int Value { get { return 19; } } }
    public struct F20 : IBitCount { public int Value { get { return 20; } } }
    public struct F21 : IBitCount { public int Value { get { return 21; } } }
    public struct F22 : IBitCount { public int Value { get { return 22; } } }
    public struct F23 : IBitCount { public int Value { get { return 23; } } }
    public struct F24 : IBitCount { public int Value { get { return 24; } } }
    public struct F25 : IBitCount { public int Value { get { return 25; } } }
    public struct F26 : IBitCount { public int Value { get { return 26; } } }
    public struct F27 : IBitCount { public int Value { get { return 27; } } }
    public struct F28 : IBitCount { public int Value { get { return 28; } } }
    public struct F29 : IBitCount { public int Value { get { return 29; } } }
    public struct F30 : IBitCount { public int Value { get { return 30; } } }
    public struct F31 : IBitCount { public int Value { get { return 31; } } }
    public struct F32 : IBitCount { public int Value { get { return 32; } } }
}
