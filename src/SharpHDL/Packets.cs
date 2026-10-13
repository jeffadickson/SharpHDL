using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SharpHDL
{
    public sealed class PacketLayout
    {
        readonly List<KeyValuePair<string,int>> fields=new List<KeyValuePair<string,int>>();
        public int Width { get { return fields.Sum(f=>f.Value); } }
        public PacketLayout Field(string name,int width)
        {
            if(string.IsNullOrEmpty(name) || fields.Any(f=>f.Key==name) || width<1 || Width+width>4096)
                throw new ArgumentException("Invalid field name or width.");
            fields.Add(new KeyValuePair<string,int>(name,width));return this;
        }
        public string Description
        {
            get { int high=Width-1;return "Packed fields, MSB first: "+string.Join("; ",fields.Select(f=>{string t=f.Key+" ["+high+":"+(high-f.Value+1)+"]";high-=f.Value;return t;}).ToArray())+"."; }
        }
        public Expr Describe(Expr word)
        { if(word.Width!=Width)throw new ArgumentException("Layout width mismatch.");return word.Describe(Description); }
        int Low(string name,out int width)
        {
            int offset=0;
            foreach(var f in fields) { offset+=f.Value;if(f.Key==name){width=f.Value;return Width-offset;} }
            throw new ArgumentException("Unknown field: "+name);
        }
        public Expr Extract(Expr word,string name)
        { int width;int low=Low(name,out width);if(word.Width!=Width)throw new ArgumentException("Header width mismatch.");return word.Slice(low+width-1,low).Describe("Packet field "+name+": source bits ["+(low+width-1)+":"+low+"]."); }
        public Expr Replace(Expr word,string name,Expr value)
        {
            int width;int low=Low(name,out width);
            if(word.Width!=Width || value.Width!=width)throw new ArgumentException("Header width mismatch.");
            var clear=Expr.Mask(Width) ^ (Expr.Mask(width)<<low);
            return (word & Expr.Const(Width,clear)) | (value.Resize(Width)<<low);
        }
        public Expr Pack(IDictionary<string,Expr> values)
        {
            if(values.Count!=fields.Count)throw new ArgumentException("Supply exactly the declared fields.");
            Expr result=null;
            foreach(var f in fields)
            { Expr v;if(!values.TryGetValue(f.Key,out v)||v.Width!=f.Value)throw new ArgumentException("Field width mismatch.");result=result==null?v:Expr.Concat(result,v); }
            if(result==null)throw new InvalidOperationException("Empty layout.");return result.Describe(Description);
        }
        public BigInteger PackValue(IDictionary<string,BigInteger> values)
        {
            if(values.Count!=fields.Count)throw new ArgumentException("Supply exactly the declared fields.");
            BigInteger result=0;
            foreach(var f in fields) { BigInteger v;if(!values.TryGetValue(f.Key,out v))throw new ArgumentException("Missing field.");Expr.Const(f.Value,v);result=(result<<f.Value)|v; }
            return result;
        }
        public Dictionary<string,BigInteger> Decode(BigInteger word)
        {
            Expr.Const(Width,word);var result=new Dictionary<string,BigInteger>();
            foreach(var f in fields){int width;int low=Low(f.Key,out width);result[f.Key]=(word>>low)&Expr.Mask(width);}return result;
        }
    }

    public sealed class RouterDesign
    {
        public Module Module;public Stream Input,Zero,One;public PacketLayout Header;
        public static RouterDesign Build(int queueDepth=4)
        {
            var m=new Module("PacketRouter");
            var header=new PacketLayout().Field("version",4).Field("type",12).Field("length",16).Field("source",32);
            var input=Stream.Input(m,"s_axis",64,8);input.Data.Describe("First beat: "+header.Description+" Subsequent beats: opaque payload bytes.");
            var zero=Stream.Output(m,"m_axis_zero",64,8);var one=Stream.Output(m,"m_axis_one",64,8);
            var active=m.Register("in_packet",1,0);var route=m.Register("packet_route",1,0);
            var match=header.Extract(input.Data,"type").Eq(Expr.Const(12,1));
            var selected=Expr.Mux(active,route,match);
            var a=Stream.Internal(m,"route_zero",64,8);var b=Stream.Internal(m,"route_one",64,8);
            m.Drive(input.Ready,Expr.Mux(selected,b.Ready,a.Ready));
            m.NextWhen(active,input.Fire,~input.Last);
            m.NextWhen(route,input.Fire & ~active,match);
            foreach(var dest in new[]{a,b})
            {m.Drive(dest.Data,input.Data);m.Drive(dest.Keep,input.Keep);m.Drive(dest.Last,input.Last);m.Drive(dest.User,input.User);}
            m.Drive(a.Valid,input.Valid & ~selected);m.Drive(b.Valid,input.Valid & selected);
            Stream.Connect(m,StreamBlocks.Fifo(m,a,"queue_zero",queueDepth),zero);
            Stream.Connect(m,StreamBlocks.Fifo(m,b,"queue_one",queueDepth),one);
            return new RouterDesign{Module=m,Input=input,Zero=zero,One=one,Header=header};
        }
    }

    public static class PacketMath
    {
        // Reflected CRC-32/ISO-HDLC: bytes in low-to-high lanes, initial all ones.
        public static Expr Crc32Word(Expr crc,Expr data,Expr keep)
        {
            if(crc.Width!=32 || data.Width%8!=0 || keep.Width!=data.Width/8)throw new ArgumentException("CRC shape mismatch.");
            Expr result=crc;
            for(int lane=0;lane<keep.Width;lane++)
            {
                Expr next=result ^ data.Slice(lane*8+7,lane*8).Resize(32);
                for(int bit=0;bit<8;bit++)
                    next=Expr.Mux(next.Slice(0,0),(next>>1)^Expr.Const(32,0xedb88320U),next>>1);
                result=Expr.Mux(keep.Slice(lane,lane),next,result);
            }
            return result;
        }
        // A pass-through packet stream with CRC metadata attached on the last beat.
        public static Stream AttachCrc32(Module m,Stream input,string name)
        {
            var output=Stream.Internal(m,name,input.Width,32);
            var state=m.Register(name+"_crc",32,0xffffffffU);
            var next=Crc32Word(state,input.Data,input.Keep);
            m.Drive(input.Ready,output.Ready);
            m.Drive(output.Valid,input.Valid);m.Drive(output.Data,input.Data);m.Drive(output.Keep,input.Keep);m.Drive(output.Last,input.Last);
            m.Drive(output.User,Expr.Mux(input.Last,~next,Expr.Const(32,0)));
            m.NextWhen(state,input.Fire,Expr.Mux(input.Last,Expr.Const(32,0xffffffffU),next));return output;
        }
    }
}
