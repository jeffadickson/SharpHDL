using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using Microsoft.Z3;
using SharpHDL.Core;
using SharpHDL.Simulation;

namespace SharpHDL.Verification;

/// <summary>Only Equivalent denotes a completed UNSAT check of the declared combinational profile.</summary>
public enum EquivalenceStatus
{
    /// <summary>No differing valuation exists within the complete declared input bit-vector space.</summary>
    Equivalent,
    /// <summary>A differing valuation was found and replayed through the independent Core evaluator.</summary>
    Different,
    /// <summary>The graph/interface is outside the explicitly supported profile.</summary>
    Unsupported,
    /// <summary>The solver did not decide, or its counterexample failed independent replay.</summary>
    Unknown,
    /// <summary>The caller cancelled; this is not proof.</summary>
    Cancelled,
    /// <summary>Matching native solver assets are unavailable on this host.</summary>
    Unavailable
}

/// <summary>Solver outcome, diagnostic, decimal input valuation, replayable SMT-LIB and actual native version.</summary>
public sealed record EquivalenceResult(EquivalenceStatus Status,string Reason,
    IReadOnlyDictionary<string,string> Counterexample,string SmtLib,string SolverVersion);

/// <summary>
/// Z3 QF_BV equivalence for two combinational outputs, not sequential equivalence.
/// Profile: 1..256 nodes per graph, widths 1..32, at most 128 total input bits,
/// matching ordinal input names/widths, no state, RAM, ROM, or DSP primitives.
/// The Signed type flag alone does not change Core arithmetic: explicit signed
/// comparisons, arithmetic shifts and sign extensions select signed semantics.
/// </summary>
public static class BitVectorEquivalence
{
    /// <summary>
    /// Check equality for every shared input valuation. Milliseconds bound solver work;
    /// resourceLimit is Z3's deterministic work budget (zero uses no extra limit).
    /// Unsupported input is rejected before constructing a native context. A SAT
    /// valuation must independently reproduce the difference before it is reported.
    /// </summary>
    public static EquivalenceResult Check(HardwareGraph left,int leftOutput,HardwareGraph right,int rightOutput,
        int timeoutMilliseconds=5000,uint resourceLimit=0,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(left);ArgumentNullException.ThrowIfNull(right);
        if(timeoutMilliseconds is <1 or >60000)throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        EquivalenceResult Failure(EquivalenceStatus status,string reason)=>new(status,reason,
            new ReadOnlyDictionary<string,string>(new Dictionary<string,string>()),"","");
        string? invalid=Validate(left,leftOutput)??Validate(right,rightOutput);
        if(invalid is not null)return Failure(EquivalenceStatus.Unsupported,invalid);
        var li=left.Nodes.Where(n=>n.Operation==HardwareOperation.Input).OrderBy(n=>n.Name,StringComparer.Ordinal).ToArray();
        var ri=right.Nodes.Where(n=>n.Operation==HardwareOperation.Input).OrderBy(n=>n.Name,StringComparer.Ordinal).ToArray();
        if(li.Length!=ri.Length||li.Where((n,i)=>n.Name!=ri[i].Name||n.Type.Width!=ri[i].Type.Width).Any()||
            left.Nodes.Single(n=>n.Id==leftOutput).Type.Width!=right.Nodes.Single(n=>n.Id==rightOutput).Type.Width)
            return Failure(EquivalenceStatus.Unsupported,"Input names/widths and output widths must match exactly.");
        if(cancellationToken.IsCancellationRequested)return Failure(EquivalenceStatus.Cancelled,"Caller cancelled before solving.");
        try
        {
            string version=Microsoft.Z3.Version.FullVersion;
            if(Microsoft.Z3.Version.Major!=4||Microsoft.Z3.Version.Minor!=12||Microsoft.Z3.Version.Build!=2)
                return Failure(EquivalenceStatus.Unavailable,"Expected native Z3 4.12.2; found "+version);
            using var context=new Context();using var solver=context.MkSolver("QF_BV");
            using var parameters=context.MkParams();parameters.Add("timeout",(uint)timeoutMilliseconds);
            if(resourceLimit!=0)parameters.Add("rlimit",resourceLimit);solver.Parameters=parameters;
            // Canonical generated symbols prevent names from injecting SMT syntax or
            // aliasing native symbols; user names remain only in the ordinal mapping.
            var inputs=li.Select((n,i)=>(Name:n.Name!,Expression:context.MkBVConst("input_"+i,(uint)n.Type.Width)))
                .ToDictionary(p=>p.Name,p=>p.Expression,StringComparer.Ordinal);
            var l=Encode(context,left,inputs);var r=Encode(context,right,inputs);
            solver.Assert(context.MkNot(context.MkEq(l[leftOutput],r[rightOutput])));
            string smt="(set-logic QF_BV)\n(set-option :produce-models true)\n"+
                $"(set-option :timeout {timeoutMilliseconds})\n"+(resourceLimit==0?"":$"(set-option :rlimit {resourceLimit})\n")+
                solver+"\n(check-sat)\n";
            using var cancellation=cancellationToken.Register(context.Interrupt);
            Status answer=solver.Check();
            var empty=new ReadOnlyDictionary<string,string>(new Dictionary<string,string>());
            if(cancellationToken.IsCancellationRequested)return new(EquivalenceStatus.Cancelled,"Caller cancelled.",empty,smt,version);
            if(answer==Status.UNKNOWN)return new(EquivalenceStatus.Unknown,solver.ReasonUnknown,empty,smt,version);
            if(answer==Status.UNSATISFIABLE)return new(EquivalenceStatus.Equivalent,"UNSAT: no differing input valuation.",empty,smt,version);
            using var model=solver.Model;
            var valuation=inputs.ToDictionary(p=>p.Key,p=>((BitVecNum)model.Evaluate(p.Value,true)).BigInteger,StringComparer.Ordinal);
            var lv=HardwareGraphEvaluator.Evaluate(left,li.ToDictionary(n=>n.Id,n=>valuation[n.Name!]));
            var rv=HardwareGraphEvaluator.Evaluate(right,ri.ToDictionary(n=>n.Id,n=>valuation[n.Name!]));
            if(lv[leftOutput]==rv[rightOutput])return new(EquivalenceStatus.Unknown,"Solver valuation failed independent Core replay.",empty,smt,version);
            return new(EquivalenceStatus.Different,"SAT: differing valuation independently replayed.",
                new ReadOnlyDictionary<string,string>(valuation.ToDictionary(p=>p.Key,p=>p.Value.ToString(CultureInfo.InvariantCulture),StringComparer.Ordinal)),
                smt+"(get-model)\n",version);
        }
        catch(DllNotFoundException ex){return Failure(EquivalenceStatus.Unavailable,ex.Message);}
        catch(BadImageFormatException ex){return Failure(EquivalenceStatus.Unavailable,ex.Message);}
        catch(EntryPointNotFoundException ex){return Failure(EquivalenceStatus.Unavailable,ex.Message);}
    }

    private static string? Validate(HardwareGraph graph,int output)
    {
        if(graph.Nodes.Count is <1 or >256||!graph.Nodes.Any(n=>n.Id==output))return "Expected 1..256 nodes and an existing output ID.";
        if(graph.StateInputNodeIds.Count!=0)return "State-input boundaries are unsupported: this is combinational equivalence.";
        var names=new HashSet<string>(StringComparer.Ordinal);int bits=0;
        foreach(var n in graph.Nodes)
        {
            if(n.Type.Width>32)return $"Node {n.Id}: width {n.Type.Width} exceeds 32-bit profile.";
            if(n.Operation is not (HardwareOperation.Input or HardwareOperation.Constant or HardwareOperation.Add or HardwareOperation.Subtract or
                HardwareOperation.Multiply or HardwareOperation.And or HardwareOperation.Or or HardwareOperation.Xor or HardwareOperation.Not or
                HardwareOperation.Equal or HardwareOperation.LessThan or HardwareOperation.SignedLessThan or HardwareOperation.Select or
                HardwareOperation.ShiftLeft or HardwareOperation.ShiftRight or HardwareOperation.ArithmeticShiftRight or HardwareOperation.Slice or
                HardwareOperation.Resize or HardwareOperation.SignExtend or HardwareOperation.Concat))
                return $"Node {n.Id}: {n.Operation} is unsupported by the combinational profile.";
            if(n.Operation==HardwareOperation.Input)
            {
                if(string.IsNullOrWhiteSpace(n.Name)||n.Name.Length>64||!names.Add(n.Name)||!(char.IsAsciiLetter(n.Name[0])||n.Name[0]=='_')||
                    n.Name.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='_'))return $"Node {n.Id}: inputs need unique 1..64-character ASCII identifier names.";
                bits+=n.Type.Width;
            }
        }
        return bits>128?"Total input width exceeds 128 bits.":null;
    }

    private static Dictionary<int,BitVecExpr> Encode(Context c,HardwareGraph graph,IReadOnlyDictionary<string,BitVecExpr> inputs)
    {
        var nodes=graph.Nodes.ToDictionary(n=>n.Id);var values=new Dictionary<int,BitVecExpr>();
        foreach(var n in graph.Nodes)
        {
            uint width=(uint)n.Type.Width;
            BitVecExpr A(int i)=>values[n.Operands[i]];
            BitVecExpr Literal(BigInteger v)=>c.MkBV(v.ToString(CultureInfo.InvariantCulture),width);
            BitVecExpr Boolean(BoolExpr b)=>(BitVecExpr)c.MkITE(b,c.MkBV(1,1),c.MkBV(0,1));
            int sourceWidth=n.Operands.Count==0?0:nodes[n.Operands[0]].Type.Width;
            values[n.Id]=n.Operation switch
            {
                HardwareOperation.Input=>inputs[n.Name!],HardwareOperation.Constant=>Literal(n.ConstantValue!.Value),
                HardwareOperation.Add=>c.MkBVAdd(A(0),A(1)),HardwareOperation.Subtract=>c.MkBVSub(A(0),A(1)),
                HardwareOperation.Multiply=>c.MkBVMul(A(0),A(1)),HardwareOperation.And=>c.MkBVAND(A(0),A(1)),
                HardwareOperation.Or=>c.MkBVOR(A(0),A(1)),HardwareOperation.Xor=>c.MkBVXOR(A(0),A(1)),
                HardwareOperation.Not=>c.MkBVNot(A(0)),HardwareOperation.Equal=>Boolean(c.MkEq(A(0),A(1))),
                HardwareOperation.LessThan=>Boolean(c.MkBVULT(A(0),A(1))),HardwareOperation.SignedLessThan=>Boolean(c.MkBVSLT(A(0),A(1))),
                HardwareOperation.Select=>(BitVecExpr)c.MkITE(c.MkEq(A(0),c.MkBV(1,1)),A(1),A(2)),
                // SMT shift counts have operand width. Oversized counts must never
                // wrap when converted to that width (especially one-bit values).
                HardwareOperation.ShiftLeft=>n.Offset>=n.Type.Width?Literal(0):c.MkBVSHL(A(0),Literal(n.Offset)),
                HardwareOperation.ShiftRight=>n.Offset>=n.Type.Width?Literal(0):c.MkBVLSHR(A(0),Literal(n.Offset)),
                HardwareOperation.ArithmeticShiftRight=>c.MkBVASHR(A(0),Literal(Math.Min(n.Offset,n.Type.Width-1))),
                HardwareOperation.Slice=>c.MkExtract((uint)(n.Offset+n.Type.Width-1),(uint)n.Offset,A(0)),
                HardwareOperation.Resize=>n.Type.Width<sourceWidth?c.MkExtract(width-1,0,A(0)):
                    n.Type.Width>sourceWidth?c.MkZeroExt(width-(uint)sourceWidth,A(0)):A(0),
                HardwareOperation.SignExtend=>n.Type.Width==sourceWidth?A(0):c.MkSignExt(width-(uint)sourceWidth,A(0)),
                HardwareOperation.Concat=>c.MkConcat(A(0),A(1)),
                _=>throw new NotSupportedException($"Unexpected operation {n.Operation}; preflight must reject it.")
            };
        }
        return values;
    }
}
