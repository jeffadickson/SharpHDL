using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using SharpHDL;
static class MemoryTargetTests
{
    static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception("Memory target: "+message);}
    public static void Run()
    {
        Directory.CreateDirectory("generated/memory-targets");
        Test(new MemoryOptions(),13);
        foreach(var target in new[]{MemoryTarget.Xilinx7Series,MemoryTarget.IntelCycloneV,MemoryTarget.IntelArria10})
        {
            foreach(var storage in new[]{MemoryStorage.Auto,MemoryStorage.Block,MemoryStorage.Registers})Test(new MemoryOptions(target,storage),storage==MemoryStorage.Block?256:13);
            Sequential(target);
        }
        Test(new MemoryOptions(MemoryTarget.Xilinx7Series,MemoryStorage.Distributed),13);
        foreach(var f in new Action[]{()=>new MemoryOptions((MemoryTarget)99),()=>new MemoryOptions(MemoryTarget.Generic,MemoryStorage.Block),()=>new MemoryOptions(MemoryTarget.IntelCycloneV,MemoryStorage.Distributed),()=>new MemoryOptions(MemoryTarget.IntelArria10,MemoryStorage.Distributed)})
        {bool bad=false;try{f();}catch(ArgumentException){bad=true;}catch(NotSupportedException){bad=true;}Check(bad,"invalid policy rejected");}
        Console.WriteLine("PASS "+checks+" memory target checks; 11 independent-oracle RAM RTL benches and 3 sequential array targets");
    }
    static void Test(MemoryOptions options,int depth)
    {
        string name="Ram"+options.Target+options.Storage;
        var m=new Module(name,options);int aw=depth==256?8:4;
        var addr=m.Input("address",aw);var wd=m.Input("wd",16);var we=m.Input("we",1);var re=m.Input("re",1);
        var seed=Enumerable.Range(0,depth).Select(i=>(BigInteger)(i*17)).ToArray();var model=seed.Select(x=>(uint)x).ToArray();
        var ram=SynchronousRam.Create(m,"storage",16,depth,addr,we,wd,re,seed);
        // Mutation of caller-owned initialization cannot change this memory.
        seed[0]=999;Check(ram.Options==options,"module default policy captured");
        var y=m.Output("y",16);m.Drive(y,ram.ReadData);var sim=m.Simulate();sim.Tick(true);uint expected=0;
        var tb=new StringBuilder("module tb;reg clk=0,rst=1;reg ["+(aw-1)+":0] a=0;reg [15:0] d=0;reg we=0,re=0;wire [15:0] y;h_"+name+" dut(.clk(clk),.rst(rst),.s_address(a),.s_wd(d),.s_we(we),.s_re(re),.s_y(y));initial begin #1;clk=1;#1;clk=0;rst=0;\n");
        var rng=new Random(2026);int collisions=0,invalid=0;
        for(int cycle=0;cycle<700;cycle++)
        {
            int a=cycle<depth?cycle:rng.Next(1<<aw);uint d=(uint)rng.Next(65536);
            bool read=cycle%5!=0,write=cycle>=depth&&cycle%3!=0,reset=cycle==depth+45;
            sim.Set(addr,a);sim.Set(wd,d);sim.Set(we,write?1:0);sim.Set(re,read?1:0);
            Check(sim.Read(y)==expected,"pre-edge hold");
            expected=reset?0:read?(a<depth?model[a]:0):expected;
            if(!reset&&write&&a<depth)model[a]=d;
            if(read&&write&&!reset)collisions++;if(a>=depth)invalid++;
            sim.Tick(reset);Check(sim.Read(y)==expected,"old-data, bounds, reset retention, enables");
            tb.AppendLine("a="+a+";d="+d+";we="+(write?1:0)+";re="+(read?1:0)+";rst="+(reset?1:0)+";#1;clk=1;#1;if(y!==16'd"+expected+")$fatal(1,\"RAM cycle "+cycle+"\");clk=0;");
        }
        Check(collisions>0&&(depth==256||invalid>0),"coverage");
        string rtl=m.Verilog();
        Check(!rtl.Contains("no_rw_check\""),"no collision relaxation");
        if(options.Storage!=MemoryStorage.Auto)Check(rtl.Contains(options.Attribute),"target attribute");
        tb.AppendLine("$display(\"PASS memory "+name+"\");$finish;end endmodule");
        File.WriteAllText("generated/memory-targets/"+name+".v",rtl);File.WriteAllText("generated/memory-targets/"+name+"_tb.v",tb.ToString());
        // Explicit per-memory override takes precedence over module policy.
        var over=new Module("Override",options);var a0=over.Input("a",1);
        var overridden=SynchronousRam.Create(over,"ram",1,2,a0,Expr.Const(1,0),Expr.Const(1,0),Expr.Const(1,1),options:new MemoryOptions());
        Check(overridden.Options.Target==MemoryTarget.Generic,"per-memory override");
    }
    static void Sequential(MemoryTarget target)
    {
        var m=CilCompiler.BuildSequential(typeof(NativeMemory).GetMethod("Bump"),"Array"+target,
            new SequentialOptions{MemoryDepth=16,MemoryOptions=new MemoryOptions(target,MemoryStorage.Block)});
        var tb=new StringBuilder("module tb;reg clk=0,rst=1,start=0;reg [31:0] arg1=3,arg2=0;wire done,fault;wire [31:0] result;integer cycles;h_Array"+target+" dut(.clk(clk),.rst(rst),.s_start(start),.s_arg1(arg1),.s_arg2(arg2),.s_done(done),.s_fault(fault),.s_result(result));initial begin #1;clk=1;#1;clk=0;rst=0;\n");
        var s=m.Simulate();var ports=m.Ports.ToDictionary(p=>p.Name);var native=new uint[16];s.Tick(true);
        foreach(uint delta in new uint[]{7,4,1})
        {
            if(delta==1)s.Tick(true); // Native cells, like hardware cells, survive reset.
            uint expected=NativeMemory.Bump(native,3,delta);
            if(delta==1)tb.AppendLine("rst=1;#1;clk=1;#1;clk=0;rst=0;");
            tb.AppendLine("arg2="+delta+";start=1;#1;clk=1;#1;clk=0;start=0;cycles=0;while(!done&&cycles<200)begin #1;clk=1;#1;clk=0;cycles=cycles+1;end if(!done||fault||result!==32'd"+expected+")$fatal(1,\"Sequential native memory mismatch\");");
            s.Set(ports["arg1"],3);s.Set(ports["arg2"],delta);s.Set(ports["start"],1);s.Tick();s.Set(ports["start"],0);
            int cycles=0;while(s.Read(ports["done"])==0&&cycles++<200)s.Tick();
            Check(cycles<200&&s.Read(ports["fault"])==0&&s.Read(ports["result"])==expected,"native array/sequential RAM agreement");
        }
        File.WriteAllText("generated/memory-targets/Array"+target+".v",m.Verilog());
        tb.AppendLine("$display(\"PASS sequential memory "+target+"\");$finish;end endmodule");
        File.WriteAllText("generated/memory-targets/Array"+target+"_tb.v",tb.ToString());
    }
}
