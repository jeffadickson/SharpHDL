using System;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;

namespace SharpHDL
{
    // Single port, synchronous read, read-before-write, independent read/write enables.
    // FPGA power-up initialization is explicit; reset clears read data but retains cells.
    public sealed class SynchronousRam
    {
        internal Module Owner;internal string Name;internal int Width,Depth;
        internal Expr Address,WriteEnable,WriteData;internal BigInteger[] Initial;
        public MemoryOptions Options { get; private set; }
        public Signal ReadData { get; private set; }
        public static SynchronousRam Create(Module m,string name,int width,int depth,
            Expr address,Expr writeEnable,Expr writeData,Expr readEnable,BigInteger[] initial=null,MemoryOptions options=null)
        {
            if(m.Frozen)throw new InvalidOperationException("Design has been frozen.");
            if(name==null||!Regex.IsMatch(name,@"\A[A-Za-z][A-Za-z0-9_]*\z")||m.Memories.Any(x=>x.Name==name))throw new ArgumentException("Invalid or duplicate RAM name.");
            if(width<1||width>4096||depth<1||depth>65536||address.Width>32||(BigInteger.One<<address.Width)<depth||writeData.Width!=width||writeEnable.Width!=1||readEnable.Width!=1)
                throw new ArgumentException("Invalid RAM shape or port width.");
            if(initial!=null&&initial.Length!=depth)throw new ArgumentException("RAM initialization must contain exactly depth words.");
            var contents=initial==null?new BigInteger[depth]:(BigInteger[])initial.Clone();
            foreach(var value in contents)Expr.Const(width,value);
            var ram=new SynchronousRam{Owner=m,Name=name,Width=width,Depth=depth,Address=address,WriteEnable=writeEnable,WriteData=writeData,Initial=contents,Options=options??m.MemoryOptions};
            ram.ReadData=m.Register(name+"_read_data",width,0);
            ram.ReadData.Describe(name+": synchronous RAM output; read-before-write; holds when read enable=0; reset clears output only. Out-of-range enabled reads yield zero.");
            var read=new Expr(width,"memory",address);read.Memory=ram;
            var inRange=address.Resize(address.Width+1).Lt(Expr.Const(address.Width+1,depth));
            m.NextWhen(ram.ReadData,readEnable,Expr.Mux(inRange,read,Expr.Const(width,0)));
            m.Memories.Add(ram);return ram;
        }
    }
}
