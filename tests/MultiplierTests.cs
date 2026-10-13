using System;
using System.IO;
using System.Text;
using System.Numerics;
using System.Collections.Generic;
using SharpHDL;
using HStream = SharpHDL.Stream;

static class MultiplierTests
{
    sealed class Expected { public BigInteger Product; public int Last,User,Due; }
    static int checks;
    static void Check(bool okay,string reason) { checks++;if(!okay)throw new Exception(reason); }
    public static void Run()
    {
        foreach(int w in new[]{4,8,12,32})
            foreach(bool signed in new[]{false,true})
                foreach(MultiplyArchitecture arch in Enum.GetValues(typeof(MultiplyArchitecture)))
                    Exercise(w,signed,arch);
        var m=new Module("RejectMultiply");var s=HStream.Input(m,"input",16);
        foreach(Action action in new Action[]{
            ()=>IntegerMultiply.Build(m,s,"bad",3,MultiplyArchitecture.Parallel),
            ()=>IntegerMultiply.Build(m,s,"bad",4,MultiplyArchitecture.Parallel),
            ()=>IntegerMultiply.Build(m,s,"bad",8,(MultiplyArchitecture)99)})
        { bool rejected=false;try {action();}catch(ArgumentException){rejected=true;}Check(rejected,"Invalid configuration accepted"); }
        Console.WriteLine("PASS: "+checks+" multiplier assertions; 16 RTL trace benches.");
    }
    static void Exercise(int w,bool signed,MultiplyArchitecture arch)
    {
        string name="Multiply"+w+(signed?"Signed":"Unsigned")+arch;
        var m=new Module(name);var input=HStream.Input(m,"input",2*w,4);
        var output=HStream.Output(m,"output",2*w,4);
        HStream.Connect(m,IntegerMultiply.Build(m,input,"mul",w,arch,signed),output);
        var sim=m.Simulate();sim.Tick(true);
        string rtl=m.Verilog();File.WriteAllText("generated/"+name+".v",rtl);
        string graph=m.DumpGraph();File.WriteAllText("generated/"+name+".graph.txt",graph);
        var tb=new StringBuilder();
        tb.AppendLine("module tb; reg clk=0,rst=1; reg ["+(2*w-1)+":0] idata=0; reg ["+(w/4-1)+":0] ikeep='1; reg ilast=0,iv=0,orr=0; reg [3:0] iuser=0; wire ir,ov,olast; wire [3:0] ouser; wire ["+(2*w-1)+":0] odata; wire ["+(w/4-1)+":0] okeep;");
        tb.AppendLine("h_"+name+" dut(.clk(clk),.rst(rst),.s_input_tdata(idata),.s_input_tkeep(ikeep),.s_input_tlast(ilast),.s_input_tuser(iuser),.s_input_tvalid(iv),.s_input_tready(ir),.s_output_tdata(odata),.s_output_tkeep(okeep),.s_output_tlast(olast),.s_output_tuser(ouser),.s_output_tvalid(ov),.s_output_tready(orr)); initial begin #1;clk=1;#1;clk=0;rst=0;");
        var random=new Random(171+w);var queue=new Queue<Expected>();
        BigInteger mask=Expr.Mask(w);int sent=0,received=0,lastAccepted=-10000;
        int total=w==4?256:100;int cycle=0;bool pending=false;BigInteger packed=0;
        int resets=0,phaseSent=0,afterReset=0;
        // First exhaust signed/unsigned 4-bit patterns or wider edge/random vectors.
        // Then flush one transaction mid-computation and one while output is stalled.
        for(;cycle<200000;cycle++)
        {
            bool reset=false;
            if(received==total && queue.Count==0 && resets==0) { resets=1;phaseSent=0; }
            if(resets==1 && phaseSent==1 && cycle-lastAccepted==2) reset=true;
            if(resets==2 && phaseSent==1 && cycle-lastAccepted==(arch==MultiplyArchitecture.Parallel?3:w+3)) reset=true;
            bool offer=sent<total || ((resets==1 || resets==2) && phaseSent==0) || (resets==3 && afterReset<12);
            bool ready=resets==2?false:(cycle%17<10 && random.Next(5)!=0);
            if(resets==3)ready=true;
            if(!pending && offer)
            {
                BigInteger a,b;
                if(w==4 && sent<total) {a=sent&15;b=sent>>4;}
                else if(sent%5==0) {a=mask;b=mask;}
                else if(sent%5==1) {a=BigInteger.One<<(w-1);b=a;}
                else if(sent%5==2) {a=0;b=mask;}
                else {a=((BigInteger)random.Next()<<16|random.Next(65536))&mask;b=((BigInteger)random.Next()<<16|random.Next(65536))&mask;}
                packed=(b<<w)|a;pending=true;
            }
            int last=sent%3==0?1:0,user=sent&15;
            sim.Set(input.Data,packed);sim.Set(input.Keep,Expr.Mask(w/4));sim.Set(input.Last,last);
            sim.Set(input.User,user);sim.Set(input.Valid,pending?1:0);sim.Set(output.Ready,ready?1:0);
            bool ov=sim.Read(output.Valid)==1,ir=sim.Read(input.Ready)==1;
            bool expectedValid=queue.Count>0 && cycle>=queue.Peek().Due;
            Check(ov==expectedValid,name+" latency/valid cycle "+cycle);
            bool expectedReady=arch==MultiplyArchitecture.Parallel?(!expectedValid||ready):
                (cycle>lastAccepted+w && (!expectedValid||ready));
            Check(ir==expectedReady,name+" ready cycle "+cycle);
            tb.AppendLine("idata="+(2*w)+"'d"+packed+";ilast="+last+";iuser="+user+";iv="+(pending?1:0)+";orr="+(ready?1:0)+";rst="+(reset?1:0)+";#1;if(ir!==1'b"+(ir?1:0)+" || ov!==1'b"+(ov?1:0)+")$fatal(1,\"handshake "+cycle+"\");");
            if(ov)
            {
                var e=queue.Peek();Check(sim.Read(output.Data)==e.Product,name+" product");
                Check(sim.Read(output.Last)==e.Last && sim.Read(output.User)==e.User && sim.Read(output.Keep)==Expr.Mask(w/4),name+" sidebands");
                tb.AppendLine("if(odata!=="+(2*w)+"'d"+e.Product+" || olast!==1'b"+e.Last+" || ouser!==4'd"+e.User+" || okeep!=="+(w/4)+"'d"+Expr.Mask(w/4)+")$fatal(1,\"payload "+cycle+"\");");
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
                    if(signed){if(a>(mask>>1))a-=mask+1;if(b>(mask>>1))b-=mask+1;}
                    queue.Enqueue(new Expected{Product=(a*b)&Expr.Mask(2*w),Last=last,User=user,Due=cycle+(arch==MultiplyArchitecture.Parallel?1:w+1)});
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
}
