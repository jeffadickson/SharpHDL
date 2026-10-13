using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Numerics;
using System.Text;
using SharpHDL;
using Module=SharpHDL.Module;
using HStream=SharpHDL.Stream;
using Behavioral;
class Program
{
    static int checks,benches;static string directory;
    static void Check(bool x,string reason){checks++;if(!x)throw new Exception(reason);}
    static void Write(string file,string text){File.WriteAllText(Path.Combine(directory,file),text);}
    static int Width(ParameterInfo p){var a=p.GetCustomAttribute<PortWidthAttribute>();return a==null?(p.ParameterType==typeof(bool)?1:32):a.Width;}
    static object Native(BigInteger raw,ParameterInfo p)
    {if(p.ParameterType==typeof(bool))return raw!=0;if(p.ParameterType==typeof(uint))return (uint)raw;return (int)Expr.SignedValue(raw,Width(p));}
    static BigInteger Bits(object x){if(x is bool)return (bool)x?1:0;if(x is uint)return (uint)x;return (int)x;}
    static void Function(MethodInfo method,string tag)
    {
        string name="Cil"+tag+method.Name;var m=new Module(name);var ps=method.GetParameters();
        var a=m.Input("a",Width(ps[0]));var b=m.Input("b",Width(ps[1]));
        var compiled=CilCompiler.Compile(method,new Expr[]{a,b});var y=m.Output("y",compiled.Value.Width);var fault=m.Output("fault",1);
        m.Drive(y,compiled.Value);m.Drive(fault,compiled.Fault);var sim=m.Simulate();sim.Tick(true);
        Write(name+".v",m.Verilog());Write(name+".graph.txt",m.DumpGraph());Write(name+".cil.txt",compiled.Report);
        var tb=new StringBuilder("module tb;reg clk=0,rst=0;reg ["+(a.Width-1)+":0] a;reg ["+(b.Width-1)+":0] b;wire ["+(y.Width-1)+":0] y;wire fault;h_"+name+" dut(.clk(clk),.rst(rst),.s_a(a),.s_b(b),.s_y(y),.s_fault(fault));initial begin\n");
        int[] edges={int.MinValue,int.MinValue+1,-256,-128,-7,-2,-1,0,1,2,3,4,5,7,127,128,255,int.MaxValue-1,int.MaxValue};
        var rng=new Random(4721);int vectors=edges.Length*edges.Length+160;
        for(int n=0;n<vectors;n++)
        {
            BigInteger av=n<edges.Length*edges.Length?edges[n/edges.Length]:unchecked((int)((uint)rng.Next()<<1|(uint)rng.Next(2)));
            BigInteger bv=n<edges.Length*edges.Length?edges[n%edges.Length]:unchecked((int)((uint)rng.Next()<<1|(uint)rng.Next(2)));
            av&=Expr.Mask(a.Width);bv&=Expr.Mask(b.Width);bool failed=false;BigInteger expected=0;
            try{expected=Bits(method.Invoke(null,new[]{Native(av,ps[0]),Native(bv,ps[1])}))&Expr.Mask(y.Width);}
            catch(TargetInvocationException ex){if(ex.InnerException is OverflowException||ex.InnerException is DivideByZeroException)failed=true;else throw;}
            sim.Set(a,av);sim.Set(b,bv);
            Check(sim.Read(fault)==(failed?1:0),name+" fault A="+av+" B="+bv+" expected="+failed);
            if(!failed)Check(sim.Read(y)==expected,name+" value A="+av+" B="+bv);
            tb.AppendLine("a="+a.Width+"'d"+av+";b="+b.Width+"'d"+bv+";#1;if(fault!==1'b"+(failed?1:0)+")$fatal(1,\"fault "+n+"\");");
            if(!failed)tb.AppendLine("if(y!=="+y.Width+"'d"+expected+")$fatal(1,\"value "+n+"\");");
        }
        tb.AppendLine("$display(\"PASS HDL "+name+"\");$finish;end endmodule");Write(name+"_tb.v",tb.ToString());benches++;
        Console.WriteLine("PASS "+name+" "+vectors+" software/hardware vectors");
    }
    static void Reject(MethodInfo method,string expected,CilOptions options=null)
    {
        try { CilCompiler.BuildModule(method,"Reject",options);throw new Exception("Accepted "+method.Name); }
        catch(NotSupportedException e){Check(e.Message.Contains(expected),"Wrong rejection: "+e.Message);Write(method.Name+".rejected.txt",e.Message+"\n");}
    }
    static void Counter()
    {
        var m=new Module("CilCounter");var enable=m.Input("enable",1);var count=m.Register("count",32,0);
        var result=CilCompiler.Compile(typeof(CSharpFunctions).GetMethod("NextCounter"),new Expr[]{count,enable});
        m.Next(count,result.Value);var y=m.Output("y",32);m.Drive(y,count);var f=m.Output("fault",1);m.Drive(f,result.Fault);
        var sim=m.Simulate();sim.Tick(true);uint reference=0;
        var tb=new StringBuilder("module tb;reg clk=0,rst=1,enable=0;wire [31:0] y;wire fault;h_CilCounter dut(.clk(clk),.rst(rst),.s_enable(enable),.s_y(y),.s_fault(fault));initial begin #1;clk=1;#1;clk=0;rst=0;\n");
        for(int i=0;i<160;i++)
        {bool en=i%3!=0,reset=i==73;sim.Set(enable,en?1:0);Check(sim.Read(y)==reference,"Counter state");Check(sim.Read(f)==0,"Counter fault");
         tb.AppendLine("enable="+(en?1:0)+";rst="+(reset?1:0)+";#1;if(y!==32'd"+reference+" || fault!==0)$fatal(1,\"counter\");clk=1;#1;clk=0;");
         sim.Tick(reset);reference=reset?0:(uint)CSharpFunctions.NextCounter((int)reference,en);}
        Write("CilCounter.v",m.Verilog());tb.AppendLine("$display(\"PASS HDL CilCounter\");$finish;end endmodule");Write("CilCounter_tb.v",tb.ToString());benches++;
    }
    static void RamTest(int depth)
    {
        string name="Ram"+depth;int aw=1;while((1<<aw)<depth)aw++;
        var m=new Module(name);var address=m.Input("address",aw);var wd=m.Input("wd",16);var we=m.Input("we",1);var re=m.Input("re",1);
        var initial=Enumerable.Range(0,depth).Select(i=>(BigInteger)(i*17)).ToArray();
        var ram=SynchronousRam.Create(m,"storage",16,depth,address,we,wd,re,initial);var y=m.Output("y",16);m.Drive(y,ram.ReadData);
        var sim=m.Simulate();sim.Tick(true);var cells=(BigInteger[])initial.Clone();BigInteger expected=0;
        var tb=new StringBuilder("module tb;reg clk=0,rst=1;reg ["+(aw-1)+":0] address=0;reg [15:0] wd=0;reg we=0,re=0;wire [15:0] y;h_"+name+" dut(.clk(clk),.rst(rst),.s_address(address),.s_wd(wd),.s_we(we),.s_re(re),.s_y(y));initial begin #1;clk=1;#1;clk=0;rst=0;\n");
        var rng=new Random(995);for(int i=0;i<600;i++)
        {
            int a=i<depth?i:rng.Next(1<<aw),data=rng.Next(65536);bool read=i%5!=0,write=i>=depth&&i%3!=0,reset=i==depth+45;
            sim.Set(address,a);sim.Set(wd,data);sim.Set(we,write?1:0);sim.Set(re,read?1:0);
            Check(sim.Read(y)==expected,"RAM held output before edge");
            var next=reset?BigInteger.Zero:read?(a<depth?cells[a]:0):expected;
            if(!reset&&write&&a<depth)cells[a]=data;
            sim.Tick(reset);expected=next;Check(sim.Read(y)==expected,"RAM old data, enable, bounds, initialization and reset");
            tb.AppendLine("address="+aw+"'d"+a+";wd=16'd"+data+";we="+(write?1:0)+";re="+(read?1:0)+";rst="+(reset?1:0)+";#1;clk=1;#1;if(y!==16'd"+expected+")$fatal(1,\"RAM edge "+i+"\");clk=0;");
        }
        Write(name+".v",m.Verilog());Write(name+".graph.txt",m.DumpGraph());tb.AppendLine("$display(\"PASS HDL "+name+"\");$finish;end endmodule");Write(name+"_tb.v",tb.ToString());benches++;
    }
    sealed class Pending { public BigInteger Value;public bool Fault;public int Last,User,Cycle; }
    static void MixedPipeline()
    {
        var m=new Module("CilMixedPipeline");var input=HStream.Input(m,"input",64,4);var output=HStream.Output(m,"output",40,4);
        var fs=typeof(FSharpFunctions).GetMethod("Minimum");var vb=typeof(VisualBasicFunctions).GetMethod("SelectArithmetic");
        var minimum=StreamBlocks.Map(m,input,"fsharp",40,x=>{var r=CilCompiler.Compile(fs,new[]{x.Slice(31,0),x.Slice(63,32)});return Expr.Concat(r.Fault,r.Value).Resize(40);});
        var skid=StreamBlocks.SkidBuffer(m,minimum,"skid");
        var finish=StreamBlocks.Map(m,skid,"vb",40,x=>{var r=CilCompiler.Compile(vb,new[]{x.Slice(31,0),Expr.Const(32,2)});return Expr.Concat(r.Fault|x.Slice(32,32),r.Value).Resize(40);});
        HStream.Connect(m,finish,output);var sim=m.Simulate();sim.Tick(true);var queue=new System.Collections.Generic.Queue<Pending>();
        var tb=new StringBuilder("module tb;reg clk=0,rst=1;reg [63:0] id=0;reg iv=0,il=0,orr=0;reg [3:0] iu=0;wire ir,ov,ol;wire [39:0] od;wire [4:0] ok;wire [3:0] ou;h_CilMixedPipeline dut(.clk(clk),.rst(rst),.s_input_tdata(id),.s_input_tkeep(8'hff),.s_input_tlast(il),.s_input_tuser(iu),.s_input_tvalid(iv),.s_input_tready(ir),.s_output_tdata(od),.s_output_tkeep(ok),.s_output_tlast(ol),.s_output_tuser(ou),.s_output_tvalid(ov),.s_output_tready(orr));initial begin #1;clk=1;#1;clk=0;rst=0;\n");
        int sent=0,received=0;var rng=new Random(751);bool held=false;BigInteger packed=0;
        for(int cycle=0;cycle<4000&&received<120;cycle++)
        {
            if(!held&&sent<120){int a=sent%13==0?int.MaxValue:rng.Next(-1000,1000),b=sent%13==0?int.MaxValue:rng.Next(-1000,1000);packed=((BigInteger)(uint)a)|((BigInteger)(uint)b<<32);held=true;}
            bool ready=cycle<15||rng.Next(4)!=0&&cycle%29<20;int user=sent&15,last=sent%7==0?1:0;
            sim.Set(input.Data,packed);sim.Set(input.Keep,255);sim.Set(input.Valid,held?1:0);sim.Set(input.Last,last);sim.Set(input.User,user);sim.Set(output.Ready,ready?1:0);
            bool valid=sim.Read(output.Valid)==1,ir=sim.Read(input.Ready)==1;
            tb.AppendLine("id=64'd"+packed+";iv="+(held?1:0)+";il="+last+";iu="+user+";orr="+(ready?1:0)+";#1;if(ir!==1'b"+(ir?1:0)+" || ov!==1'b"+(valid?1:0)+")$fatal(1,\"flow\");");
            if(valid)
            {
                Check(queue.Count>0,"Unsolicited pipeline output");var e=queue.Peek();var actual=sim.Read(output.Data);
                Check(((actual>>32)&1)==(e.Fault?1:0),"Pipeline fault");if(!e.Fault)Check((actual&Expr.Mask(32))==e.Value,"Pipeline value");
                Check((actual>>33)==0&&sim.Read(output.Last)==e.Last&&sim.Read(output.User)==e.User&&sim.Read(output.Keep)==31,"Pipeline sidebands");
                tb.AppendLine("if(od[32]!==1'b"+(e.Fault?1:0)+" || od[39:33]!==0 || ol!==1'b"+e.Last+" || ou!==4'd"+e.User+" || ok!==5'd31)$fatal(1,\"metadata\");");
                if(!e.Fault)tb.AppendLine("if(od[31:0]!==32'd"+e.Value+")$fatal(1,\"pipeline result\");");
                if(ready){if(cycle<15)Check(cycle-e.Cycle==3,"Pipeline latency");queue.Dequeue();received++;}
            }
            if(held&&ir)
            {
                int a=unchecked((int)(uint)(packed&Expr.Mask(32))),b=unchecked((int)(uint)(packed>>32));var e=new Pending{Last=last,User=user,Cycle=cycle};
                int min=FSharpFunctions.Minimum(a,b);try{e.Value=(BigInteger)(uint)VisualBasicFunctions.SelectArithmetic(min,2);}catch(OverflowException){e.Fault=true;}
                queue.Enqueue(e);held=false;sent++;
            }
            sim.Tick();tb.AppendLine("clk=1;#1;clk=0;");
        }
        Check(received==120&&queue.Count==0,"Pipeline drains");Write("CilMixedPipeline.v",m.Verilog());tb.AppendLine("$display(\"PASS HDL CilMixedPipeline\");$finish;end endmodule");Write("CilMixedPipeline_tb.v",tb.ToString());benches++;
    }

    static void Main(string[] args)
    {
        directory=args.Length==0?"generated/behavioral":args[0];Directory.CreateDirectory(directory);
        foreach(var method in typeof(CSharpFunctions).GetMethods(BindingFlags.Public|BindingFlags.Static).Where(x=>!x.Name.StartsWith("Bad")&&x.Name!="ConstantInfinite"))Function(method,"Cs");
        var debug=Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory,"CSharpDebug.dll")).GetType("Behavioral.CSharpFunctions");
        foreach(var method in debug.GetMethods(BindingFlags.Public|BindingFlags.Static).Where(x=>!x.Name.StartsWith("Bad")&&x.Name!="ConstantInfinite"))Function(method,"Debug");
        foreach(var t in new[]{typeof(FSharpFunctions),typeof(VisualBasicFunctions)})
            foreach(var method in t.GetMethods(BindingFlags.Public|BindingFlags.Static))Function(method,t==typeof(FSharpFunctions)?"Fs":"Vb");
        Reject(typeof(CSharpFunctions).GetMethod("BadLoop"),"backward");
        Reject(typeof(CSharpFunctions).GetMethod("BadRecursion"),"Recursive");
        Reject(typeof(CSharpFunctions).GetMethod("BadExternal"),"same assembly");
        Reject(typeof(CSharpFunctions).GetMethod("BadArray"),"Unsupported");
        Reject(typeof(CSharpFunctions).GetMethod("BadHandler"),"exception handlers");
        Reject(typeof(CSharpFunctions).GetMethod("BadLong"),"port type");
        Reject(typeof(CSharpFunctions).GetMethod("ConstantInfinite"),"budget",new CilOptions{MaxInstructions=100});
        Counter();RamTest(13);RamTest(256);MixedPipeline();
        Write("verification.txt","PASS "+checks+" assertions; "+benches+" RTL benches generated. Native CLR methods are the arithmetic/exception oracle. Faulted result bits are intentionally unspecified.\n");
        Console.WriteLine("PASS "+checks+" assertions; "+benches+" RTL benches generated.");
    }
}
