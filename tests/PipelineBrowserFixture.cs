using System;
using System.IO;
using System.Numerics;
using SharpHDL;

static class PipelineBrowserFixture
{
    public static void Run()
    {
        var m=new Module("BrowserPipeline");
        var input=HwStream<ushort>.Input(m,"input");
        var output=HwStream<ushort>.Output(m,"output");
        var data=((input.Data + Expr.Const(16,3)).Named("bias") * Expr.Const(16,5)).Named("gain");
        var handle=StructuralPipeline.Attach(m,"pipeline",input,output,data,
            new PipelineOptions { LogicLevelsPerStage=1 });
        var probe=new PipelineSignalProbe(handle,input.Valid,input.Ready,output.Valid,output.Ready);
        var recorder=new PipelineTraceRecorder(probe);
        var sim=m.Simulate();
        sim.Set(input.Valid,0); sim.Set(input.Data,0); sim.Set(output.Ready,0);
        sim.Tick(true);
        var accepted=0;
        for(int cycle=0;cycle<28;cycle++)
        {
            bool offer=cycle<15;
            sim.Set(input.Valid,offer?1:0);
            sim.Set(input.Data,(cycle*11)&65535);
            sim.Set(output.Ready,cycle>=8 && cycle%12<7?1:0);
            recorder.Capture(sim,cycle);
            if(offer && sim.Read(input.Ready)!=BigInteger.Zero)accepted++;
            sim.Tick();
        }
        Directory.CreateDirectory("docs/pipeline");
        File.WriteAllText("docs/pipeline/trace.json",recorder.ToJson(handle.DataflowGraph));
        Console.WriteLine("Exported 28 observed cycles, "+accepted+" accepted inputs to docs/pipeline/trace.json");
    }
}
