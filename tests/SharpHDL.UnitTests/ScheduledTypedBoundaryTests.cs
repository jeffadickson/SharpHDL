using System.Reflection.Emit;
using SharpHDL.Verification;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Deterministic boundary/encoding checks also run in the focused mutation replay.</summary>
public sealed class ScheduledTypedBoundaryTests
{
    /// <summary>Exercise full and short encodings for reference and scalar locals.</summary>
    [Theory][InlineData(false,false)][InlineData(false,true)][InlineData(true,false)][InlineData(true,true)]
    public void Variable_encodings_validate_both_storage_categories(bool shortForm,bool reference)
    {
        var method=TypedCilFixture.Create(il=>
        {
            void Variable(OpCode full,OpCode compact,int slot){if(shortForm)il.Emit(compact,(byte)slot);else il.Emit(full,(short)slot);}
            Variable(OpCodes.Ldarg,OpCodes.Ldarg_S,reference?0:1);Variable(OpCodes.Stloc,OpCodes.Stloc_S,0);
            Variable(OpCodes.Ldloc,OpCodes.Ldloc_S,0);
            if(reference){il.Emit(OpCodes.Ldlen);il.Emit(OpCodes.Conv_U4);}
            else {il.Emit(OpCodes.Ldc_I4_7);il.Emit(OpCodes.Cgt_Un);}
            il.Emit(OpCodes.Ret);
        },reference?typeof(uint[]):typeof(uint));
        Assert.NotNull(CilCompiler.BuildScheduledSequential(method,"EncodedVariables"));
    }

    /// <summary>Even unreachable invalid load/store indexes must fail at the exact first-invalid boundary.</summary>
    [Theory][InlineData(false,false)][InlineData(false,true)][InlineData(true,false)][InlineData(true,true)]
    public void Invalid_index_boundaries_cover_loads_and_stores(bool local,bool store)
    {
        foreach(bool shortForm in new[]{false,true})
        {
            var method=TypedCilFixture.Create(il=>
            {
                var end=il.DefineLabel();il.Emit(OpCodes.Br,end);
                OpCode full=local?(store?OpCodes.Stloc:OpCodes.Ldloc):(store?OpCodes.Starg:OpCodes.Ldarg);
                OpCode compact=local?(store?OpCodes.Stloc_S:OpCodes.Ldloc_S):(store?OpCodes.Starg_S:OpCodes.Ldarg_S);
                int index=local?1:2;if(store)il.Emit(OpCodes.Ldc_I4_0);
                if(shortForm)il.Emit(compact,(byte)index);else il.Emit(full,(short)index);
                if(!store)il.Emit(OpCodes.Pop);il.MarkLabel(end);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ret);
            },typeof(uint));
            var error=Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(method,"BadBoundary"));
            Assert.Contains("index",error.Message);Assert.Contains("IL_",error.Message);
        }
    }

    /// <summary>Validate stack depth and native categories without relying on downstream indexing failures.</summary>
    [Theory][InlineData("underflow")][InlineData("overflow")][InlineData("extra")][InlineData("height")]
    [InlineData("nativebranch")][InlineData("mixedleft")][InlineData("mixedright")]
    public void Invalid_stack_shapes_and_mixed_comparisons_reject(string mode)
    {
        var method=TypedCilFixture.Create(il=>
        {
            if(mode=="underflow")il.Emit(OpCodes.Pop);
            else if(mode=="overflow")for(int i=0;i<33;i++)il.Emit(OpCodes.Ldc_I4_0);
            else if(mode=="extra"){il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ldc_I4_0);}
            else if(mode=="height")
            {
                var join=il.DefineLabel();il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Brtrue,join);il.Emit(OpCodes.Ldc_I4_0);
                il.MarkLabel(join);il.Emit(OpCodes.Ldc_I4_0);
            }
            else if(mode=="nativebranch")
            {
                var end=il.DefineLabel();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldlen);il.Emit(OpCodes.Brtrue,end);
                il.MarkLabel(end);il.Emit(OpCodes.Ldc_I4_0);
            }
            else
            {
                il.Emit(mode=="mixedleft"?OpCodes.Ldarg_0:OpCodes.Ldarg_1);
                il.Emit(mode=="mixedleft"?OpCodes.Ldarg_1:OpCodes.Ldarg_0);il.Emit(OpCodes.Ceq);
            }
            il.Emit(OpCodes.Ret);
        });
        Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(method,"BadShape"));
    }

    /// <summary>Both sides of the documented variable-state resource limit are exercised.</summary>
    [Theory][InlineData(1022,true)][InlineData(1023,false)]
    public void Variable_resource_boundary_is_exact(int locals,bool valid)
    {
        var method=TypedCilFixture.Create(il=>{il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ret);},Enumerable.Repeat(typeof(uint[]),locals).ToArray());
        if(valid)Assert.NotNull(CilCompiler.BuildScheduledSequential(method,"ExactLimit"));
        else Assert.Contains("1024",Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(method,"OverLimit")).Message);
    }

    /// <summary>Keep one copied-reference/scalar-local backedge case in every mutation run.</summary>
    [Fact]
    public void Scalar_and_reference_local_loop_joins_are_valid()
        =>new ScheduledArrayAliasTests().Copied_aliases_share_RAM_and_match_CLR(3);

    /// <summary>A later stack error must not mask a missing operand/store diagnostic at the original opcode.</summary>
    [Theory][InlineData("scalarstore")][InlineData("aliasstore")][InlineData("storearray")]
    [InlineData("storeindex")][InlineData("storevalue")][InlineData("rebind")]
    public void Invalid_stores_report_the_faulting_instruction(string mode)
    {
        int offset=0;
        var method=TypedCilFixture.Create(il=>
        {
            if(mode=="scalarstore"||mode=="aliasstore")
            {
                il.Emit(mode=="scalarstore"?OpCodes.Ldarg_0:OpCodes.Ldc_I4_1);offset=il.ILOffset;
                il.Emit(OpCodes.Stloc_0);
            }
            else if(mode=="rebind")
            {il.Emit(OpCodes.Ldarg_0);offset=il.ILOffset;il.Emit(OpCodes.Starg_S,(byte)0);}
            else
            {
                il.Emit(mode=="storearray"?OpCodes.Ldarg_1:OpCodes.Ldarg_0);
                il.Emit(mode=="storeindex"?OpCodes.Ldarg_0:OpCodes.Ldc_I4_0);
                il.Emit(mode=="storevalue"?OpCodes.Ldarg_0:OpCodes.Ldc_I4_1);
                offset=il.ILOffset;il.Emit(OpCodes.Stelem_I4);
            }
            il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ret);
        },mode=="scalarstore"?typeof(uint):typeof(uint[]));
        var error=Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(method,"InvalidStore"));
        Assert.Contains("IL_"+offset.ToString("x4"),error.Message);
        if(mode=="rebind")Assert.Contains("Cannot reassign the bound RAM",error.Message);
        if(mode=="aliasstore")Assert.Contains("Array alias storage requires",error.Message);
    }

    /// <summary>The largest admitted evaluation stack must not be rejected by an off-by-one guard.</summary>
    [Fact]
    public void Maximum_admitted_stack_depth_is_valid()
    {
        var method=TypedCilFixture.Create(il=>
        {
            for(int i=0;i<32;i++)il.Emit(OpCodes.Ldc_I4_0);
            for(int i=0;i<31;i++)il.Emit(OpCodes.Pop);il.Emit(OpCodes.Ret);
        });
        Assert.NotNull(CilCompiler.BuildScheduledSequential(method,"ExactStack"));
    }

    /// <summary>An excessive peak must reject even if later pops restore a valid return height.</summary>
    [Fact]
    public void Excessive_peak_rejects_at_the_push_before_later_pops()
    {
        var method=TypedCilFixture.Create(il=>
        {
            for(int i=0;i<33;i++)il.Emit(OpCodes.Ldc_I4_0);
            for(int i=0;i<32;i++)il.Emit(OpCodes.Pop);il.Emit(OpCodes.Ret);
        });
        var error=Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(method,"ExcessivePeak"));
        Assert.Contains("IL_0020",error.Message);
    }
}
