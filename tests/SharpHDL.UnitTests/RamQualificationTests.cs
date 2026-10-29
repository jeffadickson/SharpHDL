using System.Numerics;
using System.Reflection.Emit;
using SharpHDL.Verification;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Admission boundaries and lost facts from the reviewed helper mutation campaign.</summary>
public sealed class RamQualificationTests
{
    private static System.Reflection.MethodInfo Length() => TypedCilFixture.Create(il =>
    { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_U4); il.Emit(OpCodes.Ret); });

    [Theory][InlineData(1)][InlineData(65536)]
    public void Endpoint_depth_is_admitted(int depth)
    {
        var d = CilCompiler.BuildScheduledSequential(Length(), "DepthEndpoint", new SequentialOptions { MemoryDepth = depth }).Design;
        Assert.Equal(depth, Assert.Single(d.Memories).Value.Initial.Count);
    }

    [Fact]
    public void Configuration_diagnostics_explain_the_bad_binding()
    {
        var m = Length();
        Assert.Contains("cannot be combined", Assert.Throws<NotSupportedException>(() => CilCompiler.BuildScheduledSequential(m,"Conflict",new SequentialOptions
        {InitialMemory=[1],InitialMemories=new Dictionary<int,BigInteger[]>{{0,[2]}}})).Message);
        Assert.NotNull(CilCompiler.BuildScheduledSequential(m,"Empty",new SequentialOptions
        {MemoryDepth=1,InitialMemory=[1],InitialMemories=new Dictionary<int,BigInteger[]>()}));
        foreach(bool overlap in new[]{false,true})
            Assert.Contains("parameter index",Assert.Throws<ArgumentException>(()=>CilCompiler.BuildScheduledSequential(m,"BadKey",new SequentialOptions
            {MemoryDepths=overlap?new Dictionary<int,int>{{1,2}}:null!,InitialMemories=new Dictionary<int,BigInteger[]>{{1,[1]}}})).Message);
        foreach(int depth in new[]{0,65537})
            Assert.Contains("1..65536",Assert.Throws<ArgumentOutOfRangeException>(()=>CilCompiler.BuildScheduledSequential(m,"BadDepth",new SequentialOptions{MemoryDepth=depth})).Message);
        Assert.Contains("cannot be null",Assert.Throws<ArgumentException>(()=>CilCompiler.BuildScheduledSequential(m,"NullWords",new SequentialOptions
        {InitialMemories=new Dictionary<int,BigInteger[]>{{0,null!}}})).Message);
    }

    [Fact]
    public void Predicate_present_on_only_one_path_cannot_authorize_a_load()
    {
        var m=TypedCilFixture.Create(il=>
        {
            var other=il.DefineLabel();var join=il.DefineLabel();var missing=il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Brfalse,other);
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Stloc_0);
            il.Emit(OpCodes.Ldloc_0);il.Emit(OpCodes.Ldnull);il.Emit(OpCodes.Cgt_Un);il.Emit(OpCodes.Stloc_1);il.Emit(OpCodes.Br,join);
            il.MarkLabel(other);il.Emit(OpCodes.Ldc_I4_1);il.Emit(OpCodes.Stloc_1);
            il.MarkLabel(join);il.Emit(OpCodes.Ldloc_1);il.Emit(OpCodes.Brfalse,missing);
            il.Emit(OpCodes.Ldloc_0);il.Emit(OpCodes.Ldlen);il.Emit(OpCodes.Conv_U4);il.Emit(OpCodes.Ret);
            il.MarkLabel(missing);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ret);
        },typeof(uint[]),typeof(bool));
        Assert.Contains("definitely",Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(m,"Asymmetric")).Message);
    }

    [Fact]
    public void Unrelated_assignment_preserves_stack_guard_provenance()
    {
        var m=TypedCilFixture.Create(il=>
        {
            var join=il.DefineLabel();var missing=il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Brfalse,join);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Stloc_0);il.MarkLabel(join);
            il.Emit(OpCodes.Ldloc_0);il.Emit(OpCodes.Ldc_I4_1);il.Emit(OpCodes.Stloc_1);il.Emit(OpCodes.Brfalse,missing);
            il.Emit(OpCodes.Ldloc_0);il.Emit(OpCodes.Ldlen);il.Emit(OpCodes.Conv_U4);il.Emit(OpCodes.Ret);
            il.MarkLabel(missing);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ret);
        },typeof(uint[]),typeof(uint));
        Assert.NotNull(CilCompiler.BuildScheduledSequential(m,"RetainedProvenance"));
    }
}
