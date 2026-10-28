using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Toolchains.NativeAot;
using SharpHDL.Benchmarking;

var selection=BaselineRunOptions.Parse(args);
args=selection.Arguments.ToArray();
if(selection.Qualified)
{
    var job=Job.ShortRun.WithLaunchCount(1).WithWarmupCount(2).WithIterationCount(3).WithGcServer(false);
    var config=ManualConfig.Create(DefaultConfig.Instance).AddExporter(JsonExporter.Full)
        .AddJob(job.WithRuntime(CoreRuntime.Core10_0).WithId("JIT10"));
    // Native AOT is deferred. Merely running the controlled profile must not
    // restore ILCompiler or compile native workers; an explicit opt-in is required.
    if(selection.IncludeNativeAot)
    {
        var aot=new NativeAotToolchainBuilder();aot.UseNuGet("10.0.13");aot.TargetFrameworkMoniker("net10.0");aot.RuntimeFrameworkVersion("10.0.13");
        config.AddJob(job.WithRuntime(NativeAotRuntime.Net10_0).WithToolchain(aot.ToToolchain()).WithId("AOT10-ILC10.0.13"));
    }
    config.Options|=ConfigOptions.DisableParallelBuild|ConfigOptions.LogBuildOutput;
    config.BuildTimeout=TimeSpan.FromMinutes(10);
    var summaries=BenchmarkSwitcher.FromAssembly(typeof(ScheduledSimulationBenchmarks).Assembly).Run(args,config);
    var reports=summaries.SelectMany(s=>s.Reports).ToArray();
    if(reports.Length!=selection.ExpectedMeasurementCount||reports.Any(r=>!r.Success))
        throw new InvalidOperationException($"Qualified baseline requires {selection.ExpectedMeasurementCount} successful measurements; inspect retained reports.");
}
else BenchmarkSwitcher.FromAssembly(typeof(ScheduledSimulationBenchmarks).Assembly).Run(args);
