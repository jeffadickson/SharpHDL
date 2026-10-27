using System.Numerics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using SharpHDL.Examples;
using SharpHDL.HLS;
using SharpHDL.Simulation;

BenchmarkSwitcher.FromAssembly(typeof(ScheduledSimulationBenchmarks).Assembly).Run(args);

/// <summary>Reproducible simulator baseline; no optimization/speedup claim.</summary>
[MemoryDiagnoser]
public class ScheduledSimulationBenchmarks
{
    private ScheduledHardwareDesign design=null!;
    /// <summary>Same transaction stream over small and larger RAM arrays.</summary>
    [Params(1,8)]
    public int AddressWidth { get; set; }
    /// <summary>Compile hardware outside the measured runtime operation.</summary>
    [GlobalSetup]
    public void Setup()=>design=ScheduledTable.Build(AddressWidth);
    /// <summary>Include machine construction, 256 clock edges and final independent result check.</summary>
    [Benchmark]
    public BigInteger ConstructAndSimulate256Edges()
    {
        var sim=new HardwareGraphMachine(design.Graph,design.Inputs,design.Outputs,design.NextState,design.ResetValues,design.Memories);
        sim.SetInput("request_valid",1);sim.SetInput("request_update",1);
        sim.SetInput("request_delta",1);sim.SetInput("response_ready",1);
        for(int i=0;i<256;i++)sim.Tick();
        var value=sim.ReadOutput("response_data");
        if(value!=64)throw new InvalidOperationException("Benchmark transaction checksum mismatch.");
        return value;
    }
}
