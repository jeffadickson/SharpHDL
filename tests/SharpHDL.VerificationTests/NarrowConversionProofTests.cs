using SharpHDL.Core;
using SharpHDL.HLS;
using SharpHDL.Verification;
using Xunit;

namespace SharpHDL.VerificationTests;

/// <summary>Actual all-input bit-vector checks of local conversion expressions, not sequential proofs.</summary>
public sealed class NarrowConversionProofTests
{
    /// <summary>Compare HLS truncation/extension with independent mask/subtract logic and deliberate corruptions.</summary>
    [Theory]
    [InlineData(8,true,false)][InlineData(8,false,false)][InlineData(16,true,false)][InlineData(16,false,false)]
    [InlineData(8,true,true)][InlineData(8,false,true)][InlineData(16,true,true)][InlineData(16,false,true)]
    public void Conversion_cones_match_independent_arithmetic_for_every_32_bit_input(int width,bool signed,bool corrupted)
    {
        var builder=new ScheduledMachineBuilder("ConversionProof");var input=builder.Input("value",32);builder.State("idle");
        var narrow=input.Resize(width);builder.Output("converted",signed?narrow.SignExtend(32):narrow.Resize(32));
        var d=builder.Lower();int output=d.Outputs["converted"];
        var nodes=d.Graph.Nodes.ToDictionary(n=>n.Id);var live=new HashSet<int>();
        void Visit(int id){if(!live.Add(id))return;foreach(int operand in nodes[id].Operands)Visit(operand);}
        Visit(output);
        // This projection is only the combinational conversion cone. Assert that
        // no state boundary was removed from the expression being checked.
        Assert.DoesNotContain(d.Graph.StateInputNodeIds,id=>live.Contains(id));
        var left=new HardwareGraph(d.Graph.Nodes.Where(n=>live.Contains(n.Id)));
        var word=new HardwareType(32);var bit=new HardwareType(1);
        var reference=new List<HardwareNode>
        {
            new(0,HardwareOperation.Input,word,Array.Empty<int>(),Name:"value"),
            new(1,HardwareOperation.Constant,word,Array.Empty<int>(),ConstantValue:(1u<<width)-1),
            new(2,HardwareOperation.And,word,new[]{0,1}),
            new(3,HardwareOperation.Constant,word,Array.Empty<int>(),ConstantValue:1u<<(width-1)),
            new(4,HardwareOperation.LessThan,bit,new[]{2,3}),
            new(5,HardwareOperation.Constant,word,Array.Empty<int>(),ConstantValue:1u<<width),
            new(6,HardwareOperation.Subtract,word,new[]{2,5}),
            new(7,HardwareOperation.Select,word,new[]{4,2,6})
        };
        int rightOutput=signed?7:2;
        if(corrupted)
        {
            reference.Add(new(8,HardwareOperation.Constant,word,Array.Empty<int>(),ConstantValue:1));
            reference.Add(new(9,HardwareOperation.Xor,word,new[]{rightOutput,8}));rightOutput=9;
        }
        var result=BitVectorEquivalence.Check(left,output,new HardwareGraph(reference),rightOutput);
        Assert.Equal(corrupted?EquivalenceStatus.Different:EquivalenceStatus.Equivalent,result.Status);
        Assert.Contains("(check-sat)",result.SmtLib);
        if(corrupted)Assert.Single(result.Counterexample);else Assert.Empty(result.Counterexample);
        // Campaign drivers may retain exact native SMT/results. Test execution
        // still checks the solver and independent replay when no output is requested.
        string? evidence=Environment.GetEnvironmentVariable("SHARPHDL_PROOF_EVIDENCE");
        if(evidence is not null)
        {
            if(!Directory.Exists(evidence))throw new DirectoryNotFoundException(evidence);
            string stem=$"conversion-{width}-{(signed?"signed":"unsigned")}-{(corrupted?"corrupt":"correct")}";
            using(var file=new FileStream(Path.Combine(evidence,stem+".smt2"),FileMode.CreateNew))
            using(var writer=new StreamWriter(file))writer.Write(result.SmtLib);
            using var record=new FileStream(Path.Combine(evidence,stem+".json"),FileMode.CreateNew);
            System.Text.Json.JsonSerializer.Serialize(record,new{width,signed,corrupted,status=result.Status.ToString(),
                result.Reason,result.SolverVersion,result.Counterexample});
        }
    }
}
