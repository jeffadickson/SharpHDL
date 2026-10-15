using System;
using System.IO;
using System.Reflection;
using SharpHDL;

/// <summary>Generates comparable 32-bit multiplier pipelines for RTL verification and synthesis.</summary>
public static class Program
{
    [HardwareProcess]
    public static void Kernel(HwStream<uint> input,HwStream<uint> output)
    {
        while(true)
        {
            uint x=input.Read();
            output.Write(x*(x+3u));
        }
    }

    public static void Main(string[] args)
    {
        var root=Path.GetFullPath(args.Length>0?args[0]:Directory.GetCurrentDirectory());
        var dir=Path.Combine(root,"generated/multipliers");
        Directory.CreateDirectory(dir);
        var method=typeof(Program).GetMethod(nameof(Kernel))!;
        foreach(var architecture in new []{
            MultiplierImplementation.Operator,
            MultiplierImplementation.ShiftAdd,
            MultiplierImplementation.BoothRadix4,
            MultiplierImplementation.WallaceTree,
            MultiplierImplementation.DspPreferred})
        {
            var module=new Module("Mul"+architecture);
            var input=HwStream<uint>.Input(module,"in");
            var output=HwStream<uint>.Output(module,"out");
            var schedule=CilCompiler.AttachPipeline(module,"kernel",method,input,output,
                new PipelineOptions { MultiplierImplementation=architecture,
                    LogicLevelsPerStage=8,MaxStages=256 });
            var file=Path.Combine(dir,"Mul"+architecture+".v");
            File.WriteAllText(file,module.Verilog());
            File.WriteAllText(Path.ChangeExtension(file,"schedule.txt"),schedule.Report);
            Console.WriteLine(architecture+": stages="+schedule.Latency+
                " registerBits="+schedule.PayloadRegisterBits+
                " II="+schedule.InitiationInterval);
        }
    }
}
