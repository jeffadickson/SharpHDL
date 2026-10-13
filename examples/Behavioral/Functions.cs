using System;
using SharpHDL;
namespace Behavioral
{
    public static class CSharpFunctions
    {
        public static int Minimum(int a,int b) { if(a<b)return a;return b; }
        public static int SelectArithmetic(int a,int b) { if(a>b)return unchecked(a+2);return unchecked(b-1); }
        public static int CheckedAdd(int a,int b) { return checked(a+b); }
        public static uint CheckedUnsigned(uint a,uint b) { return checked(a+b); }
        public static int CheckedMultiply(int a,int b) { return checked(a*b); }
        public static int CheckedSubtract(int a,int b) { return checked(a-b); }
        public static int GuardedAdd(int a,int b) { if(b==0)return a;return checked(a+b); }
        public static int Divide(int a,int b) { return a/b; }
        public static int Remainder(int a,int b) { return a%b; }
        public static uint UnsignedMinimum(uint a,uint b) { return a<b?a:b; }
        public static int ShiftMix(int a,int b) { return unchecked((a<<b)^(a>>b)); }
        public static uint LogicalShift(uint a,uint b) { return a>>(int)b; }
        public static int Switch(int a,int b)
        { switch(a){case 0:return b+1;case 1:return b+3;case 2:return b-2;case 3:return b^17;default:return b;} }
        public static int Loop(int a,int b) { for(int i=0;i<4;i++)a=unchecked((a<<1)+b);return a; }
        public static int Loop12(int a,int b) { for(int i=1;i<=12;i++)a=unchecked(a+b);return a; }
        public static int While4(int a,int b) { int i=0;while(i<4){a=unchecked(a+b);i++;}return a; }
        public static int DoWhile4(int a,int b) { int i=0;do{a=unchecked(a^b);i++;}while(i<4);return a; }
        public static int BreakContinue(int a,int b) { for(int i=0;i<8;i++){if(i==2)continue;if(i==6)break;a=unchecked(a+b);}return a; }
        public static int NestedLoops(int a,int b) { for(int i=0;i<3;i++)for(int j=0;j<2;j++)a=unchecked(a+b);return a; }
        public static int Half(int a,int b) { return a/2; }
        public static int HalfRemainder(int a,int b) { return a%2; }
        public static uint UnsignedHalf(uint a,uint b) { return a/2; }
        public static int Double(int a,int b) { return unchecked(a*2); }
        public static int CheckedDouble(int a,int b) { return checked(a*2); }
        public static int Nested(int a,int b) { return a<0?(b<0?a-b:a+b):(b<0?a^b:a&b); }
        static int Helper(int a,int b) { return a<b?a:b; }
        public static int Call(int a,int b) { return unchecked(Helper(a,b)+3); }
        public static int Casts(int a,int b) { return unchecked((sbyte)a+(ushort)b); }
        public static bool Predicate(int a,int b) { return a<b && b!=0; }
        public static int NextCounter(int count,bool enable) { return enable?unchecked(count+1):count; }
        [return:PortWidth(8)]
        public static int Narrow([PortWidth(8)] int a,[PortWidth(8)] int b) { return a<b?a+2:b-1; }
        public static int BadLoop(int a,int b) { while(a<b)a++;return a; }
        public static int BadRecursion(int a,int b) { return BadRecursion(a,b); }
        public static int BadExternal(int a,int b) { return Math.Abs(a); }
        public static int BadArray(int a,int b) { var xs=new int[a];return xs[b]; }
        public static int BadHandler(int a,int b) { try{return a/b;}catch{return 0;} }
        public static long BadLong(long a,long b) { return a+b; }
        public static int ConstantInfinite(int a,int b) { while(true) a++; }
    }
}
