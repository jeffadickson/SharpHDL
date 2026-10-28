using SharpHDL.Core;
using SharpHDL.HLS;
using SharpHDL.Verification;
using Xunit;

namespace SharpHDL.VerificationTests;

/// <summary>Native local range-predicate proofs; no claim of whole sequential equivalence.</summary>
public sealed class CheckedConversionProofTests
{
    /// <summary>Compare the actual frontend helper against signed/unsigned interval bounds for every input.</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void Checked_predicates_match_independent_ranges_and_replay_corruptions(bool corrupt)
    {
        foreach(string target in new[]{"i1","u1","i2","u2","i4","u4"})
        foreach(bool unsignedSource in new[]{false,true})
        {
            string opcode="conv.ovf."+target+(unsignedSource?".un":"");
            var b=new ScheduledMachineBuilder("CheckedProof");var x=b.Input("value",32);b.State("idle");
            b.Output("fits",CilCompiler.ScheduledClrChecked(b,x,opcode).Fits);var d=b.Lower();int output=d.Outputs["fits"];
            var nodes=d.Graph.Nodes.ToDictionary(n=>n.Id);var live=new HashSet<int>();
            void Visit(int id){if(!live.Add(id))return;foreach(int operand in nodes[id].Operands)Visit(operand);}
            Visit(output);live.Add(d.Inputs["value"]); // Keep the same public input interface even for total conversions.
            Assert.DoesNotContain(d.Graph.StateInputNodeIds,id=>live.Contains(id));
            var left=new HardwareGraph(d.Graph.Nodes.Where(n=>live.Contains(n.Id)));
            int width=int.Parse(target[1..])*8;bool signed=target[0]=='i';
            long min=signed?-(1L<<(width-1)):0,max=signed?(1L<<(width-1))-1:(1L<<width)-1;
            var reference=new List<HardwareNode>();
            int Add(HardwareOperation op,int bits,int[] operands,uint? constant=null,string? name=null)
            {int id=reference.Count;reference.Add(new(id,op,new HardwareType(bits),operands,Name:name,ConstantValue:constant));return id;}
            int input=Add(HardwareOperation.Input,32,[],name:"value");
            int low=Add(HardwareOperation.Constant,32,[],unchecked((uint)min));
            int high=Add(HardwareOperation.Constant,32,[],unchecked((uint)max));
            int predicate;
            if(unsignedSource)
            {
                int above=Add(HardwareOperation.LessThan,1,[high,input]);predicate=Add(HardwareOperation.Not,1,[above]);
            }
            else
            {
                int below=Add(HardwareOperation.SignedLessThan,1,[input,low]);
                // UInt32 destinations impose only the nonnegative lower bound on an Int32 source.
                int above=!signed&&width==32?Add(HardwareOperation.Constant,1,[],0):Add(HardwareOperation.SignedLessThan,1,[high,input]);
                int outside=Add(HardwareOperation.Or,1,[below,above]);predicate=Add(HardwareOperation.Not,1,[outside]);
            }
            if(corrupt)predicate=Add(HardwareOperation.Not,1,[predicate]);
            var result=BitVectorEquivalence.Check(left,output,new HardwareGraph(reference),predicate);
            Assert.Equal(corrupt?EquivalenceStatus.Different:EquivalenceStatus.Equivalent,result.Status);
            if(corrupt)Assert.Single(result.Counterexample);
            string? evidence=Environment.GetEnvironmentVariable("SHARPHDL_PROOF_EVIDENCE");
            if(evidence is not null)
            {
                if(!Directory.Exists(evidence))throw new DirectoryNotFoundException(evidence);
                string stem="checked-"+target+(unsignedSource?"-unsigned":"-signed")+(corrupt?"-corrupt":"-correct");
                using(var file=new FileStream(Path.Combine(evidence,stem+".smt2"),FileMode.CreateNew))
                using(var writer=new StreamWriter(file))writer.Write(result.SmtLib);
                using var record=new FileStream(Path.Combine(evidence,stem+".json"),FileMode.CreateNew);
                System.Text.Json.JsonSerializer.Serialize(record,new{opcode,corrupt,status=result.Status.ToString(),result.Reason,result.SolverVersion,result.Counterexample});
            }
        }
    }
}
