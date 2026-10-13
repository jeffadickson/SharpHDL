using System;
using System.Linq;
using System.Reflection;
namespace SharpHDL
{
    // CPU-executable unsigned bit vector. Unlike int, every operation wraps to W bits.
    public struct UBits<W> where W:struct,IBitCount
    {
        readonly uint value;
        static int Width { get {int w=default(W).Value;if(w<1||w>32)throw new ArgumentException("UBits width must be 1..32.");return w;} }
        static uint Mask { get {return Width==32?uint.MaxValue:(1u<<Width)-1;} }
        UBits(uint v){value=v&Mask;}
        public static implicit operator UBits<W>(uint v){return new UBits<W>(v);}
        public static explicit operator uint(UBits<W> v){return v.value;}
        public static UBits<W> operator +(UBits<W> a,UBits<W> b){return new UBits<W>(unchecked(a.value+b.value));}
        public static UBits<W> operator -(UBits<W> a,UBits<W> b){return new UBits<W>(unchecked(a.value-b.value));}
        public static UBits<W> operator *(UBits<W> a,UBits<W> b){return new UBits<W>(unchecked(a.value*b.value));}
        public static UBits<W> operator &(UBits<W> a,UBits<W> b){return new UBits<W>(a.value&b.value);}
        public static UBits<W> operator |(UBits<W> a,UBits<W> b){return new UBits<W>(a.value|b.value);}
        public static UBits<W> operator ^(UBits<W> a,UBits<W> b){return new UBits<W>(a.value^b.value);}
        public static UBits<W> operator ~(UBits<W> a){return new UBits<W>(~a.value);}
        public static UBits<W> operator <<(UBits<W> a,int n){return new UBits<W>(a.value<<n);}
        public static UBits<W> operator >>(UBits<W> a,int n){return new UBits<W>(a.value>>n);}
        public static bool operator <(UBits<W> a,UBits<W> b){return a.value<b.value;}
        public static bool operator >(UBits<W> a,UBits<W> b){return a.value>b.value;}
        public static bool operator <=(UBits<W> a,UBits<W> b){return a.value<=b.value;}
        public static bool operator >=(UBits<W> a,UBits<W> b){return a.value>=b.value;}
    }
    [AttributeUsage(AttributeTargets.Struct)] public sealed class HardwareStructAttribute:Attribute {}
    [AttributeUsage(AttributeTargets.Field)] public sealed class HardwareFieldAttribute:Attribute
    {public int Offset {get;private set;}public HardwareFieldAttribute(int offset){Offset=offset;}}
    public sealed class HardwareAssertionException:Exception {public HardwareAssertionException():base("Hardware assertion failed."){} }
    public static class Hardware
    {public static void Assert(bool condition){if(!condition)throw new HardwareAssertionException();}}
    public static class PackedLayout
    {
        internal static bool IsBits(Type t){return t.IsGenericType&&t.GetGenericTypeDefinition()==typeof(UBits<>);}
        internal static bool IsRecord(Type t){return t.IsValueType&&Attribute.IsDefined(t,typeof(HardwareStructAttribute));}
        public static int Width(Type t)
        {
            if(IsBits(t)){int w=((IBitCount)Activator.CreateInstance(t.GetGenericArguments()[0])).Value;if(w<1||w>32)throw new ArgumentException("UBits width must be 1..32.");return w;}
            if(t==typeof(bool))return 1;if(t==typeof(byte)||t==typeof(sbyte))return 8;if(t==typeof(ushort)||t==typeof(short))return 16;if(t==typeof(uint)||t==typeof(int))return 32;
            if(!IsRecord(t))throw new NotSupportedException("Not a packed hardware type: "+t);
            var fields=t.GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);int width=0;var occupied=new System.Collections.Generic.HashSet<int>();
            foreach(var f in fields)
            {
                if(IsRecord(f.FieldType))throw new NotSupportedException("Nested records are not supported.");
                var a=(HardwareFieldAttribute)Attribute.GetCustomAttribute(f,typeof(HardwareFieldAttribute));if(a==null||a.Offset<0)throw new ArgumentException("Every packed field needs a nonnegative HardwareField offset.");
                int w=Width(f.FieldType);if(a.Offset>4096-w)throw new ArgumentException("Packed record exceeds 4096 bits.");
                for(int bit=a.Offset;bit<a.Offset+w;bit++)if(!occupied.Add(bit))throw new ArgumentException("Overlapping packed fields.");width=Math.Max(width,a.Offset+w);
            }
            if(width==0)throw new ArgumentException("Packed record must have fields.");return width;
        }
        internal static string Describe(Type t)
        {
            if(!IsRecord(t))return t.Name+" port; scalar operations use native CIL width, UBits operations wrap explicitly.";
            Width(t);
            return "Packed "+t.Name+" bus: "+string.Join("; ",t.GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).Select(f=>{var a=(HardwareFieldAttribute)Attribute.GetCustomAttribute(f,typeof(HardwareFieldAttribute));return f.Name+" bits["+(a.Offset+Width(f.FieldType)-1)+":"+a.Offset+"] "+f.FieldType.Name;}))+". Unassigned gaps are unused; no CLR padding.";
        }
        internal static Expr Read(Expr value,FieldInfo field)
        {
            int width=Width(field.DeclaringType);if(value.Width!=width)throw new ArgumentException("Packed record width mismatch.");
            var a=(HardwareFieldAttribute)Attribute.GetCustomAttribute(field,typeof(HardwareFieldAttribute));int w=Width(field.FieldType);var x=value.Slice(a.Offset+w-1,a.Offset);
            bool signed=field.FieldType==typeof(int)||field.FieldType==typeof(short)||field.FieldType==typeof(sbyte);
            return signed?x.SignExtend(32):x.Resize(32);
        }
    }
}
