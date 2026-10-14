using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Numerics;
using System.Collections.Generic;
using SharpHDL;
public static class ControlMemoryTests
{
    static int checks;
    static void Check(bool condition,string message){checks++;if(!condition)throw new Exception("Control/memory: "+message);}
    static void Reject(Action f){checks++;try{f();}catch(ArgumentException){return;}catch(InvalidOperationException){return;}throw new Exception("Expected rejection");}
    sealed class Probe
    {
        readonly Module module;readonly Simulator sim;readonly Dictionary<string,Signal> signals;
        readonly Dictionary<string,BigInteger> inputs=new Dictionary<string,BigInteger>();readonly StringBuilder tb=new StringBuilder();readonly string stem;
        int cycle;
        public Probe(Module m,string stem) {
            module=m;this.stem=stem;signals=m.Signals.ToDictionary(s=>s.Name);sim=m.Simulate();
            File.WriteAllText("generated/control-memory/"+stem+".v",m.Verilog());
            tb.AppendLine("`timescale 1ns/1ps\nmodule tb;reg clk=0,rst=1;");
            foreach(var p in m.Signals.Where(s=>s.Kind=="input"||s.Kind=="output")) {
                tb.AppendLine((p.Kind=="input"?"reg":"wire")+" ["+(p.Width-1)+":0] "+p.Name+(p.Kind=="input"?"=0":"")+";");
                if(p.Kind=="input")inputs.Add(p.Name,0);
            }
            tb.Append("h_"+m.Name+" dut(.clk(clk),.rst(rst)");
            foreach(var p in m.Signals.Where(s=>s.Kind=="input"||s.Kind=="output"))tb.Append(",.s_"+p.Name+"("+p.Name+")");
            tb.AppendLine(");initial begin");Tick(true);
        }
        public void Set(string name,BigInteger value){sim.Set(signals[name],value);inputs[name]=value;}
        public BigInteger Read(string name){return sim.Read(signals[name]);}
        public void Tick(bool reset=false) {
            foreach(var p in inputs)tb.Append(p.Key+"="+signals[p.Key].Width+"'d"+p.Value+";");
            tb.AppendLine("rst="+(reset?1:0)+";#5;clk=1;#5;");sim.Tick(reset);
            foreach(var p in module.Signals.Where(s=>s.Kind=="output"))tb.AppendLine("if("+p.Name+"!=="+p.Width+"'d"+sim.Read(p)+")$fatal(1,\""+stem+" "+p.Name+" cycle "+cycle+"\");");
            tb.AppendLine("clk=0;");cycle++;
        }
        public void Finish(){tb.AppendLine("$display(\"PASS "+stem+" RTL: "+cycle+" cycles\");$finish;end endmodule");File.WriteAllText("build/control-memory/"+stem+"_tb.v",tb.ToString());}
    }
    public static void Run() {
        checks=0;Directory.CreateDirectory("generated/control-memory");Directory.CreateDirectory("build/control-memory");
        Axi();Banks();Capture();MappingFixture();
        var instrument=CaptureInstrument.Build();File.WriteAllText("generated/control-memory/CaptureInstrument.v",instrument.Verilog());
        File.WriteAllText("generated/control-memory/CaptureInstrument_axi.v",AxiLite.Wrapper(instrument));
        Console.WriteLine("PASS control/memory: "+checks+" assertions");
    }
    static void Axi() {
        var malformed=new Module("BadHandshake");malformed.Input("s_axi_rvalid",2);Reject(()=>AxiLite.Wrapper(malformed));
        var m=new Module("AxiRegisters");var port=AxiLitePort.Slave(m,"s_axi",8);var bank=new AxiLiteRegisterBank(m,port,"csr");
        var a=bank.AddRegister("a",0,0x12345678);var b=bank.AddRegister("b",4,0xabcdef01);
        var status=m.Input("status",32);bank.AddStatus("status",8,status);
        var debugA=m.Output("register_a",32);m.Drive(debugA,a.Value);var debugB=m.Output("register_b",32);m.Drive(debugB,b.Value);
        var pulse=m.Output("write_a",1);m.Drive(pulse,a.Written);
        Reject(()=>bank.AddRegister("duplicate",0));Reject(()=>bank.AddRegister("misaligned",2));Reject(()=>bank.AddRegister("outside",256));bank.Build();Reject(()=>bank.AddRegister("late",12));
        uint statusValue=0;var native=bank.CreateNative(n=>statusValue);
        var noCallback=bank.CreateNative();Reject(()=>noCallback.Read(8));Reject(()=>native.Write(0,0,16));
        var p=new Probe(m,"AxiRegisters");var random=new Random(1787);
        Action<uint,uint,byte,int,int,int> write=(address,data,strobe,ad,dd,stall)=>{
            byte expected=native.Write(address,data,strobe);bool aw=false,w=false,response=false;int seen=0;
            for(int t=0;t<150;t++) {
                p.Set("s_axi_awaddr",address);p.Set("s_axi_awvalid",!aw&&t>=ad?1:0);p.Set("s_axi_wdata",data);p.Set("s_axi_wstrb",strobe);p.Set("s_axi_wvalid",!w&&t>=dd?1:0);
                bool bvalid=p.Read("s_axi_bvalid")!=0;
                if(bvalid){Check(aw&&w,"response requires both channels");Check(p.Read("s_axi_bresp")==expected,"BRESP");Check(p.Read("s_axi_awready")==0&&p.Read("s_axi_wready")==0,"pending response blocks next write pair");seen++;}
                bool ready=seen>stall;p.Set("s_axi_bready",ready?1:0);
                bool af=!aw&&t>=ad&&p.Read("s_axi_awready")!=0,wf=!w&&t>=dd&&p.Read("s_axi_wready")!=0;
                if(bvalid&&ready)response=true;p.Tick();aw|=af;w|=wf;
                if(response)break;
            }
            p.Set("s_axi_awvalid",0);p.Set("s_axi_wvalid",0);p.Set("s_axi_bready",0);
            Check(response,"write terminates");Check(p.Read("register_a")==native.Read(0).Data,"register A strobes");Check(p.Read("register_b")==native.Read(4).Data,"register B strobes");
        };
        Action<uint,int> read=(address,stall)=>{
            var expected=native.Read(address);p.Set("s_axi_araddr",address);p.Set("s_axi_arvalid",1);p.Set("s_axi_rready",0);
            Check(p.Read("s_axi_arready")==1,"idle AR ready");p.Tick();p.Set("s_axi_arvalid",0);
            for(int i=0;i<stall+1;i++){if(address==8)p.Set("status",random.Next());Check(p.Read("s_axi_rvalid")==1,"RVALID held");Check(p.Read("s_axi_rdata")==expected.Data,"read snapshot");Check(p.Read("s_axi_rresp")==expected.Response,"RRESP");p.Tick();}
            p.Set("s_axi_rready",1);p.Tick();p.Set("s_axi_rready",0);Check(p.Read("s_axi_rvalid")==0,"read retired");
        };
        for(int i=0;i<160;i++) {
            statusValue=(uint)random.Next();p.Set("status",statusValue);
            uint address=new uint[]{0,4,8,12,1,255}[i%6];uint data=((uint)random.Next()<<1)|(uint)(i&1);
            write(address,data,(byte)(i%16),i%3==0?7:0,i%3==1?9:0,i%7);read(address,i%5);
        }
        // AR on the commit edge returns old data, not the simultaneously written value.
        uint old=native.Read(0).Data;p.Set("s_axi_awaddr",0);p.Set("s_axi_awvalid",1);p.Set("s_axi_wdata",42);p.Set("s_axi_wstrb",15);p.Set("s_axi_wvalid",1);p.Tick();
        p.Set("s_axi_awvalid",0);p.Set("s_axi_wvalid",0);p.Set("s_axi_araddr",0);p.Set("s_axi_arvalid",1);p.Tick();p.Set("s_axi_arvalid",0);
        Check(p.Read("s_axi_rdata")==old&&p.Read("register_a")==42,"read/write collision returns old register");
        p.Tick(true);native.Reset();Check(p.Read("s_axi_bvalid")==0&&p.Read("s_axi_rvalid")==0,"reset drops responses");
        // Reset each half of a write; its mate must never complete an old transaction.
        foreach(bool addressFirst in new[]{true,false}) {
            p.Set("s_axi_awvalid",addressFirst?1:0);p.Set("s_axi_wvalid",addressFirst?0:1);p.Tick();
            p.Set("s_axi_awvalid",0);p.Set("s_axi_wvalid",0);p.Tick(true);
            p.Set("s_axi_awvalid",addressFirst?0:1);p.Set("s_axi_wvalid",addressFirst?1:0);p.Tick();p.Set("s_axi_awvalid",0);p.Set("s_axi_wvalid",0);
            for(int i=0;i<4;i++){p.Tick();Check(p.Read("s_axi_bvalid")==0,"reset half-transaction discarded");}p.Tick(true);
        }
        Check(p.Read("register_a")==0x12345678,"register reset value");p.Finish();
        File.WriteAllText("generated/control-memory/AxiRegisters_axi.v",AxiLite.Wrapper(m));
    }
    static void Banks() {
        Reject(()=>new BankedMemoryLayout(16,3,8));Reject(()=>new BankedMemoryLayout(16,4,0));
        foreach(var target in new[]{MemoryTarget.Generic,MemoryTarget.Xilinx7Series,MemoryTarget.IntelCycloneV,MemoryTarget.IntelArria10}) {
            var policy=new MemoryOptions(target,target==MemoryTarget.Generic?MemoryStorage.Auto:MemoryStorage.Block);
            var layout=new BankedMemoryLayout(16,4,13);var initial=Enumerable.Range(0,layout.Depth).Select(i=>(BigInteger)(i*7)).ToArray();
            var native=new NativeBankedRam(layout,initial);initial[0]=99;Check(native.Read(0)==0,"native owns initialization");initial[0]=0;
            var m=new Module("Banked"+target,policy);var rows=new Expr[4];var we=new Expr[4];var wd=new Expr[4];var re=new Expr[4];
            for(int b=0;b<4;b++){rows[b]=m.Input("row"+b,4);we[b]=m.Input("we"+b,1);wd[b]=m.Input("wd"+b,16);re[b]=m.Input("re"+b,1);}
            var banks=BankedRam.Create(m,"memory",layout,rows,we,wd,re,initial);for(int b=0;b<4;b++)m.Drive(m.Output("q"+b,16),banks.ReadData[b]);
            var p=new Probe(m,"Banked"+target);var random=new Random(1041);var outputs=new BigInteger[4];
            for(int cycle=0;cycle<600;cycle++) {
                bool reset=cycle==201||cycle==399;
                for(int b=0;b<4;b++) {
                    int row=cycle<13?cycle:random.Next(16);bool read=cycle%5!=0,write=cycle>=13&&random.Next(3)!=0;int data=random.Next(65536);
                    p.Set("row"+b,row);p.Set("we"+b,write?1:0);p.Set("wd"+b,data);p.Set("re"+b,read?1:0);
                    if(reset)outputs[b]=0;else {
                        if(read)outputs[b]=row<13?native.Read(row*4+b):0;
                        if(write&&row<13)native.Write(row*4+b,data);
                    }
                }
                p.Tick(reset);for(int b=0;b<4;b++)Check(p.Read("q"+b)==outputs[b],"independent bank old-data/hold/bounds/reset");
            }
            string rtl=m.Verilog();if(target!=MemoryTarget.Generic)Check(rtl.Split(new[]{policy.Attribute},StringSplitOptions.None).Length-1==4,"all banks have target policy");
            for(int word=0;word<layout.Depth;word++)Check(layout.RowForWord(word)*4+layout.BankForWord(word)==word,"cyclic address mapping");
            Reject(()=>native.Read(-1));Reject(()=>native.Write(layout.Depth,1));p.Finish();
        }
    }
    static void MappingFixture() {
        var layout=new BankedMemoryLayout(16,4,256);
        var m=new Module("BankedIntelCycloneVMapping",new MemoryOptions(MemoryTarget.IntelCycloneV,MemoryStorage.Block));
        var rows=new Expr[4];var we=new Expr[4];var wd=new Expr[4];var re=new Expr[4];
        for(int b=0;b<4;b++){rows[b]=m.Input("row"+b,8);we[b]=m.Input("we"+b,1);wd[b]=m.Input("wd"+b,16);re[b]=m.Input("re"+b,1);}
        var ram=BankedRam.Create(m,"memory",layout,rows,we,wd,re);
        for(int b=0;b<4;b++)m.Drive(m.Output("q"+b,16),ram.ReadData[b]);
        File.WriteAllText("generated/control-memory/BankedIntelCycloneVMapping.v",m.Verilog());
    }
    static void Capture() {
        var m=new Module("StreamCapture");var input=SharpHDL.Stream.Input(m,"s_axis",16,8);
        var enable=m.Input("enable",1);var clear=m.Input("clear",1);var re=m.Input("host_read",1);
        var hr0=m.Input("host_row0",3);var hr1=m.Input("host_row1",3);
        var capture=SharpHDL.StreamCapture.Build(m,input,"capture",2,5,enable,clear,new Expr[]{hr0,hr1},re);
        m.Drive(m.Output("count",4),capture.Count);m.Drive(m.Output("busy",1),capture.Busy);m.Drive(m.Output("done",1),capture.Done);m.Drive(m.Output("full",1),capture.Full);
        for(int b=0;b<2;b++)m.Drive(m.Output("host_data"+b,27),capture.Memory.ReadData[b]);
        var p=new Probe(m,"StreamCapture");var model=new NativeStreamCapture(new AxisFormat(16,8),2,5);var random=new Random(885);
        var readData=new BigInteger[2];int accepted=0;
        for(int cycle=0;cycle<1200;cycle++) {
            bool reset=cycle==403,clearing=cycle%47==0,on=cycle%29<23,valid=random.Next(4)!=0,hostRead=cycle%3!=0;
            var beat=new AxisBeat(random.Next(65536),random.Next(4),cycle%17==0,random.Next(256));
            int row0=random.Next(8),row1=random.Next(8);p.Set("host_row0",row0);p.Set("host_row1",row1);p.Set("host_read",hostRead?1:0);
            p.Set("enable",on?1:0);p.Set("clear",clearing?1:0);p.Set("s_axis_tvalid",valid?1:0);p.Set("s_axis_tdata",beat.Data);p.Set("s_axis_tkeep",beat.Keep);p.Set("s_axis_tlast",beat.Last?1:0);p.Set("s_axis_tuser",beat.User);
            bool busy=on&&!model.Done&&!model.Full&&!clearing;
            Check(p.Read("s_axis_tready")== (busy?1:0),"capture ready");
            if(reset){readData[0]=readData[1]=0;model.Reset();}
            else if(clearing)model.Reset();
            else {
                if(hostRead&&!busy){readData[0]=row0<5?model.Memory.Read(row0*2):0;readData[1]=row1<5?model.Memory.Read(row1*2+1):0;}
                if(valid&&busy){Check(model.TryAccept(beat),"native capture accepted");accepted++;}
            }
            p.Tick(reset);Check(p.Read("count")==model.Count,"capture count");Check(p.Read("done")== (model.Done?1:0),"capture done");Check(p.Read("full")== (model.Full?1:0),"capture full");
            for(int b=0;b<2;b++)Check(p.Read("host_data"+b)==readData[b],"capture memory read/hold/reset retention");
        }
        Check(accepted>100,"capture coverage");
        model.Reset();var single=new AxisBeat(42,1,true,7);Check(model.TryAccept(single),"capture last");Check(!model.TryAccept(single),"capture stops at last");var actual=model.Read(0);Check(actual.Data==42&&actual.Keep==1&&actual.Last&&actual.User==7,"native unpack");
        model.Reset();for(int i=0;i<10;i++)Check(model.TryAccept(new AxisBeat(i,3,false,0)),"fill capacity");Check(model.Full&&!model.Done&&!model.TryAccept(single),"full without last");p.Finish();
    }
}
