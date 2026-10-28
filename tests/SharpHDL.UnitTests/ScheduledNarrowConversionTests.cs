using System.Numerics;
using System.Reflection.Emit;
using SharpHDL.Examples;
using SharpHDL.HLS;
using SharpHDL.Simulation;
using SharpHDL.Verification;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>CLR, arithmetic-oracle and legacy/Core comparisons of narrow evaluation-stack values.</summary>
public sealed class ScheduledNarrowConversionTests
{
    private static readonly string[] Opcodes=["conv.i1","conv.u1","conv.i2","conv.u2"];
    private static readonly IReadOnlyDictionary<string,ScheduledHardwareDesign> Designs=Opcodes.ToDictionary(
        name=>name,name=>CilCompiler.BuildScheduledSequential(NarrowCilFixture.Method(name),"Narrow"+name.Replace('.','_')).Design);
    private static HardwareGraphMachine Machine(ScheduledHardwareDesign d)=>new(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);

    /// <summary>Sign thresholds, upper discarded bits, repeated calls and captured arguments agree edge by edge.</summary>
    [Theory][InlineData("conv.i1")][InlineData("conv.u1")][InlineData("conv.i2")][InlineData("conv.u2")]
    public void Boundary_conversions_match_CLR_arithmetic_and_legacy_edges(string opcode)
    {
        var method=NarrowCilFixture.Method(opcode);var native=method.CreateDelegate<Func<uint,uint>>();
        var d=Designs[opcode];var sim=Machine(d);var module=CilCompiler.BuildSequential(method,"LegacyNarrow");
        var old=module.Simulate();var ports=module.Ports.ToDictionary(p=>p.Name);old.Tick(true);
        foreach(uint value in new uint[]{0,1,0x7f,0x80,0xff,0x100,0x17f,0x180,0x7fff,0x8000,0xffff,0x10000,0x123480ff,0x7fffffff,0x80000000,0xffffffff})
        {
            sim.SetInput("arg0",value);sim.SetInput("start",1);old.Set(ports["arg0"],value);old.Set(ports["start"],1);
            int edges=0;
            do
            {
                sim.Tick();old.Tick();
                foreach(string output in new[]{"busy","done","fault","timeout","result","accept"})Assert.Equal(old.Read(ports[output]),sim.ReadOutput(output));
                sim.SetInput("start",0);sim.SetInput("arg0",~value);old.Set(ports["start"],0);old.Set(ports["arg0"],~value);
            }while(sim.ReadOutput("done")==0&&++edges<64);
            Assert.Equal(1,(int)sim.ReadOutput("done"));Assert.Equal(0,(int)sim.ReadOutput("fault"));
            Assert.Equal(NarrowCilFixture.Expected(value,opcode),native(value));Assert.Equal(native(value),(uint)sim.ReadOutput("result"));
        }
        int width=opcode.EndsWith('1')?8:16;
        Assert.Contains(d.Graph.Nodes,n=>n.Operation==Core.HardwareOperation.Resize&&n.Type.Width==width);
        if(opcode.Contains(".i"))Assert.Contains(d.Graph.Nodes,n=>n.Operation==Core.HardwareOperation.SignExtend&&n.Type.Width==32);
    }

    /// <summary>All four opcodes challenge full-width unsigned input patterns using reproducible generation.</summary>
    [FsCheck.Xunit.Property(MaxTest=100,Replay="1145655947,296144285")]
    public void Generated_conversions_preserve_the_Int32_stack_contract(uint value)
    {
        foreach(string opcode in Opcodes)
        {
            var sim=Machine(Designs[opcode]);sim.SetInput("arg0",value);sim.SetInput("start",1);sim.Tick();sim.SetInput("start",0);
            for(int edge=0;edge<64&&sim.ReadOutput("done")==0;edge++)sim.Tick();
            Assert.Equal(1,(int)sim.ReadOutput("done"));Assert.Equal(0,(int)sim.ReadOutput("fault"));
            Assert.Equal(NarrowCilFixture.Expected(value,opcode),(uint)sim.ReadOutput("result"));
        }
    }

    /// <summary>Ordinary C# composition retains the required conversions through actual compiled CIL.</summary>
    [Fact]
    public void Composed_CSharp_example_matches_its_native_result()
    {
        var d=ScheduledNarrowCil.Build();
        foreach(uint value in new uint[]{0,128,255,32768,65535,0x80000000,0xffffffff,0xaabbccdd})
        {
            var sim=Machine(d);sim.SetInput("arg0",value);sim.SetInput("start",1);sim.Tick();sim.SetInput("start",0);
            for(int edge=0;edge<128&&sim.ReadOutput("done")==0;edge++)sim.Tick();
            Assert.Equal(1,(int)sim.ReadOutput("done"));Assert.Equal(0,(int)sim.ReadOutput("fault"));
            Assert.Equal(ScheduledNarrowCil.Evaluate(value),(uint)sim.ReadOutput("result"));
        }
    }

    /// <summary>Extension is explicit, bounded and never silently truncates its source.</summary>
    [Fact]
    public void Sign_extension_width_and_identity_contracts_are_explicit()
    {
        var b=new ScheduledMachineBuilder("SignExtension");var x=b.Input("x",8);b.State("idle");
        Assert.Same(x,x.SignExtend(8));
        foreach(int invalid in new[]{-1,0,7,4097,int.MaxValue})Assert.Throws<ArgumentOutOfRangeException>(()=>x.SignExtend(invalid));
        b.Output("wide",x.SignExtend(4096));var sim=Machine(b.Lower());sim.SetInput("x",128);
        Assert.Equal((BigInteger.One<<4096)-128,sim.ReadOutput("wide"));sim.SetInput("x",127);Assert.Equal(127,sim.ReadOutput("wide"));
    }

    /// <summary>Adjacent checked/wide/floating instructions, narrow locals and malformed stacks still reject.</summary>
    [Fact]
    public void Unsupported_conversions_and_malformed_stacks_have_actionable_diagnostics()
    {
        foreach(var opcode in new[]{OpCodes.Conv_Ovf_I1,OpCodes.Conv_Ovf_U1,OpCodes.Conv_Ovf_I2,OpCodes.Conv_Ovf_U2,
            OpCodes.Conv_Ovf_I1_Un,OpCodes.Conv_Ovf_U1_Un,OpCodes.Conv_Ovf_I2_Un,OpCodes.Conv_Ovf_U2_Un,
            OpCodes.Conv_I8,OpCodes.Conv_U8,OpCodes.Conv_R4,OpCodes.Conv_I})
        {
            var error=Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(NarrowCilFixture.Create(opcode),"Rejected"));
            Assert.Contains(opcode.Name!,error.Message);Assert.Contains("IL_",error.Message);Assert.Contains("Convert",error.Message);
        }
        var locals=Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(NarrowCilFixture.Create(OpCodes.Conv_U1,narrowLocal:true),"NarrowLocal"));
        Assert.Contains("locals",locals.Message);
        var stack=Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(NarrowCilFixture.Create(OpCodes.Conv_I1,omitOperand:true),"BadStack"));
        Assert.Contains("stack",stack.Message);Assert.Contains("IL_",stack.Message);
    }
}
