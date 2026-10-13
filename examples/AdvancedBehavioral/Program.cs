using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using SharpHDL;
using Module=SharpHDL.Module;
class Program
{
    static int checks,benches;static readonly string dir="generated/advanced";
    static void Check(bool b,string why){checks++;if(!b)throw new Exception(why);}
    static BigInteger Bits(object x){if(x is uint)return (uint)x;if(x is int)return (uint)(int)x;if(x is bool)return (bool)x?1:0;return (uint)x.GetType().GetMethod("op_Explicit").Invoke(null,new[]{x});}
    static object Scalar(BigInteger value,Type t)
    {if(t==typeof(int))return unchecked((int)(uint)value);if(t==typeof(uint))return (uint)value;if(t==typeof(bool))return value!=0;return t.GetMethod("op_Implicit").Invoke(null,new object[]{(uint)value});}
    sealed class Trace
    {
        public Module M;public Simulator Sim;public StringBuilder Text=new StringBuilder();public Signal[] Inputs,Outputs;
        public Trace(Module m)
        {
            M=m;Inputs=m.Ports.Where(x=>x.IsInput).ToArray();Outputs=m.Ports.Where(x=>x.IsOutput).ToArray();Sim=m.Simulate();
            Text.AppendLine("`timescale 1ns/1ps\nmodule tb;reg clk=0,rst=0;");foreach(var p in Inputs)Text.AppendLine("reg ["+(p.Width-1)+":0] s_"+p.Name+"=0;");foreach(var p in Outputs)Text.AppendLine("wire ["+(p.Width-1)+":0] s_"+p.Name+";");
            Text.Append("h_"+m.Name+" dut(.clk(clk),.rst(rst)");foreach(var p in Inputs.Concat(Outputs))Text.Append(",.s_"+p.Name+"(s_"+p.Name+")");Text.AppendLine(");initial begin");Tick(true);
        }
        public Signal P(string n){return M.Ports.Single(p=>p.Name==n);}
        public void Set(string n,BigInteger v){var p=P(n);v&=Expr.Mask(p.Width);Sim.Set(p,v);Text.AppendLine("s_"+n+"="+p.Width+"'d"+v+";");}
        public BigInteger Read(string n){return Sim.Read(P(n));}
        public void Compare(){foreach(var p in Outputs)Text.AppendLine("if(s_"+p.Name+"!=="+p.Width+"'d"+Sim.Read(p)+")$fatal(1,\""+M.Name+" "+p.Name+"\");");}
        public void Settle(){Text.AppendLine("#1;");Compare();}
        public void Tick(bool reset=false){Sim.Tick(reset);Text.AppendLine("rst="+(reset?1:0)+";#1;clk=1;#1;");Compare();Text.AppendLine("clk=0;rst=0;");}
        public void Save(){File.WriteAllText(Path.Combine(dir,M.Name+".v"),M.Verilog());File.WriteAllText(Path.Combine(dir,M.Name+".graph.txt"),M.DumpGraph());Text.AppendLine("$display(\"PASS HDL "+M.Name+"\");$finish;end endmodule");File.WriteAllText(Path.Combine(dir,M.Name+"_tb.v"),Text.ToString());benches++;}
    }
    static void Comb(Type type,string methodName,string tag)
    {
        var method=type.GetMethod(methodName);var t=new Trace(CilCompiler.BuildModule(method,tag+methodName));var rng=new Random(13);
        for(int i=0;i<160;i++)
        {
            object[] args;
            if(methodName=="TypedAccumulate")
            {uint a=(uint)(i<16?4095-i:rng.Next(4096)),b=(uint)rng.Next(4096);t.Set("arg0",a);t.Set("arg1",b);args=new[]{Scalar(a,method.GetParameters()[0].ParameterType),Scalar(b,method.GetParameters()[1].ParameterType)};}
            else if(methodName=="Packet")
            {uint length=(uint)rng.Next(4096),kind=(uint)rng.Next(256),urgent=(uint)(i&1),limit=(uint)rng.Next(4096);var header=Activator.CreateInstance(method.GetParameters()[0].ParameterType);var fs=header.GetType().GetFields();fs.Single(f=>f.Name=="Length").SetValue(header,Scalar(length,typeof(UBits<W12>)));fs.Single(f=>f.Name=="Kind").SetValue(header,(byte)kind);fs.Single(f=>f.Name=="Urgent").SetValue(header,urgent!=0);args=new object[]{header,limit};t.Set("arg0",length|(kind<<12)|(urgent<<20));t.Set("arg1",limit);}
            else {int a=i-80;bool check=i%3!=0;t.Set("arg0",(uint)a);t.Set("arg1",check?1:0);args=new object[]{a,check};}
            bool failed=false;BigInteger expected=0;try{expected=Bits(method.Invoke(null,args));}catch(TargetInvocationException e){if(!(e.InnerException is HardwareAssertionException))throw;failed=true;}
            Check(t.Read("fault")== (failed?1:0),"comb fault");Check(t.Read("assertion_failed")== (failed?1:0),"comb assertion");if(!failed)Check(t.Read("result")==expected,"comb result "+methodName);t.Settle();
        }
        t.Save();File.WriteAllText(Path.Combine(dir,t.M.Name+".cil.txt"),CilCompiler.Disassemble(method));
    }
    static void Seq(Type type,string methodName,string tag)
    {
        var method=type.GetMethod(methodName);var ps=method.GetParameters();bool ram=ps[0].ParameterType==typeof(uint[]);int max=methodName=="Infinite"?60:2500;
        var memory=Enumerable.Range(0,16).Select(i=>(uint)(i+1)).ToArray();var options=new SequentialOptions{MaxCycles=max,MemoryDepth=16,InitialMemory=memory.Select(x=>(BigInteger)x).ToArray()};
        var t=new Trace(CilCompiler.BuildSequential(method,tag+"Seq"+methodName,options));var rng=new Random(998);int count=methodName=="Infinite"?3:24;
        for(int test=0;test<count;test++)
        {
            var values=new object[ps.Length];for(int p=0;p<ps.Length;p++)
            {
                if(ps[p].ParameterType==typeof(uint[])){values[p]=memory;continue;}
                int v;
                if(methodName=="WriteThenAssert")v=p==1?0:(test%2==0?20+test:1);
                else if(methodName=="Checked")v=test%2==0?int.MaxValue:2;
                else if(methodName=="Divide"||methodName=="Remainder")v=p==0?(test%3==0?int.MinValue:17):(test%3==0?-1:test%3==1?0:3);
                else if(ram)v=p==1?(test<4?new[]{-1,0,16,17}[test]:rng.Next(16)):test+1;
                else v=p==0?test%9-1:3;
                BigInteger raw=(BigInteger)unchecked((uint)v);values[p]=Scalar(raw,ps[p].ParameterType);t.Set("arg"+p,raw);
            }
            bool failed=false,assertion=false,timed=methodName=="Infinite";BigInteger expected=0;
            if(!timed)try{expected=Bits(method.Invoke(null,values));}catch(TargetInvocationException e){failed=true;assertion=e.InnerException is HardwareAssertionException;if(!assertion&&!(e.InnerException is OverflowException)&&!(e.InnerException is DivideByZeroException)&&!(e.InnerException is IndexOutOfRangeException))throw;}
            t.Set("start",1);Check(t.Read("accept")==1,"accept idle");t.Tick();t.Set("start",0);Check(t.Read("busy")==1&&t.Read("done")==0,"launch");
            // Change inputs and pulse start while busy: operands must remain latched.
            for(int p=0;p<ps.Length;p++)if(ps[p].ParameterType!=typeof(uint[]))t.Set("arg"+p,123);
            int cycles=0;while(t.Read("busy")!=0&&cycles<max+3){t.Set("start",cycles==1?1:0);t.Tick();cycles++;}
            t.Set("start",0);Check(t.Read("busy")==0&&t.Read("done")==1,"completion "+methodName);
            Check(t.Read("timeout")== (timed?1:0),"timeout "+methodName);Check(t.Read("fault")== (failed||timed?1:0),"fault "+methodName);Check(t.Read("assertion_failed")== (assertion?1:0),"assertion "+methodName);
            if(!failed&&!timed)Check(t.Read("result")==expected,"sequential result "+methodName+" "+test+" got "+t.Read("result")+" expected "+expected);
            var result=t.Read("result");t.Tick();Check(t.Read("done")==1&&t.Read("result")==result,"done held");
            if(test==0){t.Set("start",1);t.Tick();t.Set("start",0);t.Tick(true);Check(t.Read("busy")==0&&t.Read("done")==0&&t.Read("fault")==0,"reset abort");}
        }
        t.Save();File.WriteAllText(Path.Combine(dir,t.M.Name+".cil.txt"),CilCompiler.Disassemble(method));
    }
    static void Main()
    {
        Directory.CreateDirectory(dir);var debug=Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory,"AdvancedDebug.dll")).GetType("AdvancedFunctions");
        foreach(var type in new[]{typeof(AdvancedFunctions),debug})
        {string tag=type==debug?"Debug":"Cs";foreach(var method in new[]{"TypedAccumulate","Packet","AssertBranch"})Comb(type,method,tag);
         foreach(var method in new[]{"RuntimeFor","RuntimeWhile","RuntimeDo","Nested","Choice","Checked","Divide","Remainder","AssertLoop","Infinite","Memory","ReadWord","WriteThenRead","WriteThenAssert","TypedAccumulate"})Seq(type,method,tag);}
        try{CilCompiler.BuildSequential(typeof(AdvancedFunctions).GetMethod("BadCall"),"Bad");throw new Exception("Accepted bad call");}catch(NotSupportedException){checks++;}
        File.WriteAllText(Path.Combine(dir,"verification.txt"),"PASS "+checks+" independent assertions; "+benches+" generated RTL benches.\n");Console.WriteLine("PASS "+checks+" assertions; "+benches+" benches");
    }
}
