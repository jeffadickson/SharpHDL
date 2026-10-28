using System.Reflection;
using System.Reflection.Emit;
using SharpHDL.HLS;
using SharpHDL.Simulation;
using SharpHDL.Verification;
using SharpHDL.Examples;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Actual CLR exceptions and independent mathematical ranges define checked conversion behavior.</summary>
public sealed class ScheduledCheckedConversionTests
{
    private static readonly string[] Names=["conv.ovf.i1","conv.ovf.u1","conv.ovf.i2","conv.ovf.u2","conv.ovf.i4","conv.ovf.u4",
        "conv.ovf.i1.un","conv.ovf.u1.un","conv.ovf.i2.un","conv.ovf.u2.un","conv.ovf.i4.un","conv.ovf.u4.un"];
    private static readonly Dictionary<string,OpCode> Codes=typeof(OpCodes).GetFields().Where(f=>f.FieldType==typeof(OpCode))
        .Select(f=>(OpCode)f.GetValue(null)!).Where(c=>c.Name!=null).ToDictionary(c=>c.Name!);
    private static readonly Dictionary<string,MethodInfo> Methods=Names.ToDictionary(n=>n,n=>NarrowCilFixture.Create(Codes[n]));

    private static bool Fits(uint bits,string opcode)
    {
        string target=opcode.Split('.')[2];int width=int.Parse(target[1..])*8;
        long value=opcode.EndsWith(".un",StringComparison.Ordinal)?bits:unchecked((int)bits);
        long min=target[0]=='i'?-(1L<<(width-1)):0;
        long max=target[0]=='i'?(1L<<(width-1))-1:(1L<<width)-1;
        return min<=value&&value<=max;
    }

    private static void Check(uint bits,string opcode,bool clr)
    {
        bool fits=Fits(bits,opcode);uint expected=bits;
        if(clr)
        {
            if(fits)expected=(uint)Methods[opcode].Invoke(null,[bits])!;
            else Assert.IsType<OverflowException>(Assert.Throws<TargetInvocationException>(()=>Methods[opcode].Invoke(null,[bits])).InnerException);
        }
        // Compile while this test executes: a process-static graph cache would
        // reuse pre-mutation lowering and mask changes activated by Stryker.
        var d=CilCompiler.BuildScheduledSequential(Methods[opcode],"Checked"+opcode.Replace('.','_')).Design;
        var sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        sim.SetInput("arg0",bits);sim.SetInput("start",1);sim.Tick();sim.SetInput("start",0);sim.SetInput("arg0",~bits);
        for(int edge=0;edge<32&&sim.ReadOutput("done")==0;edge++)sim.Tick();
        Assert.Equal(1,(int)sim.ReadOutput("done"));Assert.Equal(0,(int)sim.ReadOutput("busy"));Assert.Equal(0,(int)sim.ReadOutput("timeout"));
        Assert.Equal(fits?0:1,(int)sim.ReadOutput("fault"));Assert.Equal(fits?0:1,(int)sim.ReadOutput("conversion_overflow"));
        Assert.Equal(fits?0:1,(int)sim.ReadOutput("fault_il")); // ldarg.0 is one byte; conversion starts at IL_0001.
        Assert.Equal(fits?expected:0,(uint)sim.ReadOutput("result"));
        sim.Tick();Assert.Equal(fits?0:1,(int)sim.ReadOutput("conversion_overflow"));
        sim.SetInput("arg0",0);sim.SetInput("start",1);sim.Tick();sim.SetInput("start",0);
        Assert.Equal(0,(int)sim.ReadOutput("fault"));Assert.Equal(0,(int)sim.ReadOutput("fault_il"));
        Assert.Equal(0,(int)sim.ReadOutput("conversion_overflow"));
        for(int edge=0;edge<32&&sim.ReadOutput("done")==0;edge++)sim.Tick();
        Assert.Equal(1,(int)sim.ReadOutput("done"));Assert.Equal(0,(int)sim.ReadOutput("fault"));
        sim.Reset();Assert.Equal(0,(int)sim.ReadOutput("done"));Assert.Equal(0,(int)sim.ReadOutput("conversion_overflow"));
    }

    /// <summary>Every admitted opcode is compared to CLR execution across both signed and unsigned boundaries.</summary>
    [Theory]
    [InlineData("conv.ovf.i1")][InlineData("conv.ovf.u1")][InlineData("conv.ovf.i2")][InlineData("conv.ovf.u2")]
    [InlineData("conv.ovf.i4")][InlineData("conv.ovf.u4")][InlineData("conv.ovf.i1.un")][InlineData("conv.ovf.u1.un")]
    [InlineData("conv.ovf.i2.un")][InlineData("conv.ovf.u2.un")][InlineData("conv.ovf.i4.un")][InlineData("conv.ovf.u4.un")]
    public void Checked_boundaries_and_diagnostics_match_CLR(string opcode)
    {
        foreach(uint bits in new uint[]{0,1,126,127,128,255,256,32767,32768,65535,65536,0x7fffffff,0x80000000,
            0xffff7fff,0xffff8000,0xffffff7f,0xffffff80,uint.MaxValue})Check(bits,opcode,true);
    }

    /// <summary>Reproducible full-width generated values challenge the independent range oracle.</summary>
    [FsCheck.Xunit.Property(MaxTest=100,Replay="1145655947,296144285")]
    public void Generated_checked_values_match_ranges(uint bits)
    {
        foreach(string opcode in Names)Check(bits,opcode,true);
    }

    /// <summary>Defensive helper boundaries reject unsound input widths and unsupported opcode families.</summary>
    [Fact]
    public void Helper_rejects_wide_stack_and_unknown_checked_opcodes()
    {
        var b=new ScheduledMachineBuilder("InvalidChecked");
        var widthError=Assert.Throws<ArgumentException>(()=>CilCompiler.ScheduledClrChecked(b,b.Input("wide",64),"conv.ovf.i1"));
        Assert.Equal("value",widthError.ParamName);Assert.Contains("32-bit",widthError.Message);
        foreach(string opcode in new[]{"conv.ovf.i8","conv.ovf.i","conv.i1","conv.ovf.i1.invalid"})
        {
            var error=Assert.Throws<ArgumentException>(()=>CilCompiler.ScheduledClrChecked(b,b.Input("x"+opcode.Replace('.','_'),32),opcode));
            Assert.Equal("opcode",error.ParamName);Assert.Contains("Unsupported checked",error.Message);
        }
    }

    /// <summary>Overflow is an early exit, not transactional rollback; later RAM stores must never execute.</summary>
    [Fact]
    public void Checked_CSharp_preserves_prior_RAM_writes_and_suppresses_later_stores()
    {
        var method=typeof(ScheduledNarrowCil).GetMethod(nameof(ScheduledNarrowCil.CheckedStore))!;
        var d=CilCompiler.BuildScheduledSequential(method,"CheckedMemory",new SequentialOptions{MemoryDepth=2,InitialMemory=[0,5],MaxCycles=128}).Design;
        var sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        uint[] memory=[0,5];
        Assert.Throws<OverflowException>(()=>ScheduledNarrowCil.CheckedStore(memory,256));
        void Run(uint value)
        {
            sim.SetInput("arg1",value);sim.SetInput("start",1);sim.Tick();sim.SetInput("start",0);
            for(int edge=0;edge<130&&sim.ReadOutput("done")==0;edge++)sim.Tick();
            Assert.Equal(1,(int)sim.ReadOutput("done"));
        }
        Run(256);Assert.Equal(1,(int)sim.ReadOutput("conversion_overflow"));Assert.True(sim.ReadOutput("fault_il")>0);
        Assert.Equal(0,(int)sim.ReadOutput("result"));
        Run(0);Assert.Equal(ScheduledNarrowCil.CheckedStore(memory,0),(uint)sim.ReadOutput("result"));
        Run(255);Assert.Equal(ScheduledNarrowCil.CheckedStore(memory,255),(uint)sim.ReadOutput("result"));
        Run(0);Assert.Equal(ScheduledNarrowCil.CheckedStore(memory,0),(uint)sim.ReadOutput("result"));
    }

    /// <summary>Watchdog expiry has priority over conversion diagnostics.</summary>
    [Fact]
    public void Timeout_does_not_report_an_overflow_that_did_not_execute()
    {
        var d=CilCompiler.BuildScheduledSequential(Methods["conv.ovf.u1.un"],"TimeoutChecked",new SequentialOptions{MaxCycles=1}).Design;
        var sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        sim.SetInput("arg0",256);sim.SetInput("start",1);sim.Tick();sim.SetInput("start",0);
        for(int edge=0;edge<8&&sim.ReadOutput("done")==0;edge++)sim.Tick();
        Assert.Equal(1,(int)sim.ReadOutput("done"));Assert.Equal(1,(int)sim.ReadOutput("timeout"));
        Assert.Equal(0,(int)sim.ReadOutput("conversion_overflow"));Assert.Equal(0,(int)sim.ReadOutput("fault_il"));
    }
}
