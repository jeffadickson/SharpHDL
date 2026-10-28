using System.Reflection;
using System.Reflection.Emit;
using SharpHDL.HLS;
using SharpHDL.Verification;
using SharpHDL.Simulation;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Exercise storage boundaries separately from explicit conversion instructions.</summary>
public sealed class ScheduledScalarStorageTests
{
    private static readonly Type[] Types=[typeof(sbyte),typeof(byte),typeof(short),typeof(ushort),typeof(char)];

    private static uint Expected(uint value,Type type)
    {
        int width=type==typeof(byte)||type==typeof(sbyte)?8:16;
        uint modulus=1u<<width,low=value%modulus;
        return (type==typeof(sbyte)||type==typeof(short))&&low>=modulus/2?unchecked(low-modulus):low;
    }

    private static uint Run(ScheduledHardwareDesign d,uint value,bool argument)
    {
        var sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        sim.SetInput("arg0",argument?0:value);if(argument)sim.SetInput("arg1",value);
        sim.SetInput("start",1);sim.Tick();sim.SetInput("start",0);
        sim.SetInput("arg0",0);if(argument)sim.SetInput("arg1",~value);
        for(int edge=0;edge<32&&sim.ReadOutput("done")==0;edge++)sim.Tick();
        Assert.Equal(1,(int)sim.ReadOutput("done"));Assert.Equal(0,(int)sim.ReadOutput("fault"));
        return (uint)sim.ReadOutput("result");
    }

    /// <summary>Raw CLI store/load operations must truncate and extend even without conv opcodes.</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void Storage_matches_actual_CLR_and_independent_modular_arithmetic(bool argument)
    {
        foreach(var type in Types)
        {
            var method=NarrowCilFixture.Storage(type,argument);var d=CilCompiler.BuildScheduledSequential(method,"Storage").Design;
            if(argument)Assert.Equal(type==typeof(byte)||type==typeof(sbyte)?8:16,d.Graph.Nodes.Single(n=>n.Id==d.Inputs["arg0"]).Type.Width);
            foreach(uint value in new uint[]{0,1,127,128,255,256,32767,32768,65535,65536,0x80000000,uint.MaxValue})
            {
                object?[] args=argument?[Activator.CreateInstance(type),value]:[value];
                Assert.Equal(Expected(value,type),(uint)method.Invoke(null,args)!);
                Assert.Equal(Expected(value,type),Run(d,value,argument));
            }
        }
    }

    /// <summary>Declared return widths expose the stored bits, including the sign bit of signed types.</summary>
    [Fact]
    public void Narrow_returns_have_exact_port_widths()
    {
        foreach(var type in Types)
        foreach(bool direct in new[]{false,true})
        {
            var method=NarrowCilFixture.Storage(type,result:true,readOnly:direct);
            var d=CilCompiler.BuildScheduledSequential(method,"Return").Design;
            int width=type==typeof(byte)||type==typeof(sbyte)?8:16;
            Assert.Equal(width,d.Graph.Nodes.Single(n=>n.Id==d.Outputs["result"]).Type.Width);
            Assert.Equal((1u<<width)-1,unchecked((uint)Convert.ToInt32(method.Invoke(null,[uint.MaxValue])))&((1u<<width)-1));
            Assert.Equal((1u<<width)-1,Run(d,uint.MaxValue,false));
        }
    }

    /// <summary>Signed input ports extend their top bit; unsigned ports preserve positive values.</summary>
    [Fact]
    public void Captured_input_ports_extend_from_the_declared_width()
    {
        foreach(var type in Types)
        {
            var method=NarrowCilFixture.Storage(type,argument:true,readOnly:true);
            var d=CilCompiler.BuildScheduledSequential(method,"Input").Design;
            var sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
            uint bits=type==typeof(byte)||type==typeof(sbyte)?255u:65535u;
            sim.SetInput("arg0",bits);sim.SetInput("arg1",0);sim.SetInput("start",1);sim.Tick();
            sim.SetInput("start",0);sim.SetInput("arg0",0);
            for(int edge=0;edge<32&&sim.ReadOutput("done")==0;edge++)sim.Tick();
            Assert.Equal(1,(int)sim.ReadOutput("done"));Assert.Equal(Expected(bits,type),(uint)sim.ReadOutput("result"));
            sim.Reset();Assert.Equal(0,(int)sim.ReadOutput("done"));Assert.Equal(0,(int)sim.ReadOutput("result"));
        }
    }

    /// <summary>Full-width patterns challenge implicit truncation with a reproducible FsCheck seed.</summary>
    [FsCheck.Xunit.Property(MaxTest=100,Replay="1145655947,296144285")]
    public void Local_storage_is_modular(uint value)
    {
        foreach(var type in Types)Assert.Equal(Expected(value,type),Run(CilCompiler.BuildScheduledSequential(NarrowCilFixture.Storage(type),"GeneratedStorage").Design,value,false));
    }

    /// <summary>Unsupported local storage remains an explicit compilation error.</summary>
    [Fact]
    public void Wide_and_floating_storage_rejects()
    {
        foreach(var type in new[]{typeof(long),typeof(double),typeof(IntPtr)})
            Assert.Contains("locals",Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(NarrowCilFixture.Storage(type),"Rejected")).Message);
    }

    /// <summary>Canonical Boolean and existing full-width storage remain supported alongside the new types.</summary>
    [Fact]
    public void Existing_scalar_profiles_remain_compatible()
    {
        foreach(var type in new[]{typeof(bool),typeof(int),typeof(uint)})
        foreach(bool argument in new[]{false,true})
        {
            var method=NarrowCilFixture.Storage(type,argument);var d=CilCompiler.BuildScheduledSequential(method,"ExistingScalar").Design;
            foreach(uint value in new uint[]{0,1})
            {
                object?[] args=argument?[Activator.CreateInstance(type),value]:[value];
                Assert.Equal(value,(uint)method.Invoke(null,args)!);Assert.Equal(value,Run(d,value,argument));
            }
            if(argument)Assert.Equal(type==typeof(bool)?1:32,d.Graph.Nodes.Single(n=>n.Id==d.Inputs["arg0"]).Type.Width);
        }
    }
}
