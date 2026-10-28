using System.Numerics;
using BenchmarkDotNet.Attributes;
using SharpHDL.Examples;
using SharpHDL.HLS;
using SharpHDL.Simulation;
using SharpHDL.Verification;

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

/// <summary>Same fixed transaction stream and independent checksum for both existing observation APIs.</summary>
[MemoryDiagnoser]
public class ObservedSimulationBenchmarks
{
    private ScheduledHardwareDesign design=null!;
    /// <summary>One small RAM and one 256-word RAM, with identical address-zero stimulus.</summary>
    [Params(1,8)]public int AddressWidth {get;set;}
    /// <summary>Exclude hardware compilation; verify both paths before timing.</summary>
    [GlobalSetup]
    public void Setup()
    {
        design=ScheduledTable.Build(AddressWidth);
        if(SimulatorObservationWorkload.Run(design,false)!=SimulatorObservationWorkload.Run(design,true))throw new InvalidOperationException("Unequal observation paths.");
    }
    /// <summary>Construction, 256 edges and three independent output evaluations per edge.</summary>
    [Benchmark(Baseline=true)]public ulong RepeatedOutputReads()=>SimulatorObservationWorkload.Run(design,false);
    /// <summary>Construction, 256 edges and one immutable output snapshot per edge.</summary>
    [Benchmark]public ulong SnapshotOutputReads()=>SimulatorObservationWorkload.Run(design,true);
}
