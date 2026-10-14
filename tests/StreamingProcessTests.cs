using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using System.Text;
using SharpHDL;

static class StreamingProcessTests
{
    static int checks;
    static void Check(bool condition,string message) { checks++;if(!condition)throw new Exception("Stream processes: "+message); }
    static void Reject(Action action) { try{action();}catch(NotSupportedException){checks++;return;}catch(ArgumentException){checks++;return;}catch(InvalidOperationException){checks++;return;}throw new Exception("Expected rejection"); }
    public static void Finite(HwStream<short> input,HwStream<int> output)
    { for(int i=0;i<3;i++){int x=input.Read();if(x<0)output.Write(x-1);else output.Write(x+1);} }
    public static void Divide(HwStream<int> input,HwStream<int> output) { output.Write(100/input.Read()); }
    public static void CheckedAdd(HwStream<int> input,HwStream<int> output) { output.Write(checked(input.Read()+1)); }
    public static void AssertPositive(HwStream<int> input,HwStream<int> output) { int x=input.Read();Hardware.Assert(x>0);output.Write(x); }
    public static void SameChannel(HwStream<int> channel) { channel.Write(channel.Read()); }
    public static void BadAlias(HwStream<int> a,HwStream<int> b) { var alias=a;while(true){b.Write(alias.Read());alias=b;} }
    public static void Narrow(HwStream<UBits<W4>> input,HwStream<UBits<W4>> output) { while(true)output.Write(input.Read()+(UBits<W4>)2u); }
    public static void Run()
    {
        Pipeline();FiniteAndReset();Faults();NarrowTokens();Fifos();Adapters();Rejections();
        File.WriteAllText("generated/streaming_processes.v",StreamingProcesses.Build().Verilog());
        Console.WriteLine("PASS: "+checks+" streaming process/channel assertions and generated RTL trace bench.");
    }
    static void Pipeline()
    {
        var m=StreamingProcesses.Build();var sim=m.Simulate();var ports=m.Ports.ToDictionary(s=>s.Name);
        Action<string,int> set=(n,v)=>sim.Set(ports[n],v);Func<string,int> read=n=>(int)sim.Read(ports[n]);
        set("input_data",0);set("input_valid",0);set("output_tready",0);sim.Tick(true);
        var expected=new Queue<int>();var random=new Random(4813);int sent=0,received=0;bool pending=false,stalled=false;int value=0,held=0;
        var tb=new StringBuilder("module tb; reg clk=0; reg rst=1;\n");
        foreach(var p in m.Ports)tb.AppendLine((p.Kind=="input"?"reg ":"wire ")+"["+(p.Width-1)+":0] "+p.Name+(p.Kind=="input"?"=0":"")+";");
        tb.Append("h_StreamingProcesses dut(.clk(clk),.rst(rst)");foreach(var p in m.Ports)tb.Append(",.s_"+p.Name+"("+p.Name+")");tb.AppendLine("); initial begin #5;clk=1;#5;clk=0;rst=0;");
        for(int cycle=0;cycle<16000&&received<96;cycle++)
        {
            if(!pending&&sent<96&&random.Next(4)!=0){pending=true;value=sent<3?65534+sent:random.Next(65536);value&=65535;}
            int ready=cycle<200?0:random.Next(5)==0?0:1;
            set("input_valid",pending?1:0);set("input_data",value);set("output_tready",ready);
            tb.AppendLine("input_valid="+(pending?1:0)+";input_data=16'd"+value+";output_tready="+ready+";#1;");
            var outputs=m.Ports.Where(p=>p.Kind=="output").ToArray();BigInteger packed=0;int width=0;
            foreach(var p in outputs){packed=(packed<<p.Width)|sim.Read(p);width+=p.Width;}
            tb.AppendLine("if ({"+string.Join(",",outputs.Select(p=>p.Name))+"} !== "+width+"'h"+packed.ToString("x")+") $fatal(1,\"cycle "+cycle+"\");");
            if(stalled)Check(read("output_tvalid")==1&&read("output_tdata")==held,"output changed while stalled");
            if(pending&&read("input_ready")==1){expected.Enqueue(((value+2)&65535)*3&65535);sent++;pending=false;}
            if(read("output_tvalid")==1&&ready==1){Check(expected.Count>0,"unexpected output");Check(read("output_tdata")==expected.Dequeue(),"pipeline value/order");Check(read("output_tkeep")==3&&read("output_tlast")==1&&read("output_tuser")==0,"AXI scalar sidebands");received++;}
            stalled=read("output_tvalid")==1&&ready==0;held=read("output_tdata");
            sim.Tick();tb.AppendLine("#4;clk=1;#5;clk=0;");
        }
        Check(received==96&&expected.Count==0,"pipeline timed out or lost tokens");
        tb.AppendLine("$display(\"PASS streaming process RTL trace\");$finish;end endmodule");
        File.WriteAllText("generated/streaming_processes_tb.v",tb.ToString());
    }
    static void FiniteAndReset()
    {
        var m=new Module("FiniteProcess");var input=HwStream<short>.Input(m,"in");var output=HwStream<int>.Output(m,"out");
        var actor=CilCompiler.AttachProcess(m,"finite",typeof(StreamingProcessTests).GetMethod("Finite"),input,output);
        var sim=m.Simulate();sim.Set(input.Valid,0);sim.Set(input.Data,0);sim.Set(output.Ready,0);sim.Tick(true);
        for(int n=0;n<20;n++)sim.Tick();Check(sim.Read(actor.WaitingForInput)==1,"empty input did not block");
        int[] values={-2,0,32767};int[] results={-3,1,32768};int sent=0,received=0;bool observedStall=false;
        for(int cycle=0;cycle<1000&&sim.Read(actor.Halted)==0;cycle++)
        {
            sim.Set(input.Valid,sent<3?1:0);sim.Set(input.Data,sent<3?values[sent]&65535:0);sim.Set(output.Ready,cycle>60?1:0);
            if(sim.Read(actor.WaitingForOutput)==1)observedStall=true;
            if(sent<3&&sim.Read(input.Ready)==1)sent++;
            if(sim.Read(output.Fire)==1){Check(sim.Read(output.Data)==(new BigInteger(results[received])&Expr.Mask(32)),"signed token/branch/loop");received++;}
            sim.Tick();
        }
        Check(observedStall&&received==3&&sim.Read(actor.Halted)==1&&sim.Read(actor.Fault)==0,"finite process completion");
        sim.Set(input.Valid,0);sim.Tick(true);Check(sim.Read(actor.Halted)==0,"reset did not restart");
        // Reset while an output is blocked must discard the old offer and restart at entry.
        sim.Set(input.Valid,1);sim.Set(input.Data,5);sim.Set(output.Ready,0);
        for(int n=0;n<100&&sim.Read(output.Valid)==0;n++)sim.Tick();Check(sim.Read(output.Valid)==1,"missing blocked offer");
        sim.Set(input.Valid,0);sim.Tick(true);Check(sim.Read(output.Valid)==0,"reset retained old output");
    }
    static void Faults()
    {
        foreach(string method in new[]{"Divide","CheckedAdd","AssertPositive"})
        {
            var m=new Module(method);var input=HwStream<int>.Input(m,"in");var output=HwStream<int>.Output(m,"out");
            var actor=CilCompiler.AttachProcess(m,"actor",typeof(StreamingProcessTests).GetMethod(method),input,output);var sim=m.Simulate();
            sim.Set(input.Data,method=="CheckedAdd"?int.MaxValue:0);sim.Set(input.Valid,1);sim.Set(output.Ready,1);sim.Tick(true);
            for(int i=0;i<100&&sim.Read(actor.Halted)==0;i++){Check(sim.Read(output.Valid)==0,"fault emitted token");sim.Tick();}
            Check(sim.Read(actor.Fault)==1&&sim.Read(actor.Halted)==1,"fault did not halt");
            Check(sim.Read(actor.AssertionFailed)==(method=="AssertPositive"?1:0),"assertion diagnostic");
        }
    }
    static void NarrowTokens()
    {
        var m=new Module("NarrowProcess");var input=HwStream<UBits<W4>>.Input(m,"in");var output=HwStream<UBits<W4>>.Output(m,"out");
        CilCompiler.AttachProcess(m,"narrow",typeof(StreamingProcessTests).GetMethod("Narrow"),input,output);var sim=m.Simulate();
        sim.Set(input.Data,15);sim.Set(input.Valid,1);sim.Set(output.Ready,0);sim.Tick(true);
        for(int n=0;n<100&&sim.Read(output.Valid)==0;n++)sim.Tick();Check(sim.Read(output.Valid)==1&&sim.Read(output.Data)==1,"four-bit wraparound");
    }
    static void Fifos()
    {
        foreach(int depth in new[]{1,2,3,16})
        {
            var m=new Module("Queue"+depth);var input=HwStream<byte>.Input(m,"in");var output=HwStream<byte>.Output(m,"out");
            HwStream<byte>.Connect(m,HwStream<byte>.Fifo(m,input,"fifo",depth),output);var sim=m.Simulate();
            sim.Set(input.Data,0);sim.Set(input.Valid,0);sim.Set(output.Ready,0);sim.Tick(true);
            var q=new Queue<int>();var rng=new Random(depth);bool pending=false;int value=0;
            for(int cycle=0;cycle<1000;cycle++)
            {
                if(!pending&&rng.Next(4)!=0){pending=true;value=rng.Next(256);}sim.Set(input.Valid,pending?1:0);sim.Set(input.Data,value);sim.Set(output.Ready,cycle<30?0:rng.Next(2));
                Check(sim.Read(output.Valid)==(q.Count>0?1:0),"FIFO occupancy/valid");if(q.Count>0)Check(sim.Read(output.Data)==q.Peek(),"FIFO head");
                if(sim.Read(output.Fire)==1)q.Dequeue();if(sim.Read(input.Fire)==1){q.Enqueue(value);pending=false;}Check(q.Count<=depth,"FIFO overflow");sim.Tick();
                if(cycle==500){sim.Tick(true);q.Clear();Check(sim.Read(output.Valid)==0,"FIFO reset flush");}
            }
        }
    }
    static void Adapters()
    {
        var m=new Module("ScalarAdapters");var input=SharpHDL.Stream.Input(m,"in",16);var output=SharpHDL.Stream.Output(m,"out",16);
        var tokens=HwStream<ushort>.Internal(m,"tokens");HwStream<ushort>.FromAxi(m,input,tokens,true);HwStream<ushort>.ToAxi(m,tokens,output);
        var sim=m.Simulate();sim.Set(input.Data,123);sim.Set(input.Valid,1);sim.Set(input.Keep,1);sim.Set(input.Last,0);sim.Set(input.User,1);sim.Set(output.Ready,0);sim.Tick(true);
        Check(sim.Read(output.Data)==123&&sim.Read(output.Valid)==1&&sim.Read(input.Ready)==0,"AXI scalar adapter data/backpressure");
        Check(sim.Read(output.Keep)==3&&sim.Read(output.Last)==1&&sim.Read(output.User)==0,"explicit sideband replacement");
        sim.Set(output.Ready,1);Check(sim.Read(input.Ready)==1,"AXI ready connection");
        var bad=new Module("MisalignedAdapter");Reject(()=>HwStream<UBits<W4>>.ToAxi(bad,HwStream<UBits<W4>>.Input(bad,"in"),SharpHDL.Stream.Output(bad,"out",8)));
    }
    static void Rejections()
    {
        var m=new Module("BadProcess");var a=HwStream<int>.Input(m,"a");var b=HwStream<int>.Output(m,"b");
        Reject(()=>CilCompiler.AttachProcess(m,"same",typeof(StreamingProcessTests).GetMethod("SameChannel"),a));
        Reject(()=>CilCompiler.AttachProcess(m,"alias",typeof(StreamingProcessTests).GetMethod("BadAlias"),a,b));
        Reject(()=>CilCompiler.AttachProcess(m,"duplicate",typeof(StreamingProcessTests).GetMethod("Divide"),a,a));
        Reject(()=>CilCompiler.AttachProcess(new Module("ForeignProcess"),"foreign",typeof(StreamingProcessTests).GetMethod("Divide"),a,b));
        Reject(()=>HwStream<int>.Fifo(m,a,"zero",0));
        Reject(()=>HwStream<int>.FromAxi(m,SharpHDL.Stream.Input(m,"axi",32),b,false));
        CilCompiler.AttachProcess(m,"one",typeof(StreamingProcessTests).GetMethod("Divide"),a,b);
        Reject(()=>CilCompiler.AttachProcess(m,"two",typeof(StreamingProcessTests).GetMethod("Divide"),a,b));
    }
}
