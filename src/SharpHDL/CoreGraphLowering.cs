using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SharpHDL.Core;

namespace SharpHDL
{
    /// <summary>
    /// Immutable snapshot of a supported legacy Module lowered to Core IR.
    /// Register state is represented as an input; NextState expressions are
    /// evaluated from the old state and committed simultaneously by a caller.
    /// This is an explicit compatibility bridge, not a second hardware model.
    /// </summary>
    public sealed class CoreGraphExport
    {
        /// <summary>The validated backend-neutral combinational graph.</summary>
        public HardwareGraph Graph { get; }
        /// <summary>Input ports and pre-edge register state mapped to node IDs.</summary>
        public IReadOnlyDictionary<string,int> Inputs { get; }
        /// <summary>Observable output ports mapped to node IDs.</summary>
        public IReadOnlyDictionary<string,int> Outputs { get; }
        /// <summary>Next-state expressions for every register mapped to node IDs.</summary>
        public IReadOnlyDictionary<string,int> NextState { get; }
        /// <summary>RAM ports and initial words. Writes commit after all pre-edge reads.</summary>
        public IReadOnlyDictionary<string,HardwareMemoryPort> Memories { get; }
        /// <summary>Register reset values, with exact hardware widths.</summary>
        public IReadOnlyDictionary<string,BigInteger> ResetValues { get; }

        internal CoreGraphExport(HardwareGraph graph,Dictionary<string,int> inputs,
            Dictionary<string,int> outputs,Dictionary<string,int> next,
            Dictionary<string,BigInteger> reset,Dictionary<string,HardwareMemoryPort> memories)
        {
            Graph=graph;
            Inputs=new System.Collections.ObjectModel.ReadOnlyDictionary<string,int>(inputs);
            Outputs=new System.Collections.ObjectModel.ReadOnlyDictionary<string,int>(outputs);
            NextState=new System.Collections.ObjectModel.ReadOnlyDictionary<string,int>(next);
            ResetValues=new System.Collections.ObjectModel.ReadOnlyDictionary<string,BigInteger>(reset);
            Memories=new System.Collections.ObjectModel.ReadOnlyDictionary<string,HardwareMemoryPort>(memories);
        }
    }

    /// <summary>
    /// Lowers the supported legacy expression subset to the canonical Core IR.
    /// Unsupported operations fail explicitly instead of silently changing RTL.
    /// </summary>
    internal static class CoreGraphLowering
    {
        internal static CoreGraphExport Export(Module module)
        {
            if(module==null)throw new ArgumentNullException(nameof(module));
            module.Validate();
            var nodes=new List<HardwareNode>();
            var ids=new Dictionary<Expr,int>();
            var inputs=new Dictionary<string,int>(StringComparer.Ordinal);
            var outputs=new Dictionary<string,int>(StringComparer.Ordinal);
            var next=new Dictionary<string,int>(StringComparer.Ordinal);
            var reset=new Dictionary<string,BigInteger>(StringComparer.Ordinal);
            var memories=new Dictionary<string,HardwareMemoryPort>(StringComparer.Ordinal);
            int Add(Expr expression,HardwareOperation operation,int[] operands,
                BigInteger? literal=null,string name=null,int offset=0,
                IReadOnlyList<BigInteger> table=null,string memoryName=null)
            {
                int id=nodes.Count;
                nodes.Add(new HardwareNode(id,operation,new HardwareType(expression.Width),
                    operands,literal,name,offset,table,memoryName));
                ids.Add(expression,id);
                return id;
            }

            // Allocate state boundary nodes first. Register feedback is not a
            // combinational dependency; this keeps all IR operands topological.
            foreach(var signal in module.Signals.Where(s=>s.Kind=="input"||s.Kind=="reg"))
            {
                int id=Add(signal,HardwareOperation.Input,Array.Empty<int>(),null,signal.Name);
                inputs.Add(signal.Name,id);
                if(signal.Kind=="reg")reset.Add(signal.Name,signal.ResetValue);
            }

            int Visit(Expr e)
            {
                if(ids.TryGetValue(e,out var id))return id;
                if(e is Signal signal)
                {
                    if(signal.Owner!=module)
                        throw new InvalidOperationException("Foreign signal in Core IR lowering.");
                    if(signal.Driver==null)
                        throw new InvalidOperationException("Undriven signal: "+signal.Name);
                    id=Visit(signal.Driver);
                    ids.Add(e,id);
                    return id;
                }
                if(e.Op=="const")
                    return Add(e,HardwareOperation.Constant,Array.Empty<int>(),e.Literal);
                HardwareOperation op;
                switch(e.Op)
                {
                    case "+": op=HardwareOperation.Add; break;
                    case "-": op=HardwareOperation.Subtract; break;
                    case "*": op=HardwareOperation.Multiply; break;
                    case "dsp48e2": op=HardwareOperation.DspMultiplyUnsigned; break;
                    case "&": op=HardwareOperation.And; break;
                    case "|": op=HardwareOperation.Or; break;
                    case "^": op=HardwareOperation.Xor; break;
                    case "~": op=HardwareOperation.Not; break;
                    case "mux": op=HardwareOperation.Select; break;
                    case "<<": op=HardwareOperation.ShiftLeft; break;
                    case ">>": op=HardwareOperation.ShiftRight; break;
                    case "asr": op=HardwareOperation.ArithmeticShiftRight; break;
                    case "slice": op=HardwareOperation.Slice; break;
                    case "resize": op=HardwareOperation.Resize; break;
                    case "signextend": op=HardwareOperation.SignExtend; break;
                    case "concat": op=HardwareOperation.Concat; break;
                    case "==": op=HardwareOperation.Equal; break;
                    case "<": op=HardwareOperation.LessThan; break;
                    case "slt": op=HardwareOperation.SignedLessThan; break;
                    case "lookup": op=HardwareOperation.Lookup; break;
                    case "memory": op=HardwareOperation.MemoryRead; break;
                    default:
                        throw new NotSupportedException("Core IR lowering does not yet support expression '"+
                            e.Op+"' ("+e.Width+" bits).");
                }
                var operands=e.Args.Select(Visit).ToArray();
                return Add(e,op,operands,null,e.NameHint,e.Offset,
                    e.Table==null?null:Array.AsReadOnly((BigInteger[])e.Table.Clone()),
                    e.Memory==null?null:e.Memory.Name);
            }

            foreach(var signal in module.Signals.Where(s=>s.Kind=="output"))
                outputs.Add(signal.Name,Visit(signal.Driver));
            foreach(var signal in module.Signals.Where(s=>s.Kind=="reg"))
                next.Add(signal.Name,Visit(signal.Driver));
            // RAM writes use pre-edge operands, and commit after reads and state evaluation.
            foreach(var ram in module.Memories)
                memories.Add(ram.Name,new HardwareMemoryPort(ram.Name,ram.Width,
                    Visit(ram.Address),Visit(ram.WriteEnable),Visit(ram.WriteData),ram.Initial));
            return new CoreGraphExport(new HardwareGraph(nodes,inputs.Where(pair=>reset.ContainsKey(pair.Key)).Select(pair=>pair.Value)),inputs,outputs,next,reset,memories);
        }
    }
}
