using SharpHDL.Core;
using SharpHDL.Examples;
using SharpHDL.Simulation;
using SharpHDL.Verification;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Independent reset/collision oracle before replay through external HDL tools.</summary>
public sealed class MemoryCollisionQualificationTests
{
    [Theory][InlineData(HardwareMemoryImplementation.Auto)][InlineData(HardwareMemoryImplementation.XilinxBlock)]
    public void Two_memories_match_the_independent_array_reference(HardwareMemoryImplementation implementation)
    {
        var d=new MemoryCollisionExample(implementation);var machine=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        Assert.Equal(99,MemoryCollisionCampaign.Run((n,v)=>machine.SetInput(n,v),reset=>{if(reset)machine.Reset();else machine.Tick();},n=>(int)machine.ReadOutput(n)));
    }
}
