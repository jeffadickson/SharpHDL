using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using SharpHDL;

public static class AxisInteropTests
{
    static int checks;
    static void Assert(bool b) { checks++;if(!b)throw new Exception("AXI interop check failed"); }
    static void Reject(Action action) {
        checks++;try {action();}catch(ArgumentException){return;}catch(InvalidOperationException){return;}
        throw new Exception("Expected AXI interop rejection");
    }
    public static void Run() {
        checks=0;
        Reject(()=>new AxisFormat(7));Reject(()=>new AxisFormat(16,0));
        Reject(()=>new AxisPipeline("bad; rtl"));Reject(()=>new AxisPipeline("empty").Verilog());
        var f=new AxisFormat(16,8);
        Func<string,AxisComponent> ext=n=>AxisComponent.External(n,"external.v","s_axis","m_axis",f,f);
        Reject(()=>AxisComponent.External("x","","s","m",f,f));
        Reject(()=>AxisComponent.External("x","x.v","s","s",f,f));
        Reject(()=>ext("x").Parameter("GAIN",2).Parameter("GAIN",3));
        Reject(()=>ext("x").Parameter("a);",0));
        Reject(()=>new AxisPipeline("x").Add(ext("x")));
        Reject(()=>new AxisPipeline("top").Add(ext("x")).Add(AxisComponent.External("x","different.v","s_axis","m_axis",f,f)));
        Reject(()=>new AxisPipeline("top").Add(ext("x")).Add(AxisComponent.External("y","y.v","s_axis","m_axis",new AxisFormat(32,8),f)));
        Reject(()=>new AxisPipeline("top").Add(ext("x")).Add(AxisComponent.External("y","y.v","s_axis","m_axis",new AxisFormat(16,1),f)));
        var extra=new Module("Extra");var i=SharpHDL.Stream.Input(extra,"s",16,8);var o=SharpHDL.Stream.Output(extra,"m",16,8);
        SharpHDL.Stream.Connect(extra,i,o);extra.Input("extra",1);
        Reject(()=>AxisComponent.FromSharp(extra,"s","m"));
        var wrong=new Module("Wrong");SharpHDL.Stream.Input(wrong,"s",16,8);SharpHDL.Stream.Input(wrong,"m",16,8);
        Reject(()=>AxisComponent.FromSharp(wrong,"s","m"));
        bool consumed=false;
        using(var input=HwStream<AxisBeat>.FromEnumerable(Observe(()=>consumed=true)))
        using(var output=HwStream<AxisBeat>.ToAction(b=>{}))
            Reject(()=>new AxisPipeline("top").Add(ext("missing")).Run(input,output));
        Assert(!consumed);
        var identity=AxisComponent.External("id","id.v","s","m",f,f,b=>b);
        using(var input=HwStream<AxisBeat>.FromEnumerable(new[]{new AxisBeat(65536,3,false,0)}))
        using(var output=HwStream<AxisBeat>.ToAction(b=>{}))Reject(()=>new AxisPipeline("top").Add(identity).Run(input,output));
        var invalid=AxisComponent.External("invalid","x.v","s","m",f,f,b=>new AxisBeat(0,4,false,0));
        using(var input=HwStream<AxisBeat>.FromEnumerable(new[]{new AxisBeat(0,3,false,0)}))
        using(var output=HwStream<AxisBeat>.ToAction(b=>{}))Reject(()=>new AxisPipeline("top").Add(invalid).Run(input,output));
        int index=0;
        var beats=Enumerable.Range(0,65536).Select(x=>new AxisBeat(x,x%4,x%17==0,x%256));
        var pipeline=MixedScience.Build();
        using(var input=HwStream<AxisBeat>.FromEnumerable(beats))
        using(var output=HwStream<AxisBeat>.ToAction(b=>{
            long x=index>=32768?index-65536:index;
            long y=Math.Max(-32768,Math.Min(32767,2*x-10));
            Assert(b.Data==y*y);Assert(b.Keep==(index%4==3?15:0));
            Assert(b.Last==(index%17==0));Assert(b.User==index%256);index++;
        }))pipeline.Run(input,output);
        Assert(index==65536);
        bool propagated=false;
        using(var input=HwStream<AxisBeat>.FromEnumerable(new[]{new AxisBeat(1,3,false,0)}))
        using(var output=HwStream<AxisBeat>.ToAction(b=>{throw new IOException("consumer");}))
            try{pipeline.Run(input,output);}catch(IOException){propagated=true;}
        Assert(propagated);
        Assert(pipeline.ExternalSources.SequenceEqual(new[]{"examples/MixedAxis/calibrate.v"}));
        foreach(int width in new[]{8,16,24,32,64}) {
            var module=new Module("Energy"+width);
            var si=SharpHDL.Stream.Input(module,"s",width,1);
            var so=SharpHDL.Stream.Output(module,"m",2*width,1);
            SharpHDL.Stream.Connect(module,ScientificStreams.SampleEnergy(module,si,"square"),so);
            var sim=module.Simulate();sim.Tick(true);sim.Set(so.Ready,1);
            var limit=System.Numerics.BigInteger.One<<(width-1);
            foreach(var sample in new[]{-limit,-limit+1,-1,0,1,limit-1}) {
                sim.Set(si.Data,sample & Expr.Mask(width));sim.Set(si.Keep,Expr.Mask(width/8));
                sim.Set(si.Last,1);sim.Set(si.User,1);sim.Set(si.Valid,1);sim.Tick();
                Assert(sim.Read(so.Valid)==1);Assert(sim.Read(so.Data)==sample*sample);
                Assert(sim.Read(so.Keep)==Expr.Mask(width/4));Assert(sim.Read(so.Last)==1);Assert(sim.Read(so.User)==1);
                var native=ScientificStreams.SampleEnergy(new AxisBeat(sample & Expr.Mask(width),Expr.Mask(width/8),true,1),new AxisFormat(width));
                Assert(native.Data==sample*sample);
            }
        }
        var rtl=pipeline.Verilog();Assert(rtl.Contains("sensor_calibrate"));Assert(rtl.Contains("TKEEP: bit i"));
        Directory.CreateDirectory("generated/mixed-axis");
        File.WriteAllText("generated/mixed-axis/mixed_science.v",rtl);
        File.WriteAllLines("generated/mixed-axis/sources.f",new[]{"generated/mixed-axis/mixed_science.v"}.Concat(pipeline.ExternalSources));
        Console.WriteLine("PASS AXI interop: "+checks+" checks, all 65536 signed sample encodings in native mode.");
    }
    static IEnumerable<AxisBeat> Observe(Action called) {called();yield return new AxisBeat(0,3,false,0);}
}
