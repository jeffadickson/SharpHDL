using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using SharpHDL.Core;
using SharpHDL.HLS;
using SharpHDL.Simulation;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Independent exact-width and actual CLR/CIL shift conformance.</summary>
public sealed class ScheduledShiftTests
{
    /// <summary>Actual C# left-shift kernel, including CLR count masking.</summary>
    public static uint Left(uint value,int count)=>value<<count;
    /// <summary>Actual C# unsigned right-shift kernel.</summary>
    public static uint Logical(uint value,int count)=>value>>count;
    /// <summary>Actual C# two's-complement signed right-shift kernel.</summary>
    public static uint Arithmetic(uint value,int count)=>unchecked((uint)((int)value>>count));

    /// <summary>All byte values and representative constant offsets preserve bit-vector semantics.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(4096)] [InlineData(int.MaxValue)]
    public void Constant_offsets_survive_core_lowering(int amount)
    {
        var b=new ScheduledMachineBuilder("Shift");var input=b.Input("input",8);b.State("only");
        b.Output("left",input.ShiftLeft(amount));b.Output("logical",input.ShiftRight(amount));
        b.Output("arithmetic",input.ArithmeticShiftRight(amount));
        var d=b.Lower();var sim=Machine(d);
        Assert.All(d.Graph.Nodes.Where(n=>n.Operation is HardwareOperation.ShiftLeft or HardwareOperation.ShiftRight or HardwareOperation.ArithmeticShiftRight),n=>Assert.Equal(Math.Min(amount,8),n.Offset));
        for(int value=0;value<256;value++)
        {
            sim.SetInput("input",value);
            int signed=value<128?value:value-256;
            Assert.Equal(amount>=8?0:(int)(((BigInteger)value<<amount)&255),(int)sim.ReadOutput("left"));
            Assert.Equal(amount>=8?0:(int)((BigInteger)value>>amount),(int)sim.ReadOutput("logical"));
            Assert.Equal(amount>=8?(signed<0?255:0):(int)(((BigInteger)signed>>amount)&255),(int)sim.ReadOutput("arithmetic"));
        }
        Assert.Throws<ArgumentOutOfRangeException>(()=>input.ShiftLeft(-1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>input.ShiftRight(-1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>input.ArithmeticShiftRight(-1));
    }

    /// <summary>Boundary counts and operand signs agree with native C# and legacy edge timing.</summary>
    [Theory]
    [InlineData(nameof(Left))] [InlineData(nameof(Logical))] [InlineData(nameof(Arithmetic))]
    public void Cil_shifts_match_native_and_legacy(string kernel)
    {
        var method=typeof(ScheduledShiftTests).GetMethod(kernel)!;
        var native=method.CreateDelegate<Func<uint,int,uint>>();
        var d=CilCompiler.BuildScheduledSequential(method,"Scheduled"+kernel).Design;
        var sim=Machine(d);var module=CilCompiler.BuildSequential(method,"Legacy"+kernel);
        var old=module.Simulate();old.Tick(true);var ports=module.Ports.ToDictionary(p=>p.Name);
        foreach(uint value in new uint[]{0,1,127,0x7fffffffu,0x80000000u,0xffffffffu,0xdeadbeefu})
        foreach(int count in new[]{int.MinValue,-65,-33,-32,-31,-1,0,1,7,31,32,33,63,64,int.MaxValue})
        {
            foreach(var pair in new[]{("start",1u),("arg0",value),("arg1",unchecked((uint)count))})
            {sim.SetInput(pair.Item1,pair.Item2);old.Set(ports[pair.Item1],pair.Item2);}
            int edges=0;
            do
            {
                sim.Tick();old.Tick();
                foreach(string output in new[]{"busy","done","fault","timeout","result","accept"})
                    Assert.Equal(old.Read(ports[output]),sim.ReadOutput(output));
                sim.SetInput("start",0);old.Set(ports["start"],0);
            }while(sim.ReadOutput("done")==0&&++edges<64);
            Assert.Equal(1,(int)sim.ReadOutput("done"));Assert.Equal(0,(int)sim.ReadOutput("fault"));
            Assert.Equal(native(value,count),(uint)sim.ReadOutput("result"));
        }
    }

    /// <summary>Generated unsigned operands and signed counts challenge CLR masking and sign extension.</summary>
    [FsCheck.Xunit.Property(MaxTest=200,Replay="1145655947,296144285")]
    public void Randomized_clr_shifts(uint value,int count)
    {
        foreach(string kernel in new[]{nameof(Left),nameof(Logical),nameof(Arithmetic)})
        {
            var method=typeof(ScheduledShiftTests).GetMethod(kernel)!;
            var d=CilCompiler.BuildScheduledSequential(method,"Property"+kernel).Design;var sim=Machine(d);
            sim.SetInput("arg0",value);sim.SetInput("arg1",unchecked((uint)count));sim.SetInput("start",1);sim.Tick();sim.SetInput("start",0);
            for(int edge=0;edge<64&&sim.ReadOutput("done")==0;edge++)sim.Tick();
            Assert.Equal(1,(int)sim.ReadOutput("done"));Assert.Equal(0,(int)sim.ReadOutput("fault"));
            Assert.Equal(method.CreateDelegate<Func<uint,int,uint>>()(value,count),(uint)sim.ReadOutput("result"));
        }
    }

    /// <summary>Direct CIL has no C# compiler-supplied count mask, independently testing the lowering policy.</summary>
    [Theory]
    [InlineData("shl")] [InlineData("shr.un")] [InlineData("shr")]
    public void Direct_cil_counts_are_masked_by_the_scheduled_frontend(string opcode)
    {
        var assembly=AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("RawShift"+Guid.NewGuid().ToString("N")),AssemblyBuilderAccess.RunAndCollect);
        var type=assembly.DefineDynamicModule("main").DefineType("Kernel",TypeAttributes.Public);
        var method=type.DefineMethod("Shift",MethodAttributes.Public|MethodAttributes.Static,typeof(uint),new[]{typeof(uint),typeof(int)});
        var il=method.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldarg_1);
        il.Emit(opcode=="shl"?OpCodes.Shl:opcode=="shr"?OpCodes.Shr:OpCodes.Shr_Un);il.Emit(OpCodes.Ret);
        var info=type.CreateType()!.GetMethod("Shift")!;
        var native=info.CreateDelegate<Func<uint,int,uint>>();
        var d=CilCompiler.BuildScheduledSequential(info,"RawShift").Design;
        var sim=Machine(d);
        foreach(int count in new[]{int.MinValue,-33,-32,-1,0,1,31,32,33,63,64,int.MaxValue})
        {
            const uint value=0x80000001u;
            uint expected=opcode=="shl"?Left(value,count):opcode=="shr"?Arithmetic(value,count):Logical(value,count);
            Assert.Equal(expected,native(value,count));
            sim.SetInput("arg0",value);sim.SetInput("arg1",unchecked((uint)count));sim.SetInput("start",1);
            sim.Tick();sim.SetInput("start",0);
            for(int edge=0;edge<32&&sim.ReadOutput("done")==0;edge++)sim.Tick();
            Assert.Equal(1,(int)sim.ReadOutput("done"));Assert.Equal(expected,(uint)sim.ReadOutput("result"));
        }
    }

    private static HardwareGraphMachine Machine(ScheduledHardwareDesign d)
        =>new(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
}
