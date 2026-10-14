using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using System.Text;
using SharpHDL;

static class PipelineTests
{
    static int checks;
    static void Check(bool c,string message){checks++;if(!c)throw new Exception("Pipeline: "+message);}
    static void Reject(Action a,string reason){try{a();}catch(NotSupportedException){checks++;return;}catch(ArgumentException){checks++;return;}catch(InvalidOperationException){checks++;return;}throw new Exception("Pipeline should reject "+reason);}
    public static void Identity(HwStream<uint> input,HwStream<uint> output){while(true)output.Write(input.Read());}
    public static void Constant(HwStream<uint> input,HwStream<uint> output){while(true){input.Read();output.Write(7);}}
    public static void Shift(HwStream<uint> input,HwStream<uint> output){while(true){uint x=input.Read();output.Write(unchecked((x>>(int)(x&31))+(x<<3)));}}
    public static void Comparison(HwStream<int> input,HwStream<bool> output){while(true){int x=input.Read();output.Write(x<0);}}
    public static void Narrow(HwStream<UBits<W4>> input,HwStream<UBits<W4>> output){while(true){var x=input.Read();output.Write((x+(UBits<W4>)3u)*(UBits<W4>)5u);}}
    public static void Signed(HwStream<short> input,HwStream<int> output){while(true){int x=input.Read();output.Write((x>>3)-x);}}
    public static void Recurrence(HwStream<uint> input,HwStream<uint> output){uint sum=0;while(true){sum+=input.Read();output.Write(sum);}}
    public static void Branch(HwStream<uint> input,HwStream<uint> output){while(true){uint x=input.Read();if(x>5)output.Write(x);else output.Write(0);}}
    public static void DoubleRead(HwStream<uint> input,HwStream<uint> output){while(true)output.Write(input.Read()+input.Read());}
    public static void DoubleWrite(HwStream<uint> input,HwStream<uint> output){while(true){uint x=input.Read();output.Write(x);output.Write(x);}}
    public static void Checked(HwStream<uint> input,HwStream<uint> output){while(true)output.Write(checked(input.Read()+1));}
    public static void Divide(HwStream<uint> input,HwStream<uint> output){while(true)output.Write(100/input.Read());}
    public static void Finite(HwStream<uint> input,HwStream<uint> output){output.Write(input.Read());}
    public static void Run()
    {
        for(int budget=1;budget<=3;budget++)Polynomial(budget);
        CheckTransform<uint,uint>("Identity",x=>x,1);
        CheckTransform<uint,uint>("Constant",x=>7,1);
        CheckTransform<uint,uint>("Shift",x=>unchecked((x>>(int)(x&31))+(x<<3)),8);
        CheckTransform<int,bool>("Comparison",x=>unchecked((int)x)<0?1u:0u,1);
        CheckTransform<short,int>("Signed",x=>unchecked((uint)(((short)x>>3)-(short)x)),1);
        CheckTransform<UBits<W4>,UBits<W4>>("Narrow",x=>(((x+3)&15)*5)&15,2);
        Rejections();Console.WriteLine("PASS: "+checks+" automatic pipeline assertions and scheduled RTL benches.");
    }
    static void Polynomial(int budget)
    {
        PipelineHandle handle;var m=ScheduledPipeline.Build(out handle,budget);
        int depth=(3+budget-1)/budget;Check(handle.Latency==depth&&handle.InitiationInterval==1,"schedule depth/II");
        Check(handle.Report.Contains("Payload register bits")&&handle.PayloadRegisterBits>0,"missing schedule report");
        string stem="generated/scheduled_polynomial"+budget;
        File.WriteAllText(stem+".v",m.Verilog());File.WriteAllText(stem+".schedule.txt",handle.Report);
        var p=m.Ports.ToDictionary(s=>s.Name);var sim=m.Simulate();sim.Set(p["input_valid"],0);sim.Set(p["input_data"],0);sim.Set(p["output_tready"],1);sim.Tick(true);
        // First prove continuous throughput and exact no-stall latency against an independent cycle model.
        var q=new Queue<Tuple<int,int>>();int received=0;
        for(int cycle=0;cycle<80+depth;cycle++)
        {
            bool offer=cycle<80;int x=cycle*733&65535;sim.Set(p["input_valid"],offer?1:0);sim.Set(p["input_data"],x);
            Check(sim.Read(p["input_ready"])==1,"continuous source unexpectedly stalled");
            Check(sim.Read(p["output_tvalid"])==(cycle>=depth&&cycle<80+depth?1:0),"no-stall latency/II");
            if(sim.Read(p["output_tvalid"])==1){var e=q.Dequeue();Check(cycle-e.Item1==depth,"wrong latency");Check(sim.Read(p["output_tdata"])==e.Item2,"continuous polynomial result");received++;}
            if(offer)q.Enqueue(Tuple.Create(cycle,Expected(x)));sim.Tick();
        }
        Check(received==80&&q.Count==0,"continuous drain");
        sim.Tick(true);sim.Set(p["input_valid"],0);sim.Set(p["output_tready"],0);sim.Set(p["input_data"],0);
        var tb=new StringBuilder("module tb;reg clk=0,rst=1;\n");
        foreach(var port in m.Ports)tb.AppendLine((port.IsInput?"reg ":"wire ")+"["+(port.Width-1)+":0] "+port.Name+(port.IsInput?"=0":"")+";");
        tb.Append("h_ScheduledPolynomial"+budget+" dut(.clk(clk),.rst(rst)");foreach(var port in m.Ports)tb.Append(",.s_"+port.Name+"("+port.Name+")");tb.AppendLine(");initial begin #5;clk=1;#5;clk=0;rst=0;");
        var expected=new Queue<int>();var rng=new Random(219+budget);bool pending=false,stalled=false;int held=0,value=0,accepted=0;received=0;int discarded=0;
        for(int cycle=0;cycle<1800;cycle++)
        {
            bool reset=cycle==400||cycle==1000;
            if(!pending&&cycle<1600&&rng.Next(4)!=0){pending=true;value=rng.Next(65536);}
            bool ready=cycle>=30&&cycle%100<65&&rng.Next(4)!=0;
            sim.Set(p["input_valid"],pending?1:0);sim.Set(p["input_data"],value);sim.Set(p["output_tready"],ready?1:0);
            tb.AppendLine("rst="+(reset?1:0)+";input_valid="+(pending?1:0)+";input_data="+value+";output_tready="+(ready?1:0)+";#1;");
            var outputs=m.Ports.Where(s=>s.IsOutput).ToArray();BigInteger packed=0;int width=0;foreach(var port in outputs){packed=(packed<<port.Width)|sim.Read(port);width+=port.Width;}
            tb.AppendLine("if ({"+string.Join(",",outputs.Select(s=>s.Name))+"} !== "+width+"'h"+packed.ToString("x")+") $fatal(1,\"scheduled cycle "+cycle+"\");#4;clk=1;#5;clk=0;");
            if(stalled)Check(sim.Read(p["output_tvalid"])==1&&sim.Read(p["output_tdata"])==held,"stalled output changed");
            if(reset){discarded+=expected.Count;expected.Clear();sim.Tick(true);stalled=false;Check(handle.StageValid.All(s=>sim.Read(s)==0),"reset did not flush pipeline");continue;}
            if(sim.Read(p["output_tvalid"])==1&&ready){Check(expected.Count>0,"unexpected output");Check(sim.Read(p["output_tdata"])==expected.Dequeue(),"random reconvergent result/order");Check(sim.Read(p["output_tkeep"])==3&&sim.Read(p["output_tlast"])==1,"AXI metadata");received++;}
            if(pending&&sim.Read(p["input_ready"])==1){expected.Enqueue(Expected(value));accepted++;pending=false;}
            Check(expected.Count<=depth,"pipeline capacity exceeded");
            if(cycle==29)Check(expected.Count==depth&&sim.Read(p["input_ready"])==0,"full pipeline did not backpressure");
            stalled=sim.Read(p["output_tvalid"])==1&&!ready;held=(int)sim.Read(p["output_tdata"]);sim.Tick();
        }
        Check(expected.Count==0&&received+discarded==accepted,"random drain/reset accounting");Check(received>400,"insufficient random traffic");
        tb.AppendLine("$display(\"PASS scheduled polynomial "+budget+" RTL\");$finish;end endmodule");File.WriteAllText(stem+"_tb.v",tb.ToString());
    }
    static int Expected(int x){return unchecked((ushort)(((uint)x+2u)*((uint)x*3u)^(uint)x));}
    static void CheckTransform<TIn,TOut>(string method,Func<uint,uint> oracle,int latency) where TIn:struct where TOut:struct
    {
        var m=new Module("Pipeline"+method);var input=HwStream<TIn>.Input(m,"in");var output=HwStream<TOut>.Output(m,"out");
        var handle=CilCompiler.AttachPipeline(m,"actor",typeof(PipelineTests).GetMethod(method),input,output);Check(handle.Latency==latency,"latency of "+method+": "+handle.Latency+" expected "+latency);
        var sim=m.Simulate();sim.Set(input.Valid,0);sim.Set(input.Data,0);sim.Set(output.Ready,0);sim.Tick(true);
        var q=new Queue<uint>();var rng=new Random(104);bool pending=false,held=false;uint x=0,old=0;int sent=0,received=0;
        for(int cycle=0;cycle<1500;cycle++)
        {
            if(!pending&&sent<256){x=unchecked((uint)rng.NextInt64(0,1L<<32)) & (uint)Expr.Mask(input.Data.Width);pending=true;}
            sim.Set(input.Data,x);sim.Set(input.Valid,pending?1:0);sim.Set(output.Ready,rng.Next(4)!=0?1:0);
            if(held)Check(sim.Read(output.Valid)==1&&sim.Read(output.Data)==old,"stable "+method);
            if(sim.Read(output.Fire)==1){Check(q.Count>0&&sim.Read(output.Data)==q.Dequeue(),"oracle "+method);received++;}
            if(sim.Read(input.Fire)==1){q.Enqueue(oracle(x)&(uint)Expr.Mask(output.Data.Width));pending=false;sent++;}
            held=sim.Read(output.Valid)==1&&sim.Read(output.Ready)==0;old=(uint)sim.Read(output.Data);sim.Tick();
        }
        Check(received==256&&q.Count==0,"drain "+method);
    }
    static void Rejections()
    {
        foreach(string method in new[]{"Recurrence","Branch","DoubleRead","DoubleWrite","Checked","Divide","Finite"})
        {
            var m=new Module("Bad"+method);var input=HwStream<uint>.Input(m,"in");var output=HwStream<uint>.Output(m,"out");
            Reject(()=>CilCompiler.AttachPipeline(m,"bad",typeof(PipelineTests).GetMethod(method),input,output),method);
            Check(!m.Signals.Any(s=>s.Name.StartsWith("bad_")),"rejected body mutated module");
        }
        foreach(var options in new[]{new PipelineOptions{LogicLevelsPerStage=0},new PipelineOptions{InitiationInterval=2},new PipelineOptions{MaxStages=1}})
        {
            var m=new Module("BadOptions");var input=HwStream<ushort>.Input(m,"in");var output=HwStream<ushort>.Output(m,"out");
            Reject(()=>CilCompiler.AttachPipeline(m,"bad",typeof(ScheduledPipeline).GetMethod("Polynomial"),input,output,options),"options");
        }
    }
}
