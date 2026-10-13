using SharpHDL;
[HardwareStruct]
public struct Header
{
    [HardwareField(0)] public UBits<W12> Length;
    [HardwareField(12)] public byte Kind;
    [HardwareField(20)] public bool Urgent;
}
public static class AdvancedFunctions
{
    public static UBits<W12> TypedAccumulate(UBits<W12> a,UBits<W12> b)
    {UBits<W12> sum=a+b;sum=sum*(UBits<W12>)3u;return (sum>>2)^(a<<1);}
    public static uint Packet(Header h,uint limit)
    {uint n=(uint)h.Length;Hardware.Assert(n<=limit);return h.Urgent?n+(uint)h.Kind:n;}
    public static int AssertBranch(int x,bool check){if(check)Hardware.Assert(x>=0);return x+2;}
    public static int RuntimeFor(int n,int seed){int s=seed;for(int i=0;i<n;i++){if(i==2)continue;if(i==7)break;s=unchecked(s+i);}return s;}
    public static int RuntimeWhile(int n,int seed){int s=seed;while(n>0){s=unchecked(s+n);n--;}return s;}
    public static int RuntimeDo(int n,int seed){do{seed=unchecked(seed+1);n--;}while(n>0);return seed;}
    public static int Nested(int n,int seed){for(int i=0;i<n;i++)for(int j=0;j<3;j++)seed=unchecked(seed+i+j);return seed;}
    public static int Choice(int n,int x){switch(n){case 0:return x+1;case 1:return x-1;case 2:return x^77;default:return x;}}
    public static int Checked(int a,int b){return checked(a+b);}
    public static int Divide(int a,int b){return a/b;}
    public static int Remainder(int a,int b){return a%b;}
    public static int AssertLoop(int n,int limit){int i=0;while(i<n){Hardware.Assert(i<limit);i++;}return i;}
    public static int Infinite(int n,int seed){while(true)seed=unchecked(seed+n);}
    public static uint Memory(uint[] words,int n,uint delta)
    {Hardware.Assert(n>=0&&n<=words.Length);uint sum=0;for(int i=0;i<n;i++){uint x=words[i];words[i]=unchecked(x+delta);sum=unchecked(sum+x);}return sum;}
    public static uint ReadWord(uint[] words,int index,uint unused){return words[index];}
    public static uint WriteThenRead(uint[] words,int index,uint value){words[index]=value;return words[index];}
    public static uint WriteThenAssert(uint[] words,int index,uint value)
    {uint old=words[index];words[index]=value;Hardware.Assert(value<10);return old;}
    public static int BadCall(int a,int b){return System.Math.Abs(a);}
}
