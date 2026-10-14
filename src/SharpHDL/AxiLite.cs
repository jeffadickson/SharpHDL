using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
namespace SharpHDL
{
    public static class AxiLite
    {
        public static string Wrapper(Module module) {
            string[] channels={"aw","w","b","ar","r","t"};
            return Axi4Stream.Wrapper(module,n=>channels.Any(c=>n.EndsWith("_"+c+"valid")||n.EndsWith("_"+c+"ready")),
                "AXI4-Lite 32-bit slave and optional AXI4-Stream ports; shared clock and reset.");
        }
    }
    public sealed class AxiLitePort
    {
        public Signal AwAddress,AwProtection,AwValid,AwReady,WriteData,WriteStrobe,WriteValid,WriteReady;
        public Signal WriteResponse,WriteResponseValid,WriteResponseReady;
        public Signal ArAddress,ArProtection,ArValid,ArReady,ReadData,ReadResponse,ReadValid,ReadReady;
        public int AddressWidth {get{return AwAddress.Width;}}
        internal Module Owner;
        public static AxiLitePort Slave(Module m,string name,int addressWidth=16) {
            if(m==null)throw new ArgumentNullException(nameof(m));
            if(addressWidth<2||addressWidth>32)throw new ArgumentException("AXI4-Lite address width must be 2..32 bits.");
            var p=new AxiLitePort{Owner=m};
            p.AwAddress=m.Input(name+"_awaddr",addressWidth);p.AwProtection=m.Input(name+"_awprot",3);
            p.AwValid=m.Input(name+"_awvalid",1);p.AwReady=m.Output(name+"_awready",1);
            p.WriteData=m.Input(name+"_wdata",32);p.WriteStrobe=m.Input(name+"_wstrb",4);
            p.WriteValid=m.Input(name+"_wvalid",1);p.WriteReady=m.Output(name+"_wready",1);
            p.WriteResponse=m.Output(name+"_bresp",2);p.WriteResponseValid=m.Output(name+"_bvalid",1);p.WriteResponseReady=m.Input(name+"_bready",1);
            p.ArAddress=m.Input(name+"_araddr",addressWidth);p.ArProtection=m.Input(name+"_arprot",3);
            p.ArValid=m.Input(name+"_arvalid",1);p.ArReady=m.Output(name+"_arready",1);
            p.ReadData=m.Output(name+"_rdata",32);p.ReadResponse=m.Output(name+"_rresp",2);
            p.ReadValid=m.Output(name+"_rvalid",1);p.ReadReady=m.Input(name+"_rready",1);
            p.AwAddress.Describe("AXI4-Lite byte write address; captured independently of WDATA. 32-bit register accesses require 4-byte alignment.");
            p.ArAddress.Describe("AXI4-Lite byte read address; read data snapshots on AR handshake.");
            p.WriteData.Describe("AXI4-Lite 32-bit write word; byte lane i is bits [8*i+7:8*i].");
            p.WriteStrobe.Describe("WSTRB bit i updates byte lane i; zero strobes are a no-op on writable registers.");
            p.ReadData.Describe("AXI4-Lite 32-bit read snapshot; stable while RVALID and not RREADY.");
            p.WriteResponse.Describe("BRESP: 00 OKAY, 10 SLVERR for unaligned/unmapped/read-only writes; stable until BREADY.");
            p.ReadResponse.Describe("RRESP: 00 OKAY, 10 SLVERR for unaligned/unmapped reads (data zero).");
            p.AwProtection.Describe("AWPROT accepted but ignored; no privilege/security filtering.");p.ArProtection.Describe("ARPROT accepted but ignored; no privilege/security filtering.");
            return p;
        }
    }
    public sealed class AxiLiteRegister
    {
        public string Name {get;internal set;}
        public uint Address {get;internal set;}
        public bool Writable {get;internal set;}
        public uint ResetValue {get;internal set;}
        public Expr Value {get;internal set;}
        public Signal Written {get;internal set;}
    }
    /// <summary>32-bit register bank, one buffered write pair and one read response.
    /// AW and W are independent; reads and writes may overlap. Read/write collision reads old data.</summary>
    public sealed class AxiLiteRegisterBank
    {
        readonly Module module;readonly AxiLitePort port;readonly string name;
        readonly List<AxiLiteRegister> registers=new List<AxiLiteRegister>();bool built;
        public AxiLiteRegisterBank(Module module,AxiLitePort port,string name) {
            if(module==null||port==null)throw new ArgumentNullException();
            if(port.Owner!=module)throw new ArgumentException("Foreign AXI port.");
            CheckName(name);this.module=module;this.port=port;this.name=name;
        }
        static void CheckName(string value) {
            if(value==null||!System.Text.RegularExpressions.Regex.IsMatch(value,@"\A[A-Za-z][A-Za-z0-9_]*\z"))throw new ArgumentException("Expected a letter followed by letters, digits or underscores.");
        }
        void Check(string key,uint address) {
            if(built)throw new InvalidOperationException("Register map is frozen.");
            CheckName(key);
            if((address&3)!=0 || (BigInteger)address >= (BigInteger.One<<port.AddressWidth))throw new ArgumentException("Register address must fit and be word-aligned.");
            if(registers.Any(r=>r.Name==key||r.Address==address))throw new ArgumentException("Duplicate register name or address.");
        }
        public AxiLiteRegister AddRegister(string key,uint address,uint resetValue=0) {
            Check(key,address);
            var r=new AxiLiteRegister{Name=key,Address=address,Writable=true,ResetValue=resetValue,
                Value=module.Register(name+"_"+key,32,resetValue),Written=module.Wire(name+"_"+key+"_written",1)};
            r.Value.Describe("AXI register "+key+" at byte offset "+address+"; writable with byte strobes.");
            r.Written.Describe("One-cycle write-commit event for "+key+" with at least one byte enabled. Value updates on this edge.");
            registers.Add(r);return r;
        }
        public void AddStatus(string key,uint address,Expr value) {
            Check(key,address);if(value==null||value.Width!=32)throw new ArgumentException("Status must be 32 bits.");
            registers.Add(new AxiLiteRegister{Name=key,Address=address,Value=value});
        }
        public void Build() {
            if(built)throw new InvalidOperationException("Register bank already built.");built=true;
            var m=module;var p=port;
            var awFull=m.Register(name+"_aw_pending",1,0);var aw=m.Register(name+"_write_address",p.AddressWidth,0);
            var wFull=m.Register(name+"_w_pending",1,0);var wd=m.Register(name+"_write_data",32,0);var ws=m.Register(name+"_byte_enables",4,0);
            var bv=m.Register(name+"_b_valid",1,0);var br=m.Register(name+"_b_response",2,0);
            var rv=m.Register(name+"_r_valid",1,0);var rd=m.Register(name+"_r_data",32,0);var rr=m.Register(name+"_r_response",2,0);
            m.Drive(p.AwReady,~awFull&~bv);m.Drive(p.WriteReady,~wFull&~bv);m.Drive(p.ArReady,~rv);
            var awFire=p.AwValid&p.AwReady;var wFire=p.WriteValid&p.WriteReady;var arFire=p.ArValid&p.ArReady;
            var commit=awFull&wFull&~bv;
            m.Next(awFull,Expr.Mux(commit,Expr.Const(1,0),awFull|awFire));m.Next(wFull,Expr.Mux(commit,Expr.Const(1,0),wFull|wFire));
            m.NextWhen(aw,awFire,p.AwAddress);m.NextWhen(wd,wFire,p.WriteData);m.NextWhen(ws,wFire,p.WriteStrobe);
            Expr writable=Expr.Const(1,0),readable=Expr.Const(1,0),readData=Expr.Const(32,0);
            Expr mask=Expr.Const(32,0);
            for(int i=0;i<4;i++)mask=mask|Expr.Mux(ws.Slice(i,i),Expr.Const(32,(BigInteger)255<<(i*8)),Expr.Const(32,0));
            foreach(var r in registers) {
                var readHit=p.ArAddress.Eq(Expr.Const(p.AddressWidth,r.Address));
                readable=readable|readHit;readData=Expr.Mux(readHit,r.Value,readData);
                if(r.Writable) {
                    var hit=aw.Eq(Expr.Const(p.AddressWidth,r.Address));writable=writable|hit;
                    m.NextWhen((Signal)r.Value,commit&hit,(r.Value&~mask)|(wd&mask));
                    m.Drive(r.Written,commit&hit&~ws.Eq(Expr.Const(4,0)));
                }
            }
            m.Next(bv,Expr.Mux(commit,Expr.Const(1,1),Expr.Mux(p.WriteResponseReady,Expr.Const(1,0),bv)));
            m.NextWhen(br,commit,Expr.Mux(writable,Expr.Const(2,0),Expr.Const(2,2)));
            m.Next(rv,Expr.Mux(arFire,Expr.Const(1,1),Expr.Mux(p.ReadReady,Expr.Const(1,0),rv)));
            m.NextWhen(rd,arFire,readData);m.NextWhen(rr,arFire,Expr.Mux(readable,Expr.Const(2,0),Expr.Const(2,2)));
            m.Drive(p.WriteResponseValid,bv);m.Drive(p.WriteResponse,br);m.Drive(p.ReadValid,rv);m.Drive(p.ReadData,rd);m.Drive(p.ReadResponse,rr);
        }
        public NativeAxiLiteRegisters CreateNative(Func<string,uint> readStatus=null) {
            if(!built)throw new InvalidOperationException("Build the register map first.");
            return new NativeAxiLiteRegisters(registers.ToArray(),port.AddressWidth,readStatus);
        }
    }
    public struct AxiLiteReadResult
    {
        public uint Data {get;private set;}public byte Response {get;private set;}
        internal AxiLiteReadResult(uint data,byte response){Data=data;Response=response;}
    }
    public sealed class NativeAxiLiteRegisters
    {
        readonly AxiLiteRegister[] map;readonly int addressWidth;readonly Func<string,uint> readStatus;
        readonly Dictionary<uint,uint> values=new Dictionary<uint,uint>();
        internal NativeAxiLiteRegisters(AxiLiteRegister[] map,int addressWidth,Func<string,uint> readStatus) {this.map=map;this.addressWidth=addressWidth;this.readStatus=readStatus;Reset();}
        public void Reset(){values.Clear();foreach(var r in map.Where(r=>r.Writable))values.Add(r.Address,r.ResetValue);}
        AxiLiteRegister Find(uint address){return (BigInteger)address>=(BigInteger.One<<addressWidth)?null:map.FirstOrDefault(r=>r.Address==address);}
        public byte Write(uint address,uint data,byte strobes=15) {
            if(strobes>15)throw new ArgumentException("WSTRB must fit four bits.");
            var r=Find(address);if(r==null||!r.Writable)return 2;
            uint mask=0;for(int i=0;i<4;i++)if((strobes&(1<<i))!=0)mask|=255u<<(i*8);
            values[address]=(values[address]&~mask)|(data&mask);return 0;
        }
        public AxiLiteReadResult Read(uint address) {
            var r=Find(address);if(r==null)return new AxiLiteReadResult(0,2);
            if(r.Writable)return new AxiLiteReadResult(values[address],0);
            if(readStatus==null)throw new InvalidOperationException("A native status callback is required for "+r.Name);
            return new AxiLiteReadResult(readStatus(r.Name),0);
        }
    }
}
