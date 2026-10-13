using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using SharpHDL;
class LearningTests
{
    static int checks;
    static void Check(bool b){checks++;if(!b)throw new Exception("Learning lab check "+checks);}
    static void Reject(Action f){try{f();}catch(ArgumentException){checks++;return;}catch(InvalidOperationException){checks++;return;}throw new Exception("Expected rejection");}
    static void Save(Module m){File.WriteAllText("generated/learning/"+m.Name+".v",m.Verilog());File.WriteAllText("generated/learning/"+m.Name+".graph.txt",m.DumpGraph());}
    static void Fixed()
    {
        var m=new Module("FixedShifts");var x=m.Input("x",8);var value=new SFixed<W8,F4>(x);
        var outs=new Signal[9,3];for(int n=0;n<=8;n++)
        {
            var bits=new[]{(value<<n).Bits,(value>>n).Bits,value.ShiftRight(n,Rounding.NearestEven).Bits};
            for(int k=0;k<3;k++){outs[n,k]=m.Output(new[]{"left","floor","even"}[k]+n,8);m.Drive(outs[n,k],bits[k]);}
        }
        var sim=m.Simulate();sim.Tick(true);Save(m);
        var tb=new StringBuilder("module tb;reg clk=0,rst=0;reg[7:0] x;\n");
        foreach(var o in outs)tb.AppendLine("wire[7:0] "+o.Name+";");
        tb.Append("h_FixedShifts dut(.clk(clk),.rst(rst),.s_x(x)");foreach(var o in outs)tb.Append(",.s_"+o.Name+"("+o.Name+")");tb.AppendLine(");initial begin");
        var trace=new StringBuilder("raw,n,left,floor,even\n");
        for(int raw=0;raw<256;raw++)
        {
            int signed=raw<128?raw:raw-256;sim.Set(x,raw);tb.AppendLine("x=8'd"+raw+";#1;");
            for(int n=0;n<=8;n++)
            {
                int[] expected={((signed<<n)&255),((int)Math.Floor(signed/Math.Pow(2,n)))&255,((int)Math.Round(signed/Math.Pow(2,n),MidpointRounding.ToEven))&255};
                trace.AppendLine(raw+","+n+","+string.Join(",",expected));
                for(int k=0;k<3;k++){Check(sim.Read(outs[n,k])==expected[k]);tb.AppendLine("if("+outs[n,k].Name+"!==8'd"+expected[k]+")$fatal(1,\"fixed shift\");");}
            }
        }
        tb.AppendLine("$display(\"PASS HDL FixedShifts 6912 values\");$finish;end endmodule");File.WriteAllText("generated/learning/FixedShifts_tb.v",tb.ToString());File.WriteAllText("generated/learning/fixed-trace.csv",trace.ToString());
        Reject(()=>value.ShiftRight(-1,Rounding.Floor));Reject(()=>value.ShiftRight(9,Rounding.Floor));Reject(()=>value.ShiftRight(1,(Rounding)99));
    }
    static void Fsm()
    {
        var m=DrillController.Build();Save(m);var sim=m.Simulate();sim.Tick(true);int phase=0,ticks=0;
        // Access by name is test-harness-only: these files compile alongside the library sources.
        Func<string,Signal> s=name=>m.Signals.Single(x=>x.Name==name);
        var tb=new StringBuilder("module tb;reg clk=0,rst=1,start=0,enable=0,stop=0;wire[1:0] phase;wire[3:0] ticks;wire running,done;h_DrillController dut(.clk(clk),.rst(rst),.s_start(start),.s_enable(enable),.s_stop(stop),.s_phase(phase),.s_ticks(ticks),.s_running(running),.s_done(done));initial begin #1;clk=1;#1;clk=0;rst=0;\n");
        var trace=new StringBuilder("reset,start,enable,stop,phase,ticks,running,done\n");var rng=new Random(744);
        for(int cycle=0;cycle<512;cycle++)
        {
            int start=cycle<12?(cycle==0?1:0):rng.Next(2),enable=cycle<12?1:rng.Next(2),stop=cycle<12?0:(rng.Next(8)==0?1:0);bool reset=cycle%47==46;
            sim.Set(s("start"),start);sim.Set(s("enable"),enable);sim.Set(s("stop"),stop);
            int next=phase==0?(start==1?1:0):phase==1?(stop==1?0:enable==1&&ticks==3?2:1):(start==0?0:2);
            int nt=phase==0?0:phase==1&&enable==1&&stop==0?ticks+1:ticks;
            phase=reset?0:next;ticks=reset?0:nt;sim.Tick(reset);
            Check(sim.Read(s("phase"))==phase);Check(sim.Read(s("ticks"))==ticks);Check(sim.Read(s("running"))==(phase==1?1:0));Check(sim.Read(s("done"))==(phase==2?1:0));
            trace.AppendLine((reset?1:0)+","+start+","+enable+","+stop+","+phase+","+ticks+","+(phase==1?1:0)+","+(phase==2?1:0));
            tb.AppendLine("rst="+(reset?1:0)+";start="+start+";enable="+enable+";stop="+stop+";#1;clk=1;#1;if(phase!==2'd"+phase+"||ticks!==4'd"+ticks+"||running!==1'b"+(phase==1?1:0)+"||done!==1'b"+(phase==2?1:0)+")$fatal(1,\"FSM\");clk=0;");
        }
        tb.AppendLine("rst=0;force dut.s_drill_state=2'b11;#1;clk=1;#1;clk=0;release dut.s_drill_state;#1;clk=1;#1;if(phase!==0)$fatal(1,\"illegal recovery\");$display(\"PASS HDL DrillController 512 cycles and illegal-state recovery\");$finish;end endmodule");
        File.WriteAllText("generated/learning/DrillController_tb.v",tb.ToString());File.WriteAllText("generated/learning/fsm-trace.csv",trace.ToString());
        Reject(()=>new StateMachine(new Module("Bad"),"fsm","A","A"));
        var b=new Module("Priority");var both=b.Input("both",1);var f=new StateMachine(b,"f","A","B","C");f.When("A",both,"B").When("A",both,"C");f.Build();var bs=b.Simulate();bs.Tick(true);bs.Set(both,1);bs.Tick();Check(bs.Read(f.State)==1);bs.Set(both,0);bs.Tick();Check(bs.Read(f.State)==1);
        Reject(()=>f.Build());Reject(()=>f.When("A",both,"C"));
        var bad=new StateMachine(new Module("BadNames"),"s","A","B");Reject(()=>bad.Is("unknown"));Reject(()=>bad.When("A",Expr.Const(2,0),"B"));
    }
    static void Main(){Directory.CreateDirectory("generated/learning");Fixed();Fsm();File.WriteAllText("generated/learning/verification.txt","PASS "+checks+" assertions; fixed shifts and explicit FSM.\n");Console.WriteLine("PASS "+checks+" assertions");}
}
