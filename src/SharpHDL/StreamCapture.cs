using System;
using System.Numerics;
namespace SharpHDL
{
    /// <summary>Capture one stream frame into cyclic banks, preserving the complete beat.
    /// Enable starts capture from zero after clear. Stops on LAST or capacity; does not wrap.
    /// Clear/reset discard controller state and retain RAM. Host reads are allowed only while not busy.</summary>
    public sealed class StreamCapture
    {
        public Expr Count {get;private set;}public Expr Busy {get;private set;}public Expr Done {get;private set;}public Expr Full {get;private set;}
        public BankedRam Memory {get;private set;}public BankedMemoryLayout Layout {get;private set;}
        public static StreamCapture Build(Module m,Stream input,string name,int banks,int rows,Expr enable,Expr clear,Expr[] hostRows,Expr hostReadEnable,MemoryOptions options=null) {
            if(m==null||input==null||enable==null||clear==null||hostReadEnable==null)throw new ArgumentNullException();
            if(enable.Width!=1||clear.Width!=1||hostReadEnable.Width!=1)throw new ArgumentException("Capture controls must be one bit.");
            int packedWidth=input.Width+input.Keep.Width+1+input.UserWidth;
            var layout=new BankedMemoryLayout(packedWidth,banks,rows);
            if(hostRows==null||hostRows.Length!=banks)throw new ArgumentException("One host row address per bank required.");
            foreach(var row in hostRows)if(row==null||row.Width!=layout.RowBits)throw new ArgumentException("Host row shape mismatch.");
            int cw=1;while((BigInteger.One<<cw)<=layout.Depth)cw++;
            var count=m.Register(name+"_count",cw,0);var done=m.Register(name+"_done",1,0);
            var full=count.Eq(Expr.Const(cw,layout.Depth));
            var busy=enable&~done&~full&~clear;
            m.Drive(input.Ready,busy);var fire=input.Fire;
            m.Next(count,Expr.Mux(clear,Expr.Const(cw,0),Expr.Mux(fire,count+Expr.Const(cw,1),count)));
            m.Next(done,Expr.Mux(clear,Expr.Const(1,0),done|(fire&input.Last)));
            count.Describe("Number of captured beats, including partial TKEEP beats. Stops at capacity; never wraps.");
            var packed=Expr.Concat(input.User,Expr.Concat(input.Last,Expr.Concat(input.Keep,input.Data)));
            var addresses=new Expr[banks];var we=new Expr[banks];var wd=new Expr[banks];var re=new Expr[banks];
            for(int b=0;b<banks;b++) {
                Expr selected=banks==1?Expr.Const(1,1):count.Resize(Math.Max(cw,layout.BankBits)).Slice(layout.BankBits-1,0).Eq(Expr.Const(layout.BankBits,b));
                var row=(count>>layout.BankBits).Resize(layout.RowBits);
                addresses[b]=Expr.Mux(busy,row,hostRows[b]);we[b]=fire&selected;wd[b]=packed;re[b]=hostReadEnable&~busy&~clear;
            }
            var ram=BankedRam.Create(m,name+"_memory",layout,addresses,we,wd,re,null,options);
            for(int b=0;b<banks;b++)ram.ReadData[b].Describe("Captured beat in bank "+b+": DATA ["+(input.Width-1)+":0], KEEP ["+(input.Width+input.Keep.Width-1)+":"+input.Width+"], LAST ["+(input.Width+input.Keep.Width)+"], USER above LAST. Logical index = row*"+banks+"+"+b+".");
            return new StreamCapture{Count=count,Busy=busy,Done=done,Full=full,Memory=ram,Layout=layout};
        }
    }
    public sealed class NativeStreamCapture
    {
        readonly AxisFormat format;
        public NativeBankedRam Memory {get;private set;}
        public int Count {get;private set;}public bool Done {get;private set;}
        public bool Full {get{return Count==Memory.Layout.Depth;}}
        public NativeStreamCapture(AxisFormat format,int banks,int rows) {
            this.format=format??throw new ArgumentNullException(nameof(format));
            Memory=new NativeBankedRam(new BankedMemoryLayout(format.DataWidth+format.DataWidth/8+1+format.UserWidth,banks,rows));
        }
        public void Reset(){Count=0;Done=false;}
        public bool TryAccept(AxisBeat beat,bool enabled=true) {
            format.Check(beat);if(!enabled||Done||Full)return false;
            var packed=beat.Data|(beat.Keep<<format.DataWidth)|((beat.Last?BigInteger.One:BigInteger.Zero)<<(format.DataWidth+format.DataWidth/8))|(beat.User<<(format.DataWidth+format.DataWidth/8+1));
            Memory.Write(Count++,packed);Done=beat.Last;return true;
        }
        public AxisBeat Read(int index) {
            if(index<0||index>=Count)throw new ArgumentOutOfRangeException(nameof(index));
            var p=Memory.Read(index);return new AxisBeat(p&Expr.Mask(format.DataWidth),(p>>format.DataWidth)&Expr.Mask(format.DataWidth/8),((p>>(format.DataWidth+format.DataWidth/8))&1)!=0,p>>(format.DataWidth+format.DataWidth/8+1));
        }
    }
}
