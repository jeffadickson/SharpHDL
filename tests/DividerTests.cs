using System;
using System.IO;
using System.Text;
using System.Numerics;
using System.Collections.Generic;
using SharpHDL;
using HStream = SharpHDL.Stream;

static class DividerTests
{
    sealed class Expected { public BigInteger Product; public int Last,User,Due; }
    static int checks;
    static void Check(bool okay,string reason) { checks++;if(!okay)throw new Exception(reason); }
    public static void Run()
    {
        foreach(int w in new[]{4,8,32})
            foreach(bool signed in new[]{false,true})
                foreach(DivideArchitecture arch in Enum.GetValues(typeof(DivideArchitecture)))
                    foreach(DivisionRounding rounding in Enum.GetValues(typeof(DivisionRounding)))
                        foreach(Overflow overflow in Enum.GetValues(typeof(Overflow)))
                            Exercise(w,signed,arch,rounding,overflow);
        Operators();
        var m=new Module("RejectDivide");var s=HStream.Input(m,"input",16);
        foreach(Action action in new Action[]{
            ()=>IntegerDivide.Build(m,s,"bad",3,DivideArchitecture.Parallel),
            ()=>IntegerDivide.Build(m,s,"bad",4,DivideArchitecture.Parallel),
            ()=>IntegerDivide.Build(m,s,"bad",8,(DivideArchitecture)99),
            ()=>IntegerDivide.Build(m,s,"bad",8,DivideArchitecture.Parallel,(DivisionRounding)99),
            ()=>IntegerDivide.Evaluate(Expr.Const(8,1),Expr.Const(8,2),DivisionRounding.Floor,false,(Overflow)99),
            ()=>IntegerDivide.Evaluate(Expr.Const(8,1),Expr.Const(7,2)),
            ()=>IntegerDivide.ResultWidth(257)})
        { bool rejected=false;try {action();}catch(ArgumentException){rejected=true;}Check(rejected,"Invalid configuration accepted"); }
        Console.WriteLine("PASS: "+checks+" divider assertions; 120 stream RTL trace benches plus operator checks.");
    }
    static void Exercise(int w,bool signed,DivideArchitecture arch,DivisionRounding rounding,Overflow overflow)
    {
        string name="Divide"+w+(signed?"Signed":"Unsigned")+arch+rounding+overflow;
        int resultWidth=IntegerDivide.ResultWidth(w);
        var m=new Module(name);var input=HStream.Input(m,"input",2*w,4);
        var output=HStream.Output(m,"output",resultWidth,4);
        HStream.Connect(m,IntegerDivide.Build(m,input,"div",w,arch,rounding,signed,overflow),output);
        var sim=m.Simulate();sim.Tick(true);
        string rtl=m.Verilog();File.WriteAllText("generated/"+name+".v",rtl);
        string graph=m.DumpGraph();File.WriteAllText("generated/"+name+".graph.txt",graph);
        var tb=new StringBuilder();
        tb.AppendLine("module tb; reg clk=0,rst=1; reg ["+(2*w-1)+":0] idata=0; reg ["+(w/4-1)+":0] ikeep='1; reg ilast=0,iv=0,orr=0; reg [3:0] iuser=0; wire ir,ov,olast; wire [3:0] ouser; wire ["+(resultWidth-1)+":0] odata; wire ["+(resultWidth/8-1)+":0] okeep;");
        tb.AppendLine("h_"+name+" dut(.clk(clk),.rst(rst),.s_input_tdata(idata),.s_input_tkeep(ikeep),.s_input_tlast(ilast),.s_input_tuser(iuser),.s_input_tvalid(iv),.s_input_tready(ir),.s_output_tdata(odata),.s_output_tkeep(okeep),.s_output_tlast(olast),.s_output_tuser(ouser),.s_output_tvalid(ov),.s_output_tready(orr)); initial begin #1;clk=1;#1;clk=0;rst=0;");
        var random=new Random(171+w);var queue=new Queue<Expected>();
        BigInteger mask=Expr.Mask(w);int sent=0,received=0,lastAccepted=-10000;
        int total=w==4?256:64;int cycle=0;bool pending=false;BigInteger packed=0;
        int resets=0,phaseSent=0,afterReset=0;
        // First exhaust signed/unsigned 4-bit patterns or wider edge/random vectors.
        // Then flush one transaction mid-computation and one while output is stalled.
        for(;cycle<200000;cycle++)
        {
            bool reset=false;
            if(received==total && queue.Count==0 && resets==0) { resets=1;phaseSent=0; }
            if(resets==1 && phaseSent==1 && cycle-lastAccepted==2) reset=true;
            if(resets==2 && phaseSent==1 && cycle-lastAccepted==(arch==DivideArchitecture.Parallel?3:w+3)) reset=true;
            bool offer=sent<total || ((resets==1 || resets==2) && phaseSent==0) || (resets==3 && afterReset<12);
            bool ready=resets==2?false:(cycle%17<10 && random.Next(5)!=0);
            if(resets==3)ready=true;
            if(!pending && offer)
            {
                BigInteger a,b;
                if(w==4 && sent<total) {a=sent&15;b=sent>>4;}
                else if(sent%8==0) {a=mask;b=0;}
                else if(sent%8==1) {a=BigInteger.One<<(w-1);b=mask;}
                else if(sent%8==2) {a=mask;b=1;}
                else if(sent%8==3) {a=7;b=2;}
                else if(sent%8==4) {a=5;b=2;}
                else if(sent%8==5) {a=mask-6;b=3;}
                else if(sent%8==6) {a=0;b=mask;}
                else {a=((BigInteger)random.Next()<<16|random.Next(65536))&mask;b=((BigInteger)random.Next()<<16|random.Next(65536))&mask;}
                packed=(b<<w)|a;pending=true;
            }
            int last=sent%3==0?1:0,user=sent&15;
            sim.Set(input.Data,packed);sim.Set(input.Keep,Expr.Mask(w/4));sim.Set(input.Last,last);
            sim.Set(input.User,user);sim.Set(input.Valid,pending?1:0);sim.Set(output.Ready,ready?1:0);
            bool ov=sim.Read(output.Valid)==1,ir=sim.Read(input.Ready)==1;
            bool expectedValid=queue.Count>0 && cycle>=queue.Peek().Due;
            Check(ov==expectedValid,name+" latency/valid cycle "+cycle);
            bool expectedReady=arch==DivideArchitecture.Parallel?(!expectedValid||ready):
                (cycle>lastAccepted+w && (!expectedValid||ready));
            Check(ir==expectedReady,name+" ready cycle "+cycle);
            tb.AppendLine("idata="+(2*w)+"'d"+packed+";ilast="+last+";iuser="+user+";iv="+(pending?1:0)+";orr="+(ready?1:0)+";rst="+(reset?1:0)+";#1;if(ir!==1'b"+(ir?1:0)+" || ov!==1'b"+(ov?1:0)+")$fatal(1,\"handshake "+cycle+"\");");
            if(ov)
            {
                var e=queue.Peek();Check(sim.Read(output.Data)==e.Product,name+" product");
                Check(sim.Read(output.Last)==e.Last && sim.Read(output.User)==e.User && sim.Read(output.Keep)==Expr.Mask(resultWidth/8),name+" sidebands");
                tb.AppendLine("if(odata!=="+resultWidth+"'d"+e.Product+" || olast!==1'b"+e.Last+" || ouser!==4'd"+e.User+" || okeep!=="+(resultWidth/8)+"'d"+Expr.Mask(resultWidth/8)+")$fatal(1,\"payload "+cycle+"\");");
            }
            if(reset)
            {
                queue.Clear();lastAccepted=-10000;pending=false;resets++;phaseSent=0;
            }
            else
            {
                if(ov&&ready){queue.Dequeue();received++;}
                if(pending&&ir)
                {
                    BigInteger a=packed&mask,b=packed>>w;
                    
                    queue.Enqueue(new Expected{Product=Oracle(w,a,b,signed,rounding,overflow),Last=last,User=user,Due=cycle+(arch==DivideArchitecture.Parallel?1:w+1)});
                    lastAccepted=cycle;pending=false;
                    if(resets==1||resets==2)phaseSent++;
                    else if(resets==3)afterReset++;
                    else sent++;
                }
            }
            sim.Tick(reset);tb.AppendLine("clk=1;#1;clk=0;");
            if(resets==3 && afterReset==12 && queue.Count==0)break;
        }
        Check(cycle<200000,name+" did not drain");
        tb.AppendLine("$display(\"PASS HDL "+name+"\");$finish;end endmodule");
        File.WriteAllText("generated/"+name+"_tb.v",tb.ToString());
    }
    // BigInteger division is the independent reference; it does not reuse the RTL algorithm.
    static BigInteger Oracle(int w,BigInteger a,BigInteger b,bool signed,DivisionRounding mode,Overflow policy)
    {
        BigInteger mask=(BigInteger.One<<w)-1;
        if(signed){if(a>(mask>>1))a-=mask+1;if(b>(mask>>1))b-=mask+1;}
        BigInteger q=0,r=a;bool zero=b.IsZero,ov=false;
        if(!zero)
        {
            q=BigInteger.DivRem(a,b,out r);
            bool inexact=!r.IsZero;int direction=a.Sign*b.Sign;
            bool bump= mode==DivisionRounding.AwayFromZero && inexact ||
                mode==DivisionRounding.Floor && direction<0 && inexact ||
                mode==DivisionRounding.Ceiling && direction>0 && inexact;
            if(mode==DivisionRounding.NearestEven)
            {
                int comparison=(2*BigInteger.Abs(r)).CompareTo(BigInteger.Abs(b));
                bump=comparison>0 || comparison==0 && !q.IsEven;
            }
            if(bump)q+=direction;
            r=a-q*b;
            var min=signed?-(BigInteger.One<<(w-1)):BigInteger.Zero;
            var max=signed?(BigInteger.One<<(w-1))-1:mask;
            ov=q<min || q>max;
            if(ov){if(policy==Overflow.Saturate)q=BigInteger.Max(min,BigInteger.Min(max,q));r=0;}
            else Check(a==q*b+r,"Oracle reconstruction");
        }
        return (q&mask)|((r&((BigInteger.One<<(w+1))-1))<<w)|((zero?BigInteger.One:0)<<(2*w+1))|((ov?BigInteger.One:0)<<(2*w+2));
    }
    static void Operators()
    {
        var m=new Module("DivideOperators");var a=m.Input("a",4);var b=m.Input("b",4);
        var ua=new UInt<W4>(a);var ub=new UInt<W4>(b);var sa=new SInt<W4>(a);var sb=new SInt<W4>(b);
        Expr[] expressions={a/b,a%b,(ua/ub).Bits,(ua%ub).Bits,(sa/sb).Bits,(sa%sb).Bits};
        var outputs=new Signal[expressions.Length];
        var tb=new StringBuilder("module tb;reg clk=0,rst=0;reg [3:0] a,b;wire [3:0] q0,q1,q2,q3,q4,q5;h_DivideOperators dut(.clk(clk),.rst(rst),.s_a(a),.s_b(b),.s_q0(q0),.s_q1(q1),.s_q2(q2),.s_q3(q3),.s_q4(q4),.s_q5(q5));initial begin\n");
        for(int i=0;i<6;i++){outputs[i]=m.Output("q"+i,4);m.Drive(outputs[i],expressions[i]);}
        var sim=m.Simulate();sim.Tick(true);
        for(int av=0;av<16;av++)for(int bv=0;bv<16;bv++)
        {
            sim.Set(a,av);sim.Set(b,bv);tb.AppendLine("a=4'd"+av+";b=4'd"+bv+";#1;");
            for(int i=0;i<6;i++)
            {
                var packed=Oracle(4,av,bv,i>=4,DivisionRounding.TowardZero,Overflow.Wrap);
                var expected=(packed>>(i%2==0?0:4))&15;
                Check(sim.Read(outputs[i])==expected,"Division operator");
                tb.AppendLine("if(q"+i+"!==4'd"+expected+")$fatal(1,\"operator\");");
            }
        }
        tb.AppendLine("$display(\"PASS HDL DivideOperators\");$finish;end endmodule");
        File.WriteAllText("generated/DivideOperators.v",m.Verilog());File.WriteAllText("generated/DivideOperators_tb.v",tb.ToString());
        // One-bit signed range and widths beyond machine integers exercise the expression API.
        foreach(int w in new[]{1,3,65})foreach(bool signed in new[]{false,true})
            foreach(DivisionRounding mode in Enum.GetValues(typeof(DivisionRounding)))
                foreach(Overflow policy in Enum.GetValues(typeof(Overflow)))
        {
            var mod=new Module("DirectDivide");var x=mod.Input("x",w);var y=mod.Input("y",w);
            var result=IntegerDivide.Evaluate(x,y,mode,signed,policy).Pack();var output=mod.Output("result",result.Width);mod.Drive(output,result);
            var sm=mod.Simulate();sm.Tick(true);var mask=Expr.Mask(w);
            var values=w==1?new BigInteger[]{0,1}:new BigInteger[]{0,1,2,3,mask>>1,(mask>>1)+1,mask-1,mask};
            foreach(var av in values)foreach(var bv in values)
            {sm.Set(x,av);sm.Set(y,bv);Check(sm.Read(output)==Oracle(w,av,bv,signed,mode,policy),"Direct divide w="+w);}
        }
    }

}
