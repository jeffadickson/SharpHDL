using SharpHDL;
using SharpHDL.HLS;

// Production C# expression -> canonical Core IR -> independent HLS compiler
// -> registered Core IR -> compatibility RTL backend -> Verilator.
var root=Path.GetFullPath(args.Length>0?args[0]:Directory.GetCurrentDirectory());
var module=new Module("CoreHlsSource");
var input=module.Input("input",16);
var output=module.Output("output",16);
module.Drive(output,((input+Expr.Const(16,11))*input)+Expr.Const(16,9));
var core=module.ExportCoreGraph();
var pipeline=HlsElasticCompiler.Compile(core.Graph,core.Inputs["input"],
    core.Outputs["output"],2,new HlsCostProfile{
        MultiplyCost=2,ArithmeticCost=1}, "hls");
var rtl=pipeline.ToModule("CoreHlsElastic");
var dir=Path.Combine(root,"generated/core-hls");
Directory.CreateDirectory(dir);
File.WriteAllText(Path.Combine(dir,"CoreHlsElastic.v"),rtl.Verilog());
Console.WriteLine($"Generated Core IR elastic pipeline: latency={pipeline.Latency}, "+
    $"II={pipeline.InitiationInterval}, payloadBits={pipeline.PayloadRegisterBits}");
