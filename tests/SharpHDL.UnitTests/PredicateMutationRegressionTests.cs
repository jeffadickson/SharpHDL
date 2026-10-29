using System.Reflection.Emit;
using SharpHDL.Verification;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Replays facts previously hidden by early rejection or loss-of-precision mutants.</summary>
public sealed class PredicateMutationRegressionTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public void Unrelated_write_preserves_stack_or_stored_boolean_predicate(bool stored)
    {
        var m=TypedCilFixture.Create(il=>
        {
            var join=il.DefineLabel();var missing=il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Brfalse,join);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Stloc_0);il.MarkLabel(join);
            il.Emit(OpCodes.Ldloc_0);il.Emit(OpCodes.Ldnull);il.Emit(OpCodes.Cgt_Un);
            if(stored)il.Emit(OpCodes.Stloc_1);
            il.Emit(OpCodes.Ldc_I4_1);il.Emit(OpCodes.Stloc_2);
            if(stored)il.Emit(OpCodes.Ldloc_1);
            il.Emit(OpCodes.Brfalse,missing);il.Emit(OpCodes.Ldloc_0);il.Emit(OpCodes.Ldlen);il.Emit(OpCodes.Conv_U4);il.Emit(OpCodes.Ret);
            il.MarkLabel(missing);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ret);
        },typeof(uint[]),typeof(bool),typeof(uint));
        Assert.NotNull(CilCompiler.BuildScheduledSequential(m,"PreservedBoolean"));
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void Stale_stack_predicate_never_refines_an_overwritten_or_wrong_local(bool predicate)
    {
        var m=TypedCilFixture.Create(il=>
        {
            var missing=il.DefineLabel();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Stloc_0);il.Emit(OpCodes.Ldloc_0);
            if(predicate){il.Emit(OpCodes.Ldnull);il.Emit(OpCodes.Cgt_Un);}
            il.Emit(OpCodes.Ldnull);il.Emit(OpCodes.Stloc_0);il.Emit(OpCodes.Brfalse,missing);
            il.Emit(predicate?OpCodes.Ldloc_0:OpCodes.Ldloc_1);il.Emit(OpCodes.Ldlen);il.Emit(OpCodes.Conv_U4);il.Emit(OpCodes.Ret);
            il.MarkLabel(missing);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ret);
        },typeof(uint[]),typeof(uint[]));
        Assert.Contains("definitely",Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(m,"StaleBoolean")).Message);
    }

    [Fact]
    public void Safe_asymmetric_predicate_join_compiles_and_preserves_CLR_results()
    {
        var m=TypedCilFixture.Create(il=>
        {
            var other=il.DefineLabel();var join=il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Stloc_0);il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Brfalse,other);
            il.Emit(OpCodes.Ldloc_0);il.Emit(OpCodes.Ldnull);il.Emit(OpCodes.Cgt_Un);il.Emit(OpCodes.Stloc_1);il.Emit(OpCodes.Br,join);
            il.MarkLabel(other);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Stloc_1);
            il.MarkLabel(join);il.Emit(OpCodes.Ldloc_1);il.Emit(OpCodes.Ret);
        },typeof(uint[]),typeof(bool));
        Assert.NotNull(CilCompiler.BuildScheduledSequential(m,"SafeAsymmetric"));
        Assert.Equal(0u,(uint)m.Invoke(null,[new uint[1],0u])!);Assert.Equal(1u,(uint)m.Invoke(null,[new uint[1],1u])!);
    }
}
