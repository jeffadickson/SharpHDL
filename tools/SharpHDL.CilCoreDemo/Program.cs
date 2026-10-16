using SharpHDL;
using SharpHDL.RTL;

/// <summary>End-to-end real CIL Read/Write frontend through Core HLS and both RTL emitters.</summary>
public static class Program
{
    [HardwareProcess]
    public static void Transform(HwStream<uint> input,HwStream<uint> output)
    {
        while(true)
        {
            uint x=input.Read();
            output.Write((x+11u)*x+9u);
        }
    }

    public static void Main(string[] args)
    {
        var root=Path.GetFullPath(args.Length>0?args[0]:Directory.GetCurrentDirectory());
        var dir=Path.Combine(root,"generated/cil-core");
        Directory.CreateDirectory(dir);
        var module=new Module("CilCoreDemo");
        var input=HwStream<uint>.Input(module,"in");
        var output=HwStream<uint>.Output(module,"out");
        var method=typeof(Program).GetMethod(nameof(Transform))!;
        var pipeline=CilCompiler.AttachPipeline(module,"core",method,input,output,
            new PipelineOptions{UseCoreCompiler=true,LogicLevelsPerStage=1,MaxStages=64});
        File.WriteAllText(Path.Combine(dir,"CilCoreLegacy.v"),module.Verilog());
        var core=module.ExportCoreGraph();
        File.WriteAllText(Path.Combine(dir,"CilCoreNative.v"),
            CoreVerilogEmitter.Emit(core.Graph,core.Inputs,core.Outputs,
                core.NextState,core.ResetValues,core.Memories,"CilCoreNative"));
        File.WriteAllText(Path.Combine(dir,"schedule.txt"),pipeline.Report);
        Console.WriteLine("PASS CIL->Core HLS: latency="+pipeline.Latency+
            " II="+pipeline.InitiationInterval+" payloadBits="+pipeline.PayloadRegisterBits);
    }
}
