using System.Numerics;
using SharpHDL.Core;
using SharpHDL.Examples;
using SharpHDL.HLS;
using SharpHDL.RTL;
using SharpHDL.Simulation;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Explicit bank concurrency, contents, bounds and inference-policy checks.</summary>
public sealed class ScheduledBankedMemoryTests
{
    /// <summary>Existing compiled callers retain their original memory declaration signatures.</summary>
    [Fact]
    public void Original_memory_api_signatures_remain_available()
    {
        Assert.NotNull(typeof(ScheduledMachineBuilder).GetMethod(nameof(ScheduledMachineBuilder.Memory),new[]{typeof(string),typeof(int),typeof(int)}));
        Assert.NotNull(typeof(HardwareMemoryPort).GetConstructor(new[]{typeof(string),typeof(int),typeof(int),typeof(int),typeof(int),typeof(IEnumerable<BigInteger>)}));
    }
    /// <summary>Distinct banks permit simultaneous accesses without aliasing or extra state.</summary>
    [Fact]
    public void Independent_banks_roundtrip_boundary_addresses()
    {
        var d=ScheduledBankedTable.Build(3);var sim=Machine(d);
        foreach(int address in new[]{0,2,1,3,0})
        {
            sim.SetInput("address",address);sim.SetInput("left",65535-address);sim.SetInput("right",address+7);
            sim.Tick();sim.Tick();
            Assert.Equal(address<3?65535-address:0,(int)sim.ReadOutput("out_left"));
            Assert.Equal(address<3?address+7:0,(int)sim.ReadOutput("out_right"));
        }
        Assert.Equal(2,d.Memories.Count);
    }

    /// <summary>Linear initialization maps cyclically and remains immutable/reset-retained.</summary>
    [Fact]
    public void Cyclic_initialization_mapping_and_failed_declarations_are_exact()
    {
        var b=new ScheduledMachineBuilder("Initial");BigInteger[] data={10,11,20,21,30,31};
        var banks=b.BankedMemory("banks",8,2,3,data);data[0]=255;
        for(int word=0;word<6;word++)
        {
            Assert.Equal(word%2,banks.BankForWord(word));Assert.Equal(word/2,banks.RowForWord(word));
            Assert.Equal((word/2+1)*10+word%2,(int)banks.Banks[word%2].Initial[word/2]);
        }
        Assert.Throws<ArgumentOutOfRangeException>(()=>banks.BankForWord(6));
        Assert.Throws<ArgumentOutOfRangeException>(()=>banks.RowForWord(-1));
        Assert.Throws<ArgumentException>(()=>b.Memory("retry",8,2,new BigInteger[]{1}));
        Assert.Throws<ArgumentException>(()=>b.Memory("retry",8,2,new BigInteger[]{1,256}));
        b.Memory("retry",8,2);
        b.Input("collision_bank1",1);
        Assert.Throws<ArgumentException>(()=>b.BankedMemory("collision",8,2,2));
        b.Memory("collision_bank0",8,2); // Preflight failure allocated no earlier banks.
        var target=b.Register("word",8);b.State("read").Read(banks.Banks[0],b.Constant(2,0),target,b.Constant(1,1));
        b.Output("result",target.Value);var sim=Machine(b.Lower());
        sim.Tick();Assert.Equal(10,(int)sim.ReadOutput("result"));sim.Reset();sim.Tick();Assert.Equal(10,(int)sim.ReadOutput("result"));
    }

    /// <summary>Inference hints survive Core metadata and native RTL emission without changing simulation.</summary>
    [Theory]
    [InlineData(HardwareMemoryImplementation.XilinxBlock,"ram_style = \"block\"")]
    [InlineData(HardwareMemoryImplementation.XilinxDistributed,"ram_style = \"distributed\"")]
    [InlineData(HardwareMemoryImplementation.IntelM10K,"ramstyle = \"M10K\"")]
    [InlineData(HardwareMemoryImplementation.IntelM20K,"ramstyle = \"M20K\"")]
    public void Explicit_hints_are_preserved(HardwareMemoryImplementation implementation,string attribute)
    {
        var d=ScheduledBankedTable.Build(256,implementation);
        Assert.All(d.Memories.Values,m=>Assert.Equal(implementation,m.Implementation));
        Assert.Contains(attribute,CoreVerilogEmitter.Emit(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories,d.Name));
        var sim=Machine(d);sim.SetInput("left",17);sim.SetInput("right",42);sim.Tick();sim.Tick();
        Assert.Equal(17,(int)sim.ReadOutput("out_left"));Assert.Equal(42,(int)sim.ReadOutput("out_right"));
    }

    private static HardwareGraphMachine Machine(ScheduledHardwareDesign d)
        =>new(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
}
