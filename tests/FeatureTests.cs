using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Numerics;
using SharpHDL;
using HStream=SharpHDL.Stream;

static class FeatureTests
{
    static int assertions;
    static double cordicError,romError;
    static void Assert(bool okay,string message){assertions++;if(!okay)throw new Exception(message);}
    sealed class Beat
    {
        public BigInteger Data,Keep,User;public bool Last;
        public Beat(BigInteger d,BigInteger k,bool l,BigInteger u){Data=d;Keep=k;Last=l;User=u;}
    }
    sealed class Trace
    {
        public Simulator Sim;public Module Module;StringBuilder tb=new StringBuilder();int cycle;
        Signal[] outputs;
        public Trace(Module m)
        {
            Module=m;Sim=m.Simulate();Sim.Tick(true);outputs=m.Ports.Where(p=>p.IsOutput).ToArray();
            File.WriteAllText("generated/"+m.Name+".v",m.Verilog());
            File.WriteAllText("generated/"+m.Name+".graph.txt",m.DumpGraph());
            File.WriteAllText("generated/"+m.Name+"_axi.v",Axi4Stream.Wrapper(m));
            tb.AppendLine("`timescale 1ns/1ps\nmodule tb; reg clk=0; reg rst=1;");
            foreach(var p in m.Ports)tb.AppendLine((p.IsInput?"reg ":"wire ")+"["+(p.Width-1)+":0] "+p.Name+(p.IsInput?"=0;":";"));
            tb.Append("h_"+m.Name+" dut(.clk(clk),.rst(rst)");
            foreach(var p in m.Ports)tb.Append(",.s_"+p.Name+"("+p.Name+")");
            tb.AppendLine("); initial begin #1; clk=1; #1; clk=0; rst=0;");
        }
        public void Set(Signal s,BigInteger v){Sim.Set(s,v);tb.AppendLine(s.Name+"="+s.Width+"'d"+v+";");}
        public bool On(Expr e){return !Sim.Read(e).IsZero;}
        public void Source(HStream s,Beat beat)
        {
            Set(s.Valid,beat==null?0:1);
            if(beat!=null){Set(s.Data,beat.Data);Set(s.Keep,beat.Keep);Set(s.Last,beat.Last?1:0);Set(s.User,beat.User);}
        }
        public void Check(HStream s,Beat beat)
        {
            Assert(Sim.Read(s.Data)==beat.Data,"Payload mismatch "+Module.Name+" cycle "+cycle);
            Assert(Sim.Read(s.Keep)==beat.Keep,"Keep mismatch");Assert(On(s.Last)==beat.Last,"Last mismatch");
            Assert(Sim.Read(s.User)==beat.User,"User mismatch");
        }
        public Beat ReadBeat(HStream s){return new Beat(Sim.Read(s.Data),Sim.Read(s.Keep),On(s.Last),Sim.Read(s.User));}
        public void Step(bool reset=false)
        {
            tb.AppendLine("rst="+(reset?1:0)+"; #1;");
            foreach(var p in outputs)tb.AppendLine("if ("+p.Name+" !== "+p.Width+"'d"+Sim.Read(p)+") $fatal(1, \""+Module.Name+" cycle "+cycle+" port "+p.Name+"\");");
            tb.AppendLine("clk=1; #1; clk=0;");Sim.Tick(reset);cycle++;
        }
        public void Save(){tb.AppendLine("$display(\"PASS HDL "+Module.Name+"\");$finish;end endmodule");File.WriteAllText("generated/"+Module.Name+"_tb.v",tb.ToString());}
    }
    static Beat StallCheck(Trace t,HStream output,Beat stalled)
    {
        if(stalled!=null){Assert(t.On(output.Valid),"Valid dropped under stall");t.Check(output,stalled);}
        return t.On(output.Valid)&&!t.On(output.Ready)?t.ReadBeat(output):null;
    }
    public static void Run()
    {
        NumericPipeline(false);NumericPipeline(true);Skid();DebugController();PeakExample();SignedAndLookup();Latency();Layout();Fifo(1);Fifo(4);Router();Arbiter();Crc();MathCordic();
        foreach(RomFunction f in Enum.GetValues(typeof(RomFunction))){MathRom(f);MathRom(f,true);}
        Console.WriteLine("PASS: "+assertions+" feature assertions; max CORDIC error "+cordicError.ToString("G6")+"; max ROM error "+romError.ToString("G6"));
        File.WriteAllText("generated/math_accuracy.txt","Max CORDIC sine/cosine absolute error: "+cordicError.ToString("G17")+"\nMax normalized table/polynomial absolute error: "+romError.ToString("G17")+"\n");
    }
    static void NumericPipeline(bool power)
    {
        var d=power?NumericExamples.ComplexPower():NumericExamples.QuantizedGain();
        var t=new Trace(d.Module);var queue=new Queue<Beat>();var f=FloatFormat.Compact24;
        var rng=new Random(913);Beat offer=null,stalled=null;int sent=0,received=0;
        for(int cycle=0;cycle<3000 && received<120;cycle++)
        {
            if(offer==null && sent<120 && rng.Next(4)!=0)
            {
                BigInteger data;
                if(power){double r=(sent-60)/8.0,j=(2*sent-70)/16.0;data=f.Encode(r)|(f.Encode(j)<<24);}
                else data=(sent==0?8388607:sent==1?8388608:rng.Next(1<<24));
                offer=new Beat(data,power?63:7,sent%7==0,sent);
            }
            t.Source(d.Input,offer);t.Set(d.Output.Ready,cycle<20||rng.Next(3)==0?0:1);
            stalled=StallCheck(t,d.Output,stalled);
            if(t.On(d.Output.Fire)){Assert(queue.Count>0,"Unexpected numeric output");t.Check(d.Output,queue.Dequeue());received++;}
            if(t.On(d.Input.Fire))
            {
                BigInteger expected;
                if(power){double r=f.Decode(offer.Data&Expr.Mask(24)),j=f.Decode(offer.Data>>24);expected=f.Encode(r*r+j*j);}
                else expected=new BigInteger(Math.Max(-8388608,Math.Min(8388607,Math.Round((double)Expr.SignedValue(offer.Data,24)*1.5))))&Expr.Mask(24);
                queue.Enqueue(new Beat(expected,7,offer.Last,offer.User));offer=null;sent++;
            }
            t.Step();
        }
        Assert(received==120 && queue.Count==0,"Numeric pipeline drain");t.Save();
    }
    static void Skid()
    {
        var m=new Module("SkidBuffer16");var input=HStream.Input(m,"input",16,8);var output=HStream.Output(m,"output",16,8);
        HStream.Connect(m,StreamBlocks.SkidBuffer(m,input,"skid"),output);
        var t=new Trace(m);var queue=new Queue<Beat>();var rng=new Random(7043);
        Beat offer=null,stalled=null;int sent=0,received=0;
        for(int cycle=0;cycle<800;cycle++)
        {
            if(cycle==170) { t.Source(input,null);t.Set(output.Ready,0);t.Step(true);queue.Clear();offer=null;stalled=null;continue; }
            if(offer==null && cycle<650 && (cycle<80||rng.Next(4)!=0))offer=new Beat(sent++ & 65535,rng.Next(4),rng.Next(3)==0,rng.Next(256));
            t.Source(input,offer);t.Set(output.Ready,cycle<2||cycle>=20&&cycle<40||cycle>=160&&cycle<170?0:cycle<80||cycle>650?1:rng.Next(2));
            var ready=t.Sim.Read(input.Ready);t.Set(output.Ready,1-t.Sim.Read(output.Ready));
            Assert(t.Sim.Read(input.Ready)==ready,"Skid ready must not depend combinationally on downstream ready");t.Set(output.Ready,1-t.Sim.Read(output.Ready));
            stalled=StallCheck(t,output,stalled);
            if(cycle>=42 && cycle<80)Assert(t.On(output.Fire),"Skid sustained throughput");
            if(cycle==0)Assert(!t.On(output.Valid),"Skid must register forward payload");
            if(cycle==1)Assert(t.On(output.Valid),"Skid minimum one-cycle latency");
            if(t.On(output.Fire)){Assert(queue.Count>0,"Skid produced extra beat");t.Check(output,queue.Dequeue());received++;}
            if(t.On(input.Fire)){queue.Enqueue(offer);offer=null;}
            Assert(queue.Count<=2,"Skid capacity exceeded");t.Step();
        }
        Assert(queue.Count==0 && received>200,"Skid failed to drain");t.Save();
    }
    static void DebugController()
    {
        var d=DebugUartControllerExample.Build();var t=new Trace(d.Module);var rng=new Random(4093);
        var expected=new Queue<Beat>();var bytes=new Queue<Beat>();
        var commands=new[]{new[]{0},new[]{1,0x5a},new[]{2},new[]{3,0},new[]{4},new[]{2},new[]{99},new[]{3,1},new[]{2},new[]{0}};
        int state=0;BigInteger counter=0,control=1;int tag=0,done=0;Beat offered=null,stalled=null;
        foreach(var command in commands){foreach(int value in command)bytes.Enqueue(new Beat(value,1,true,tag));tag++;}
        int currentTag=0;
        for(int cycle=0;cycle<1000 && (bytes.Count>0 || offered!=null || expected.Count>0 || done<commands.Length);cycle++)
        {
            if(offered==null&&bytes.Count>0&&rng.Next(4)!=0)offered=bytes.Dequeue();
            t.Source(d.Rx,offered);t.Set(d.Tx.Ready,cycle<20?0:rng.Next(3)==0?0:1);
            Assert(t.Sim.Read(d.Counter)==counter,"Debug counter");Assert(t.Sim.Read(d.Control)==control,"Debug control");
            stalled=StallCheck(t,d.Tx,stalled);
            if(t.On(d.Tx.Fire)){Assert(expected.Count>0,"Unexpected UART response");var b=expected.Dequeue();t.Check(d.Tx,b);if(b.Last)done++;}
            BigInteger nextControl=control;
            if(t.On(d.Rx.Fire))
            {
                int value=(int)offered.Data;
                if(state==0)
                {
                    currentTag=(int)offered.User;
                    if(value==1)state=1;
                    else if(value==3)state=2;
                    else if(value==2){for(int i=0;i<4;i++)expected.Enqueue(new Beat((counter>>(8*i))&255,1,i==3,currentTag));}
                    else expected.Enqueue(new Beat(value==0?0xa5:value==4?control:0xe0,1,true,currentTag));
                }
                else {if(state==2)nextControl=value;expected.Enqueue(new Beat(state==1?value:6,1,true,currentTag));state=0;}
                offered=null;
            }
            if((control&1)!=0)counter=(counter+1)&Expr.Mask(32);control=nextControl;t.Step();
        }
        Assert(done==commands.Length && expected.Count==0,"UART controller did not complete");t.Save();
    }
    static void PeakExample()
    {
        var d=PacketPeakExample.Build();var t=new Trace(d.Module);var rng=new Random(2081);
        var source=new Queue<Beat>();var expected=new Queue<Beat>();
        for(int packet=0;packet<60;packet++)
        {
            int count=1+packet%9,peak=0;
            for(int n=0;n<count;n++)
            {
                int value=packet==0?-32768:rng.Next(-32768,32768);peak=Math.Max(peak,Math.Abs(value));
                source.Enqueue(new Beat(value&65535,3,n==count-1,packet));
            }
            expected.Enqueue(new Beat(((BigInteger)count<<16)|peak,15,true,packet));
        }
        Beat offered=null,stalled=null;int received=0;
        for(int cycle=0;cycle<4000&&received<60;cycle++)
        {
            if(offered==null&&source.Count>0&&rng.Next(4)!=0)offered=source.Dequeue();
            t.Source(d.Input,offered);t.Set(d.Output.Ready,cycle<30?0:rng.Next(3)==0?0:1);
            stalled=StallCheck(t,d.Output,stalled);
            if(t.On(d.Output.Fire)){t.Check(d.Output,expected.Dequeue());received++;}
            if(t.On(d.Input.Fire))offered=null;t.Step();
        }
        Assert(received==60,"Packet peak reducer failed to drain");t.Save();
    }
    static void SignedAndLookup()
    {
        var m=new Module("SignedLookup");var a=m.Input("a",8);var b=m.Input("b",8);var adr=m.Input("address",3);
        var asr=m.Output("asr",8);var sext=m.Output("sext",16);var cmp=m.Output("less",1);var lut=m.Output("lut",8);
        m.Drive(asr,a.ArithmeticShift(3));m.Drive(sext,a.SignExtend(16));m.Drive(cmp,a.SignedLt(b));
        m.Drive(lut,Expr.Lookup(8,adr,new BigInteger[]{3,17,255,0,128}));var t=new Trace(m);
        for(int i=0;i<256;i++)
        {
            t.Set(a,i);t.Set(b,255-i);t.Set(adr,i%8);
            int signed=i<128?i:i-256;int bs=(255-i)<128?255-i:255-i-256;
            Assert(t.Sim.Read(asr)==((signed>>3)&255),"Signed shift");Assert(t.Sim.Read(sext)==(signed&65535),"Sign extend");
            Assert(t.On(cmp)==(signed<bs),"Signed compare");int[] table={3,17,255,0,128,0,0,0};Assert(t.Sim.Read(lut)==table[i%8],"ROM default");t.Step();
        }
        t.Save();
    }
    static void Latency()
    {
        for(int backend=0;backend<3;backend++)
        {
            var m=new Module("Latency"+backend);var input=HStream.Input(m,"s_axis",backend==0?24:16,8);
            var output=HStream.Output(m,"m_axis",backend==0?64:24,8);
            var function=backend==0?HwMath.SinCos(m,input,"math"):backend==1?HwMath.RomMath(m,input,"math",RomFunction.Exp2Fraction):HwMath.PolynomialMath(m,input,"math",RomFunction.Log2Mantissa);
            HStream.Connect(m,function,output);var t=new Trace(m);t.Set(output.Ready,1);
            int latency=backend==0?21:backend==1?2:11;
            t.Source(input,new Beat(0,backend==0?7:3,true,81));Assert(t.On(input.Ready),"Empty pipeline must accept");t.Step();t.Source(input,null);
            for(int edge=1;edge<latency;edge++){Assert(!t.On(output.Valid),"Pipeline output too early");t.Step();}
            Assert(t.On(output.Valid),"Pipeline latency incorrect");Assert(t.Sim.Read(output.User)==81,"Latency tag");t.Step();Assert(!t.On(output.Valid),"Pipeline duplicated token");t.Save();
        }
    }
    static void Layout()
    {
        var l=new PacketLayout().Field("version",4).Field("type",12).Field("length",16).Field("source",32);
        var values=new Dictionary<string,BigInteger>{{"version",2},{"type",513},{"length",1200},{"source",0x12345678}};
        var bits=l.PackValue(values);var decoded=l.Decode(bits);
        foreach(var v in values)Assert(decoded[v.Key]==v.Value,"Packet pack/decode");
        var m=new Module("HeaderRewrite");var x=m.Input("word",64);var y=m.Output("word_out",64);m.Drive(y,l.Replace(x,"type",Expr.Const(12,7)));
        var t=new Trace(m);t.Set(x,bits);t.Step();values["type"]=7;Assert(t.Sim.Read(y)==l.PackValue(values),"Header rewrite");t.Save();
    }
    static void Fifo(int depth)
    {
        var m=new Module("Fifo"+depth);var input=HStream.Input(m,"s_axis",32,8);var output=HStream.Output(m,"m_axis",32,8);
        HStream.Connect(m,StreamBlocks.Fifo(m,input,"fifo",depth),output);var t=new Trace(m);var rng=new Random(103+depth);
        var pending=new Queue<Beat>();Beat offered=null,stalled=null;int sent=0,received=0;
        for(int cycle=0;cycle<6000 && (received<400||cycle<300);cycle++)
        {
            if(offered==null && sent<400 && rng.Next(3)!=0)offered=new Beat(sent,1+(sent%15),sent%7==0,sent%256);
            t.Source(input,offered);t.Set(output.Ready,cycle<20?0:rng.Next(4)==0?0:1);
            stalled=StallCheck(t,output,stalled);
            if(t.On(output.Fire)){Assert(pending.Count>0,"Unexpected FIFO output");t.Check(output,pending.Dequeue());received++;}
            if(t.On(input.Fire)){pending.Enqueue(offered);offered=null;sent++;}
            if(cycle==90){t.Step(true);pending.Clear();received=sent;stalled=null;}
            else t.Step();
        }
        Assert(sent==400 && received==400,"FIFO failed to drain");t.Save();
    }
    static void Router()
    {
        var d=RouterDesign.Build(4);var t=new Trace(d.Module);var rng=new Random(311);
        var source=new Queue<Beat>();var expected=new[]{new Queue<Beat>(),new Queue<Beat>()};
        int total=0;
        for(int p=0;p<90;p++)
        {
            int length=1+rng.Next(6),route=p%2;
            var header=d.Header.PackValue(new Dictionary<string,BigInteger>{{"version",1},{"type",route==1?1:99},{"length",length*8},{"source",p}});
            for(int j=0;j<length;j++){var beat=new Beat(j==0?header:new BigInteger(rng.Next())<<32|rng.Next(),j==length-1?15:255,j==length-1,p);source.Enqueue(beat);total++;}
        }
        Beat offered=null;Beat[] stalls={null,null};var outs=new[]{d.Zero,d.One};bool active=false;int selected=0,received=0;
        for(int cycle=0;cycle<10000&&received<total;cycle++)
        {
            if(offered==null && source.Count>0 && rng.Next(5)!=0)offered=source.Dequeue();
            t.Source(d.Input,offered);t.Set(d.Zero.Ready,cycle<50?0:rng.Next(3)==0?0:1);t.Set(d.One.Ready,rng.Next(3)==0?0:1);
            for(int port=0;port<2;port++)
            {stalls[port]=StallCheck(t,outs[port],stalls[port]);if(t.On(outs[port].Fire)){Assert(expected[port].Count>0,"Unexpected route output");t.Check(outs[port],expected[port].Dequeue());received++;}}
            if(t.On(d.Input.Fire))
            {
                if(!active)selected=d.Header.Decode(offered.Data)["type"]==1?1:0;
                expected[selected].Enqueue(offered);active=!offered.Last;offered=null;
            }
            t.Step();
        }
        Assert(received==total,"Router failed to drain");t.Save();
    }
    static void Arbiter()
    {
        var m=new Module("Arbiter");var a=HStream.Input(m,"a_axis",32,8);var b=HStream.Input(m,"b_axis",32,8);var o=HStream.Output(m,"m_axis",32,8);
        HStream.Connect(m,StreamBlocks.Arbitrate(m,a,b,"arb"),o);var t=new Trace(m);var rng=new Random(709);
        var sources=new[]{new Queue<Beat>(),new Queue<Beat>()};int total=0;
        for(int lane=0;lane<2;lane++)for(int packet=0;packet<30;packet++)for(int j=0;j<3;j++)
        {sources[lane].Enqueue(new Beat(lane*10000+packet*10+j,15,j==2,lane));total++;}
        Beat[] offered={null,null};Beat stalled=null;int? locked=null;int received=0;var inputs=new[]{a,b};
        for(int cycle=0;cycle<5000&&received<total;cycle++)
        {
            for(int i=0;i<2;i++){if(offered[i]==null&&sources[i].Count>0&&rng.Next(4)!=0)offered[i]=sources[i].Dequeue();t.Source(inputs[i],offered[i]);}
            t.Set(o.Ready,cycle<15?0:rng.Next(3)==0?0:1);stalled=StallCheck(t,o,stalled);
            bool af=t.On(a.Fire),bf=t.On(b.Fire);Assert(!(af&&bf),"Double arbiter grant");
            if(t.On(o.Fire))
            {
                int lane=af?0:1;Assert(af||bf,"Output without accepted input");Assert(offered[lane]!=null,"Missing arbiter source");t.Check(o,offered[lane]);
                if(locked.HasValue)Assert(locked.Value==lane,"Interleaved packets");
                locked=offered[lane].Last?(int?)null:lane;offered[lane]=null;received++;
            }
            t.Step();
        }
        Assert(received==total,"Arbiter failed to drain");t.Save();
    }
    static uint ReferenceCrc(byte[] bytes)
    {
        uint crc=0xffffffff;
        foreach(byte value in bytes){crc^=value;for(int i=0;i<8;i++)crc=(crc&1)!=0?(crc>>1)^0xedb88320:crc>>1;}
        return ~crc;
    }
    static void Crc()
    {
        var m=new Module("CrcPacket");var input=HStream.Input(m,"s_axis",32);var output=HStream.Output(m,"m_axis",32,32);
        HStream.Connect(m,StreamBlocks.Map(m,PacketMath.AttachCrc32(m,input,"crc"),"buffer",32,x=>x),output);
        var t=new Trace(m);var source=new Queue<Beat>();var expected=new Queue<Beat>();
        var bytes=Encoding.ASCII.GetBytes("123456789");Assert(ReferenceCrc(bytes)==0xcbf43926,"CRC reference vector");
        for(int repeat=0;repeat<3;repeat++)for(int offset=0;offset<bytes.Length;offset+=4)
        {
            int count=Math.Min(4,bytes.Length-offset);BigInteger word=0;for(int i=0;i<count;i++)word|=(BigInteger)bytes[offset+i]<<(8*i);
            bool last=offset+count==bytes.Length;source.Enqueue(new Beat(word,(1<<count)-1,last,0));
            expected.Enqueue(new Beat(word,(1<<count)-1,last,last?0xcbf43926U:0));
        }
        var rng=new Random(919);
        for(int packet=0;packet<12;packet++)
        {
            var packetBeats=new List<Beat>();var enabledBytes=new List<byte>();
            for(int wordIndex=0;wordIndex<4;wordIndex++)
            {
                uint word=(uint)rng.Next();int keep=rng.Next(16);
                for(int lane=0;lane<4;lane++)if((keep&(1<<lane))!=0)enabledBytes.Add((byte)(word>>(8*lane)));
                packetBeats.Add(new Beat(word,keep,wordIndex==3,0));
            }
            uint crc=ReferenceCrc(enabledBytes.ToArray());
            foreach(var beat in packetBeats){source.Enqueue(beat);expected.Enqueue(new Beat(beat.Data,beat.Keep,beat.Last,beat.Last?crc:0));}
        }
        int total=source.Count;Beat offered=null,stalled=null;int received=0;
        for(int cycle=0;cycle<1000&&received<total;cycle++)
        {
            if(offered==null&&source.Count>0)offered=source.Dequeue();t.Source(input,offered);t.Set(output.Ready,rng.Next(3)==0?0:1);
            stalled=StallCheck(t,output,stalled);if(t.On(output.Fire)){t.Check(output,expected.Dequeue());received++;}
            if(t.On(input.Fire))offered=null;t.Step();
        }
        Assert(received==total,"CRC failed to drain");t.Save();
    }
    static void MathCordic()
    {
        var m=new Module("Cordic");var input=HStream.Input(m,"s_axis",24,8);var output=HStream.Output(m,"m_axis",64,8);
        HStream.Connect(m,HwMath.SinCos(m,input,"trig"),output);var t=new Trace(m);var rng=new Random(1543);
        var source=new Queue<Beat>();var pending=new Queue<Beat>();
        // Full circle, cardinal axes, near quadrant boundaries and random angles.
        var phases=new List<BigInteger>();for(int i=0;i<256;i++)phases.Add((BigInteger)i<<16);
        foreach(int boundary in new[]{0,1<<22,1<<23,3<<22})foreach(int delta in new[]{-1,0,1})phases.Add((boundary+delta)&0xffffff);
        for(int i=0;i<128;i++)phases.Add(rng.Next(1<<24));
        int seq=0;foreach(var phase in phases)source.Enqueue(new Beat(phase,7,seq%5==0,(seq++)%256));
        int total=source.Count,received=0;Beat offered=null,stalled=null;
        for(int cycle=0;cycle<10000&&received<total;cycle++)
        {
            if(offered==null&&source.Count>0&&rng.Next(4)!=0)offered=source.Dequeue();t.Source(input,offered);
            t.Set(output.Ready,cycle<60 || (cycle>180&&cycle<220)?0:rng.Next(4)==0?0:1);
            stalled=StallCheck(t,output,stalled);
            if(t.On(output.Fire))
            {
                Assert(pending.Count>0,"Unexpected math output");var expected=pending.Dequeue();
                double theta=(double)expected.Data/(1<<24)*2*Math.PI;
                var bits=t.Sim.Read(output.Data);double cos=HwMath.DecodeSigned(bits&Expr.Mask(32),32,24);double sin=HwMath.DecodeSigned(bits>>32,32,24);
                double error=Math.Max(Math.Abs(cos-Math.Cos(theta)),Math.Abs(sin-Math.Sin(theta)));cordicError=Math.Max(cordicError,error);
                Assert(error<0.000005,"CORDIC error "+error);Assert(t.Sim.Read(output.Keep)==255,"Math keep");
                Assert(t.On(output.Last)==expected.Last && t.Sim.Read(output.User)==expected.User,"Math sideband alignment");received++;
            }
            if(t.On(input.Fire)){pending.Enqueue(offered);offered=null;}t.Step();
        }
        Assert(received==total,"CORDIC failed to drain");t.Save();
    }
    static void MathRom(RomFunction f,bool polynomial=false)
    {
        var m=new Module((polynomial?"Polynomial":"Rom")+f);var input=HStream.Input(m,"s_axis",16,8);var output=HStream.Output(m,"m_axis",24,8);
        HStream.Connect(m,polynomial?HwMath.PolynomialMath(m,input,"math",f):HwMath.RomMath(m,input,"math",f),output);var t=new Trace(m);var rng=new Random(1777+(int)f);
        var source=new Queue<Beat>();var pending=new Queue<Beat>();
        for(int i=0;i<512;i++)source.Enqueue(new Beat(i*128,3,i%9==0,i%256));
        source.Enqueue(new Beat(65535,3,true,255));int total=source.Count,received=0;Beat offered=null,stalled=null;
        for(int cycle=0;cycle<5000&&received<total;cycle++)
        {
            if(offered==null&&source.Count>0&&rng.Next(4)!=0)offered=source.Dequeue();t.Source(input,offered);t.Set(output.Ready,cycle<20?0:rng.Next(3)==0?0:1);
            stalled=StallCheck(t,output,stalled);
            if(t.On(output.Fire))
            {
                Assert(pending.Count>0,"Unexpected ROM output");var expected=pending.Dequeue();
                double actual=(double)t.Sim.Read(output.Data)/(1<<20),reference=HwMath.Reference(f,(double)expected.Data/65536);
                double error=Math.Abs(actual-reference);romError=Math.Max(romError,error);Assert(error<0.000008,"ROM approximation error "+error);
                Assert(t.Sim.Read(output.Keep)==7,"ROM keep");Assert(t.On(output.Last)==expected.Last && t.Sim.Read(output.User)==expected.User,"ROM sidebands");received++;
            }
            if(t.On(input.Fire)){pending.Enqueue(offered);offered=null;}t.Step();
        }
        Assert(received==total,"ROM failed to drain");t.Save();
    }
}
