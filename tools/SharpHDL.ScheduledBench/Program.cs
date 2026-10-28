using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Toolchains.NativeAot;

bool qualified=args.Contains("--qualified-baseline",StringComparer.Ordinal);
if(qualified)
{
    args=args.Where(a=>a!="--qualified-baseline").ToArray();
    var aot=new NativeAotToolchainBuilder();aot.UseNuGet("10.0.13");aot.TargetFrameworkMoniker("net10.0");aot.RuntimeFrameworkVersion("10.0.13");
    var job=Job.ShortRun.WithLaunchCount(1).WithWarmupCount(2).WithIterationCount(3).WithGcServer(false);
    var config=ManualConfig.Create(DefaultConfig.Instance).AddExporter(JsonExporter.Full)
        .AddJob(job.WithRuntime(CoreRuntime.Core10_0).WithId("JIT10"))
        .AddJob(job.WithRuntime(NativeAotRuntime.Net10_0).WithToolchain(aot.ToToolchain()).WithId("AOT10-ILC10.0.13"));
    // Serialize generated builds on shared hosts; keep build failures visible in retained logs.
    config.Options|=ConfigOptions.DisableParallelBuild|ConfigOptions.LogBuildOutput;
    config.BuildTimeout=TimeSpan.FromMinutes(10);
    var summaries=BenchmarkSwitcher.FromAssembly(typeof(ScheduledSimulationBenchmarks).Assembly).Run(args,config);
    var reports=summaries.SelectMany(s=>s.Reports).ToArray();
    if(reports.Length!=8||reports.Any(r=>!r.Success))throw new InvalidOperationException("Qualified baseline requires eight successful JIT/AOT measurements; inspect retained reports.");
}
else BenchmarkSwitcher.FromAssembly(typeof(ScheduledSimulationBenchmarks).Assembly).Run(args);

