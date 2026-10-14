using System;
using System.Numerics;
namespace SharpHDL
{
    public sealed class BankedMemoryLayout
    {
        public int Width {get;private set;}public int Banks {get;private set;}public int Rows {get;private set;}
        public int Depth {get{return Banks*Rows;}}
        public int BankBits {get;private set;}public int RowBits {get;private set;}
        public int WordAddressBits {get{return Math.Max(1,BankBits+RowBits);}}
        public BankedMemoryLayout(int width,int banks,int rows) {
            if(width<1||width>4096||banks<1||banks>16||(banks&(banks-1))!=0||rows<1||rows>65536)throw new ArgumentException("Banked RAM: width 1..4096, power-of-two banks 1..16, rows 1..65536.");
            Width=width;Banks=banks;Rows=rows;
            int bb=0;while((1<<bb)<banks)bb++;BankBits=bb;
            int rb=1;while((1<<rb)<rows)rb++;RowBits=rb;
        }
        public int BankForWord(int word) {CheckWord(word);return word&(Banks-1);}
        public int RowForWord(int word) {CheckWord(word);return word>>BankBits;}
        internal void CheckWord(int word){if(word<0||word>=Depth)throw new ArgumentOutOfRangeException(nameof(word));}
        internal BigInteger[] Contents(BigInteger[] initial) {
            if(initial!=null&&initial.Length!=Depth)throw new ArgumentException("Initial array must contain exactly Depth words.");
            var data=initial==null?new BigInteger[Depth]:(BigInteger[])initial.Clone();foreach(var v in data)Expr.Const(Width,v);return data;
        }
    }
    /// <summary>Cyclic interleaving: word = row*Banks + bank. Independent single-port RAM per bank.
    /// No arbitration, automatic CIL array partitioning or multiport replication is inserted.</summary>
    public sealed class BankedRam
    {
        public BankedMemoryLayout Layout {get;private set;}
        public Signal[] ReadData {get;private set;}
        public static BankedRam Create(Module m,string name,BankedMemoryLayout layout,Expr[] rowAddresses,Expr[] writeEnable,Expr[] writeData,Expr[] readEnable,BigInteger[] initial=null,MemoryOptions options=null) {
            if(m==null||layout==null||rowAddresses==null||writeEnable==null||writeData==null||readEnable==null)throw new ArgumentNullException();
            int n=layout.Banks;
            if(rowAddresses.Length!=n||writeEnable.Length!=n||writeData.Length!=n||readEnable.Length!=n)throw new ArgumentException("Exactly one port tuple per bank is required.");
            for(int b=0;b<n;b++)if(rowAddresses[b]==null||writeEnable[b]==null||writeData[b]==null||readEnable[b]==null||rowAddresses[b].Width!=layout.RowBits||writeEnable[b].Width!=1||readEnable[b].Width!=1||writeData[b].Width!=layout.Width)
                throw new ArgumentException("Bank port shape mismatch.");
            var contents=layout.Contents(initial);var result=new BankedRam{Layout=layout,ReadData=new Signal[n]};
            for(int b=0;b<n;b++) {
                // Protected zero padding preserves logical bounds without a read-data mux
                // between the inferred RAM and its output register (important for BRAM inference).
                int physicalRows=1<<layout.RowBits;
                var bankInitial=new BigInteger[physicalRows];for(int row=0;row<layout.Rows;row++)bankInitial[row]=contents[row*n+b];
                var inRange=rowAddresses[b].Resize(layout.RowBits+1).Lt(Expr.Const(layout.RowBits+1,layout.Rows));
                var ram=SynchronousRam.Create(m,name+"_bank"+b,layout.Width,physicalRows,rowAddresses[b],writeEnable[b]&inRange,writeData[b],readEnable[b],bankInitial,options);
                result.ReadData[b]=ram.ReadData;
                result.ReadData[b].Describe("Bank "+b+" synchronous old-data output; logical word = row*"+n+"+"+b+". Independent port; disabled read holds; reset retains cells.");
            }
            return result;
        }
    }
    /// <summary>Native functional storage, no graph or synthesis. Indexed by logical word.</summary>
    public sealed class NativeBankedRam
    {
        public BankedMemoryLayout Layout {get;private set;}
        readonly BigInteger[] cells;
        public NativeBankedRam(BankedMemoryLayout layout,BigInteger[] initial=null) {Layout=layout??throw new ArgumentNullException(nameof(layout));cells=layout.Contents(initial);}
        public BigInteger Read(int word){Layout.CheckWord(word);return cells[word];}
        public void Write(int word,BigInteger value){Layout.CheckWord(word);Expr.Const(Layout.Width,value);cells[word]=value;}
        // Controller reset does not erase RAM. Clearing cells is an explicit software operation.
        public void Clear(){Array.Clear(cells,0,cells.Length);}
    }
}
