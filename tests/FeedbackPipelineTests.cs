using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using System.Text;
using SharpHDL;

static class FeedbackPipelineTests
{
    static int checks;
    static void Check(bool c,string message){checks++;if(!c)throw new Exception("Feedback pipeline: "+message);}
    public static void UninitializedCarry(HwStream<uint> input,HwStream<uint> output)
    {uint x=0;while(true){uint word=input.Read();while(word>0){x++;word--;}output.Write(x);}}
    public static void Run()
    {
        foreach(bool forwarding in new[]{false,true})
        {
            Test("RunningSum",new uint[]{7},(state,x)=>new uint[]{unchecked(state[0]+x)},state=>state[0],forwarding:forwarding);
            Test("ConditionalChecksum",new uint[]{0x1234,0},(state,x)=>x==0?new uint[]{0,0}:new uint[]{unchecked(state[0]*3)^x,unchecked(state[1]+1)},state=>unchecked(state[0]+state[1]),forwarding:forwarding);
            Test("PreviousValue",new uint[]{42},(state,x)=>new uint[]{x},state=>state[0],true,forwarding);
        }
        Reject("Identity",typeof(PipelineTests));Reject("Checked",typeof(PipelineTests));Reject("UninitializedCarry",typeof(FeedbackPipelineTests));
        Console.WriteLine("PASS: "+checks+" feedback pipeline assertions and generated RTL benches.");
    }
    static void Reject(string method,Type owner)
    {
        var m=new Module("BadFeedback");var input=HwStream<uint>.Input(m,"in");var output=HwStream<uint>.Output(m,"out");bool rejected=false;
        try{CilCompiler.AttachFeedbackPipeline(m,"bad",owner.GetMethod(method),input,output);}catch(NotSupportedException){rejected=true;}
        Check(rejected&&!m.Signals.Any(s=>s.Name.StartsWith("bad_")),"reject unsupported feedback before emitting registers");
    }
    static void Test(string method,uint[] initial,Func<uint[],uint,uint[]> update,Func<uint[],uint> result,bool previous=false,bool forwarding=false)
    {
        string design=method+(forwarding?"Forwarded":"");
        var m=new Module("Feedback"+design);var input=HwStream<uint>.Input(m,"in");var output=HwStream<uint>.Output(m,"out");
        var handle=CilCompiler.AttachFeedbackPipeline(m,"feedback",typeof(FeedbackExamples).GetMethod(method),input,output,forwarding?new FeedbackPipelineOptions{ForwardOnCommit=true}:null);
        Check(handle.StateRegisters.Count==initial.Length,"compiler-only locals became state");
        Check(handle.InitiationInterval==handle.Latency+(forwarding?0:1),"reported external II");
        var sim=m.Simulate();sim.Set(input.Valid,0);sim.Set(input.Data,0);sim.Set(output.Ready,1);sim.Tick(true);
        // An independent recurrence model checks exact acceptance spacing and result latency.
        uint[] committed=(uint[])initial.Clone(),next=null;uint expected=0;int lastAccept=-handle.InitiationInterval,accepted=0,received=0;
        for(int cycle=0;cycle<32*handle.InitiationInterval+handle.Latency+3;cycle++)
        {
            sim.Set(input.Valid,accepted<32?1:0);sim.Set(input.Data,(uint)(accepted+1));
            if(sim.Read(output.Fire)==1)
            {
                Check(next!=null&&sim.Read(output.Data)==expected,"continuous result");Check(cycle-lastAccept==handle.Latency,"continuous latency");committed=next;next=null;received++;
            }
            if(sim.Read(input.Fire)==1)
            {
                Check(next==null,"overlapping dependent iteration");Check(cycle-lastAccept==handle.InitiationInterval,"continuous II");lastAccept=cycle;
                next=update(committed,(uint)(accepted+1));expected=result(previous?committed:next);accepted++;
            }
            sim.Tick();for(int i=0;i<initial.Length;i++)Check(sim.Read(handle.StateRegisters[i])==committed[i],"state commit edge");
        }
        Check(accepted==32&&received==32,"continuous drain");
        sim.Set(input.Valid,0);sim.Set(input.Data,0);sim.Set(output.Ready,0);sim.Tick(true);committed=(uint[])initial.Clone();next=null;
        var tb=new StringBuilder("module tb;reg clk=0,rst=1;reg [31:0] in_data=0;reg in_valid=0,out_ready=0;wire in_ready,out_valid;wire [31:0] out_data;\n");
        tb.AppendLine("h_Feedback"+design+" dut(.clk(clk),.rst(rst),.s_in_data(in_data),.s_in_valid(in_valid),.s_in_ready(in_ready),.s_out_data(out_data),.s_out_valid(out_valid),.s_out_ready(out_ready));initial begin #5;clk=1;#5;clk=0;rst=0;");
        var rng=new Random(905);bool pending=false,held=false;uint word=0,old=0;accepted=0;received=0;int discarded=0,resetCount=0,replacements=0;
        for(int cycle=0;cycle<2200;cycle++)
        {
            if(!pending&&cycle<1800&&rng.Next(4)!=0){pending=true;word=accepted%7==0?0:unchecked((uint)rng.NextInt64(0,1L<<32));}
            bool ready=cycle>=40&&cycle%80<50&&rng.Next(3)!=0;
            sim.Set(input.Valid,pending?1:0);sim.Set(input.Data,word);sim.Set(output.Ready,ready?1:0);
            bool reset=(cycle>100&&resetCount==0&&sim.Read(output.Valid)==1&&!ready)||(cycle>600&&resetCount==1&&sim.Read(handle.Busy)==1&&sim.Read(output.Valid)==0);
            // For a single-stage recurrence output becomes valid immediately, so use a blocked output for its second reset too.
            if(handle.Latency==1&&cycle>600&&resetCount==1&&sim.Read(output.Valid)==1&&!ready)reset=true;
            // Reset must override a committing result and its same-edge replacement.
            if(cycle>1000&&resetCount==2&&sim.Read(output.Fire)==1&&(!forwarding||sim.Read(input.Fire)==1))reset=true;
            tb.AppendLine("rst="+(reset?1:0)+";in_data=32'd"+word+";in_valid="+(pending?1:0)+";out_ready="+(ready?1:0)+";#1;");
            BigInteger observed=((sim.Read(input.Ready)<<33)|(sim.Read(output.Valid)<<32)|sim.Read(output.Data));
            observed=(observed<<1)|sim.Read(handle.Busy);foreach(var state in handle.StateRegisters)observed=(observed<<32)|sim.Read(state);
            string probes="in_ready,out_valid,out_data,dut.s_"+handle.Busy.Name+","+string.Join(",",handle.StateRegisters.Select(s=>"dut.s_"+s.Name));
            tb.AppendLine("if ({"+probes+"} !== "+(35+32*initial.Length)+"'h"+observed.ToString("x")+") $fatal(1,\"feedback cycle "+cycle+"\");#4;clk=1;#5;clk=0;");
            if(held)Check(sim.Read(output.Valid)==1&&sim.Read(output.Data)==old,"blocked output changed");
            if(reset)
            {
                resetCount++;if(next!=null)discarded++;next=null;committed=(uint[])initial.Clone();sim.Tick(true);held=false;
                Check(sim.Read(handle.Busy)==0&&sim.Read(output.Valid)==0,"reset did not flush");
            }
            else
            {
                if(sim.Read(output.Fire)==1&&sim.Read(input.Fire)==1)replacements++;
                if(sim.Read(handle.Busy)==1&&(!forwarding||sim.Read(output.Fire)==0))Check(sim.Read(input.Ready)==0,"busy recurrence accepted stale state");
                if(sim.Read(output.Fire)==1){Check(next!=null&&sim.Read(output.Data)==expected,"random result/order");committed=next;next=null;received++;}
                if(sim.Read(input.Fire)==1){Check(next==null,"more than one dependent input");next=update(committed,word);expected=result(previous?committed:next);pending=false;accepted++;}
                held=sim.Read(output.Valid)==1&&!ready;old=(uint)sim.Read(output.Data);sim.Tick();
            }
            for(int i=0;i<initial.Length;i++)Check(sim.Read(handle.StateRegisters[i])==committed[i],"state changed before output acceptance or reset failed");
            Check(handle.StageValid.Sum(s=>(int)sim.Read(s))==(int)sim.Read(handle.Busy),"one in-flight iteration must match occupancy");
        }
        Check((forwarding?replacements>0:replacements==0),"simultaneous replacement coverage");
        Check(resetCount==3&&next==null&&accepted==received+discarded&&received>40,"random reset/drain accounting");
        string stem="generated/feedback_"+design.ToLowerInvariant();File.WriteAllText(stem+".v",m.Verilog());File.WriteAllText(stem+".schedule.txt",handle.Report);File.WriteAllText(stem+".cil.txt",string.Join("\n",CilCompiler.Disassemble(typeof(FeedbackExamples).GetMethod(method)).Split('\n').Select(line=>line.TrimEnd())));
        tb.AppendLine("$display(\"PASS feedback "+design+" RTL\");$finish;end endmodule");File.WriteAllText(stem+"_tb.v",tb.ToString());
    }
}
