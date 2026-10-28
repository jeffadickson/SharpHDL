using System.Numerics;
using System.Text.RegularExpressions;
using SharpHDL.Core;
using SharpHDL.Verification;
using SharpHDL.Simulation;
using Xunit;

namespace SharpHDL.VerificationTests;

/// <summary>Real native Z3 qualification, including complete small truth tables and negative controls.</summary>
public sealed class BitVectorEquivalenceTests
{
    /// <summary>Constant shift boundaries, one-bit signed values and identity extensions retain physical-width semantics.</summary>
    [Fact]
    public void Edge_widths_and_identity_operations_have_exact_bit_patterns()
    {
        foreach(int width in new[]{1,8,32})
        {
            uint mask=width==32?uint.MaxValue:(1U<<width)-1;
            foreach(uint value in new[]{0U,1U,mask,1U<<(width-1)})
            foreach(int offset in new[]{0,width-1,width,width+1,int.MaxValue})
            foreach(var op in new[]{HardwareOperation.ShiftLeft,HardwareOperation.ShiftRight,HardwareOperation.ArithmeticShiftRight})
            {
                long signed=(value&(1U<<(width-1)))!=0?(long)value-(1L<<width):value;
                uint expected=op switch{HardwareOperation.ShiftLeft=>offset>=width?0:unchecked(value<<offset)&mask,
                    HardwareOperation.ShiftRight=>offset>=width?0:value>>offset,
                    _=>unchecked((uint)(offset>=width?(signed<0?-1:0):signed>>offset))&mask};
                var left=new HardwareGraph([new(0,HardwareOperation.Constant,new(width,true),[],new BigInteger(value)),new(1,op,new(width,true),[0],Offset:offset)]);
                var right=new HardwareGraph([new(0,HardwareOperation.Constant,new(width,true),[],new BigInteger(expected))]);
                if(offset<1_000_000)Assert.Equal(new BigInteger(expected),HardwareGraphEvaluator.Evaluate(left,new Dictionary<int,BigInteger>())[1]);
                Assert.Equal(EquivalenceStatus.Equivalent,BitVectorEquivalence.Check(left,1,right,0).Status);
            }
            foreach(var op in new[]{HardwareOperation.Resize,HardwareOperation.SignExtend})
            {
                var graph=new HardwareGraph([new(0,HardwareOperation.Input,new(width),[],Name:"x"),new(1,op,new(width),[0])]);
                Assert.Equal(EquivalenceStatus.Equivalent,BitVectorEquivalence.Check(graph,0,graph,1).Status);
            }
        }
    }
    /// <summary>Replay allocations stay small when an IR constant count greatly exceeds its hardware width.</summary>
    [Fact]
    public void Oversized_shift_replay_has_a_bounded_host_allocation()
    {
        var graph=new HardwareGraph([new(0,HardwareOperation.Input,new(8),[],Name:"x"),new(1,HardwareOperation.ShiftLeft,new(8),[0],Offset:1_000_000)]);
        var inputs=new Dictionary<int,BigInteger>{{0,1}};
        HardwareGraphEvaluator.Evaluate(graph,inputs); // Warm generic/runtime initialization outside the bound.
        long before=GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(BigInteger.Zero,HardwareGraphEvaluator.Evaluate(graph,inputs)[1]);
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread()-before,0,32768);
        Assert.Equal(EquivalenceStatus.Different,BitVectorEquivalence.Check(graph,1,graph,0).Status);
    }
    /// <summary>Native UNSAT checks justify the semantic-equivalent encoding mutations instead of inventing kills.</summary>
    [Fact]
    public void Equivalent_mutation_variants_are_independently_checked_by_native_Z3()
    {
        for(uint width=1;width<=32;width++)
        {
            using var c=new Microsoft.Z3.Context();using var solver=c.MkSolver("QF_BV");
            using var parameters=c.MkParams();parameters.Add("timeout",5000U);solver.Parameters=parameters;
            var x=c.MkBVConst("x",width);var offset=c.MkBVConst("offset",32);
            var oldCount=(Microsoft.Z3.BitVecExpr)c.MkITE(c.MkBVULE(offset,c.MkBV(width-1,32)),offset,c.MkBV(width-1,32));
            var newCount=(Microsoft.Z3.BitVecExpr)c.MkITE(c.MkBVULE(offset,c.MkBV(width+1,32)),offset,c.MkBV(width+1,32));
            solver.Assert(c.MkBVULE(offset,c.MkBV(int.MaxValue,32)),c.MkOr(
                c.MkNot(c.MkEq(c.MkBVSHL(x,c.MkBV(width,width)),c.MkBV(0,width))),
                c.MkNot(c.MkEq(c.MkBVLSHR(x,c.MkBV(width,width)),c.MkBV(0,width))),
                c.MkNot(c.MkEq(c.MkBVASHR(x,c.MkExtract(width-1,0,oldCount)),c.MkBVASHR(x,c.MkExtract(width-1,0,newCount)))),
                c.MkNot(c.MkEq(x,c.MkExtract(width-1,0,x))),c.MkNot(c.MkEq(x,c.MkZeroExt(0,x))),c.MkNot(c.MkEq(x,c.MkSignExt(0,x)))));
            Assert.Equal(Microsoft.Z3.Status.UNSATISFIABLE,solver.Check());
        }
    }
    /// <summary>Every selected encoding is checked against correct and deliberately incorrect references.</summary>
    public static IEnumerable<object[]> Cases=>Enumerable.Range(0,BitVectorCases.Names.Count).SelectMany(i=>new[]{new object[]{i,false},new object[]{i,true}});
    /// <summary>SAT witnesses must identify the sole deliberately corrupted table entry; UNSAT must be actual solver success.</summary>
    [Theory][MemberData(nameof(Cases))]
    public void Supported_operations_match_complete_independent_truth_tables(int index,bool corrupted)
    {
        var sample=BitVectorCases.Build(index,corrupted);
        var result=BitVectorEquivalence.Check(sample.Left,sample.LeftOutput,sample.Right,sample.RightOutput);
        Assert.Equal(corrupted?EquivalenceStatus.Different:EquivalenceStatus.Equivalent,result.Status);
        Assert.Contains("4.12.2",result.SolverVersion);Assert.Contains("(check-sat)",result.SmtLib);
        if(corrupted){Assert.Equal("3",result.Counterexample["x"]);Assert.Equal("1",result.Counterexample["y"]);}
        else Assert.Empty(result.Counterexample);
    }
    /// <summary>Unknown from an actual deterministic native resource limit is never classified as proof.</summary>
    [Fact]
    public void Native_resource_exhaustion_and_precancellation_do_not_prove_equivalence()
    {
        var sample=BitVectorCases.Build(2);
        var result=BitVectorEquivalence.Check(sample.Left,sample.LeftOutput,sample.Right,sample.RightOutput,resourceLimit:1);
        Assert.Equal(EquivalenceStatus.Unknown,result.Status);Assert.NotEmpty(result.Reason);Assert.Empty(result.Counterexample);
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        Assert.Equal(EquivalenceStatus.Cancelled,BitVectorEquivalence.Check(sample.Left,sample.LeftOutput,sample.Right,sample.RightOutput,cancellationToken:cancelled.Token).Status);
    }
    /// <summary>State, RAM, ROM and DSP expressions are explicitly rejected, even when not the selected output.</summary>
    [Fact]
    public void Unsupported_state_and_memory_are_not_silently_omitted()
    {
        foreach(var op in new[]{HardwareOperation.Register,HardwareOperation.MemoryRead,HardwareOperation.Lookup,HardwareOperation.DspMultiplyUnsigned})
        {
            var nodes=new[]{new HardwareNode(0,HardwareOperation.Input,new(2),[],Name:"x"),
                new HardwareNode(1,op,new(op==HardwareOperation.DspMultiplyUnsigned?4:2),op==HardwareOperation.DspMultiplyUnsigned?[0,0]:[0],
                    Table:op==HardwareOperation.Lookup?new BigInteger[]{0}:null,MemoryName:op==HardwareOperation.MemoryRead?"ram":null)};
            var graph=new HardwareGraph(nodes);
            var result=BitVectorEquivalence.Check(graph,0,graph,0);
            Assert.Equal(EquivalenceStatus.Unsupported,result.Status);Assert.Contains(op.ToString(),result.Reason);Assert.Empty(result.SmtLib);
        }
        var state=new HardwareGraph([new(0,HardwareOperation.Input,new(1),[],Name:"state")],[0]);
        Assert.Equal(EquivalenceStatus.Unsupported,BitVectorEquivalence.Check(state,0,state,0).Status);
    }
    /// <summary>Exact node/width/input budgets are allowed; oversized and incompatible interfaces fail before solving.</summary>
    [Fact]
    public void Profile_bounds_and_interfaces_are_validated()
    {
        var constant=new HardwareGraph([new(0,HardwareOperation.Constant,new(1),[],BigInteger.Zero)]);
        foreach(int count in new[]{256,257})
        {
            var graph=new HardwareGraph(Enumerable.Range(0,count).Select(i=>new HardwareNode(i,HardwareOperation.Constant,new(1),[],BigInteger.Zero)));
            Assert.Equal(count==256?EquivalenceStatus.Equivalent:EquivalenceStatus.Unsupported,BitVectorEquivalence.Check(graph,count-1,constant,0).Status);
        }
        foreach(int count in new[]{4,5})
        {
            var graph=new HardwareGraph(Enumerable.Range(0,count).Select(i=>new HardwareNode(i,HardwareOperation.Input,new(32),[],Name:"n"+i)));
            Assert.Equal(count==4?EquivalenceStatus.Equivalent:EquivalenceStatus.Unsupported,BitVectorEquivalence.Check(graph,0,graph,0).Status);
        }
        var wide=new HardwareGraph([new(0,HardwareOperation.Input,new(33),[],Name:"x")]);
        Assert.Equal(EquivalenceStatus.Unsupported,BitVectorEquivalence.Check(wide,0,wide,0).Status);
        Assert.Equal(EquivalenceStatus.Unsupported,BitVectorEquivalence.Check(constant,99,constant,0).Status);
        var x=new HardwareGraph([new(0,HardwareOperation.Input,new(1),[],Name:"x")]);
        var y=new HardwareGraph([new(0,HardwareOperation.Input,new(1),[],Name:"y")]);
        Assert.Equal(EquivalenceStatus.Unsupported,BitVectorEquivalence.Check(x,0,y,0).Status);
        var width=new HardwareGraph([new(0,HardwareOperation.Input,new(2),[],Name:"x")]);
        Assert.Equal(EquivalenceStatus.Unsupported,BitVectorEquivalence.Check(x,0,width,0).Status);
        foreach(int timeout in new[]{0,60001})Assert.Throws<ArgumentOutOfRangeException>(()=>BitVectorEquivalence.Check(x,0,x,0,timeout));
    }
    /// <summary>Generated input names are checked against an independent BCL regex grammar.</summary>
    [FsCheck.Xunit.Property(MaxTest=80,Replay="1145655947,296144285")]
    public void Names_are_validated_without_SMT_injection(string name)
    {
        var graph=new HardwareGraph([new(0,HardwareOperation.Input,new(1),[],Name:name)]);
        bool legal=name is not null&&Regex.IsMatch(name,@"\A[A-Za-z_][A-Za-z0-9_]{0,63}\z",RegexOptions.CultureInvariant|RegexOptions.NonBacktracking);
        Assert.Equal(legal?EquivalenceStatus.Equivalent:EquivalenceStatus.Unsupported,BitVectorEquivalence.Check(graph,0,graph,0).Status);
    }
    /// <summary>Independent CLR integer arithmetic challenges modular widths and signed comparison over reproducible inputs.</summary>
    [FsCheck.Xunit.Property(MaxTest=100,Replay="1145655947,296144285")]
    public void Constant_arithmetic_matches_CLR_bit_patterns(uint x,uint y,byte selector,byte widthSelector)
    {
        int width=1+widthSelector%32;uint mask=width==32?uint.MaxValue:(1U<<width)-1;x&=mask;y&=mask;
        int kind=selector%5;bool compare=kind==4;int resultWidth=compare?1:width;
        uint expected=kind switch{0=>unchecked(x+y)&mask,1=>unchecked(x-y)&mask,2=>(x^y)&mask,3=>unchecked(x*y)&mask,
            _=>unchecked((int)(x<<(32-width)))>>(32-width)<(unchecked((int)(y<<(32-width)))>>(32-width))?1U:0U};
        var op=new[]{HardwareOperation.Add,HardwareOperation.Subtract,HardwareOperation.Xor,HardwareOperation.Multiply,HardwareOperation.SignedLessThan}[kind];
        var left=new HardwareGraph([new(0,HardwareOperation.Constant,new(width),[],new BigInteger(x)),new(1,HardwareOperation.Constant,new(width),[],new BigInteger(y)),new(2,op,new(resultWidth),[0,1])]);
        var right=new HardwareGraph([new(0,HardwareOperation.Constant,new(resultWidth),[],new BigInteger(expected))]);
        Assert.Equal(EquivalenceStatus.Equivalent,BitVectorEquivalence.Check(left,2,right,0).Status);
    }
}
