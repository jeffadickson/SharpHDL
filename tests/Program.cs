using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using System.Text;
using SharpHDL;
class Program
{
    static int checks;
    static void Equal(BigInteger a, BigInteger b) { checks++; if(a!=b) throw new Exception(a+" != "+b); }
    static void Reject(Action f)
    { checks++; try { f(); } catch(ArgumentException) { return; } catch(InvalidOperationException) { return; } throw new Exception("Expected rejection"); }
    static void Main(string[] args)
    {
        Directory.CreateDirectory("generated");
        if(args.Length==1 && args[0]=="--control-memory") { ControlMemoryTests.Run();return; }
        if(args.Length==1 && args[0]=="--frame-reduction") { FrameReductionTests.Run();return; }
        if(args.Length==1 && args[0]=="--axis-interop") { AxisInteropTests.Run();return; }
        if(args.Length==1 && args[0]=="--axi-names") { AxiNameTests.Run();return; }
        if(args.Length==1 && args[0]=="--stream-processes") { StreamingProcessTests.Run();return; }
        if(args.Length==1 && args[0]=="--pipelines") { PipelineTests.Run();return; }
        if(args.Length==1 && args[0]=="--feedback") { FeedbackPipelineTests.Run();return; }
        if(args.Length==1 && args[0]=="--packet-benchmark") { PacketBenchmarkTests.Run();return; }
        if(args.Length==1 && args[0]=="--simulation-benchmark") { SimulationBenchmark.Run();return; }
        if(args.Length==1 && args[0]=="--pipeline-browser") { PipelineBrowserFixture.Run();return; }
        if(args.Length==1 && args[0]=="--memory-targets") { MemoryTargetTests.Run();NativeProcessTests.Run();return; }
        ControlMemoryTests.Run();
        FrameReductionTests.Run();
        AxisInteropTests.Run();
        MemoryTargetTests.Run();NativeProcessTests.Run();
        FeedbackPipelineTests.Run();
        PipelineTests.Run();
        StreamingProcessTests.Run();
        AxiNameTests.Run();
        if(args.Length==1 && args[0]=="--multiply") { MultiplierTests.Run();return; }
        if(args.Length==1 && args[0]=="--divide") { DividerTests.Run();return; }
        File.WriteAllText("generated/counter.v",CounterExample.Build().Verilog());
        File.WriteAllText("generated/counter.graph.txt",CounterExample.Build().DumpGraph());
        Reject(()=>Expr.Const(0,0)); Reject(()=>Expr.Const(8,256));
        Reject(()=>{ var e=Expr.Const(8,0)+Expr.Const(9,0); });
        Reject(()=>Expr.Const(8,0).Slice(8,0));
        var bad=new Module("Bad"); var x=bad.Wire("x",8); var y=bad.Wire("y",8);
        bad.Drive(x,y); bad.Drive(y,x); Reject(()=>bad.Validate());
        var undriven=new Module("Undriven"); undriven.Output("q",1); Reject(()=>undriven.Validate());
        var foreign=new Module("Foreign"); var f=foreign.Input("f",8);
        var owner=new Module("Owner"); var q=owner.Output("q",8); owner.Drive(q,f); Reject(()=>owner.Validate());
        var duplicate=new Module("Duplicate"); var d=duplicate.Output("d",1);
        duplicate.Drive(d,Expr.Const(1,0)); Reject(()=>duplicate.Drive(d,Expr.Const(1,1)));
        var swap=new Module("Swap"); var sa=swap.Register("a",8,17); var sb=swap.Register("b",8,42);
        swap.Next(sa,sb); swap.Next(sb,sa); var ss=swap.Simulate(); Reject(()=>ss.Read(sa));
        ss.Tick(true); ss.Tick(); Equal(ss.Read(sa),42); Equal(ss.Read(sb),17);
        Reject(()=>swap.Input("late",1));
        var rand=new Random(104729);
        int[] widths={1,3,8,17,65};
        foreach(int w in widths) Exercise(w,rand);
        FeatureTests.Run();
        NumericTests.Run();
        MultiplierTests.Run();
        DividerTests.Run();
        Console.WriteLine("PASS: "+checks+" C# assertions; generated five HDL differential testbenches.");
    }
    static BigInteger RandomBits(Random random,int w)
    { var bytes=new byte[(w+7)/8+1]; random.NextBytes(bytes); bytes[bytes.Length-1]=0; return new BigInteger(bytes)&Expr.Mask(w); }
    static void Exercise(int w, Random random)
    {
        var m=new Module("Probe"+w);
        var a=m.Input("a",w); var b=m.Input("b",w); var en=m.Input("en",1);
        var r=m.Register("r",w,0); var prev=m.Register("prev",w,0);
        m.NextWhen(r,en,r+a); m.Next(prev,r);
        Expr[] expressions={a+b,a-b,a*b,a&b,a|b,a^b,~a,a<<2,a>>2,a.Eq(b),a.Lt(b),
            Expr.Mux(en,a,b),Expr.Concat(a,b),a.Slice(w-1,0),a.Resize(w+2),
            (a+b).Resize(w+2),a.Resize(w+2)+b.Resize(w+2),r,prev};
        var outputs=new List<Signal>();
        for(int i=0;i<expressions.Length;i++) { var o=m.Output("o"+i,expressions[i].Width);m.Drive(o,expressions[i]);outputs.Add(o); }
        var sim=m.Simulate(); sim.Tick(true);
        File.WriteAllText("generated/probe"+w+".v",m.Verilog());
        var tb=new StringBuilder("module tb;\nreg clk=0; reg rst=1;\n");
        tb.AppendLine("reg ["+(w-1)+":0] a=0,b=0; reg en=0;");
        foreach(var o in outputs) tb.AppendLine("wire ["+(o.Width-1)+":0] "+o.Name+";");
        tb.Append("h_Probe"+w+" dut(.clk(clk),.rst(rst),.s_a(a),.s_b(b),.s_en(en)");
        foreach(var o in outputs) tb.Append(",.s_"+o.Name+"("+o.Name+")");
        tb.AppendLine(");\ninitial begin\n#1; clk=1; #1; clk=0; rst=0;");
        BigInteger rv=0,pv=0,mask=Expr.Mask(w);
        for(int t=0;t<160;t++)
        {
            var av=t==0?mask:RandomBits(random,w); var bv=t==0?BigInteger.One:RandomBits(random,w);
            int ev=t%3==0?0:1; bool reset=t==79;
            sim.Set(a,av);sim.Set(b,bv);sim.Set(en,ev);
            pv=reset?0:rv;rv=reset?0:(ev==1?(rv+av)&mask:rv);sim.Tick(reset);
            BigInteger[] expected={ (av+bv)&mask,(av-bv)&mask,(av*bv)&mask,av&bv,av|bv,av^bv,(~av)&mask,
                (av<<2)&mask,av>>2,av==bv?1:0,av<bv?1:0,ev==1?av:bv,(av<<w)|bv,av,av,
                (av+bv)&mask,av+bv,rv,pv };
            tb.AppendLine("a="+w+"'d"+av+"; b="+w+"'d"+bv+"; en="+ev+"; rst="+(reset?1:0)+"; #1; clk=1; #1;");
            for(int i=0;i<outputs.Count;i++)
            {
                Equal(sim.Read(outputs[i]),expected[i]);
                tb.AppendLine("if (o"+i+" !== "+outputs[i].Width+"'d"+expected[i]+") $fatal(1, \"width "+w+" cycle "+t+" output "+i+"\");");
            }
            tb.AppendLine("clk=0;");
        }
        tb.AppendLine("$display(\"PASS HDL width "+w+"\"); $finish; end\nendmodule");
        File.WriteAllText("generated/probe"+w+"_tb.v",tb.ToString());
    }
}
