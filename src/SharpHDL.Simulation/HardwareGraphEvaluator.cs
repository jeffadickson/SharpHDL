using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Numerics;
using SharpHDL.Core;

namespace SharpHDL.Simulation;

/// <summary>
/// Deterministic two-state evaluator of a validated Core hardware graph.
/// No dependency on the legacy Module, CIL frontend or HLS scheduler.
/// Inputs include register values sampled before the active clock edge.
/// </summary>
public static class HardwareGraphEvaluator
{
    /// <summary>
    /// Evaluate each IR node once in topological order, with modular
    /// unsigned arithmetic and exact width masks. This evaluates one
    /// combinational snapshot; state commits remain the caller's concern.
    /// </summary>
    public static IReadOnlyDictionary<int,BigInteger> Evaluate(
        HardwareGraph graph,IReadOnlyDictionary<int,BigInteger> inputs)
        => Evaluate(graph,inputs,null);

    /// <summary>
    /// Evaluate with explicit pre-edge RAM snapshots. Missing RAMs are errors;
    /// enabled out-of-range reads yield zero, matching legacy semantics.
    /// No writes are performed by this evaluator.
    /// </summary>
    public static IReadOnlyDictionary<int,BigInteger> Evaluate(
        HardwareGraph graph,IReadOnlyDictionary<int,BigInteger> inputs,
        IReadOnlyDictionary<string,IReadOnlyList<BigInteger>>? memories)
    {
        if(graph==null)throw new ArgumentNullException(nameof(graph));
        if(inputs==null)throw new ArgumentNullException(nameof(inputs));
        var values=new Dictionary<int,BigInteger>();
        foreach(var node in graph.Nodes)
        {
            var mask=(BigInteger.One<<node.Type.Width)-1;
            BigInteger v;
            BigInteger A(int i)=>values[node.Operands[i]];
            switch(node.Operation)
            {
                case HardwareOperation.Input:
                    if(!inputs.TryGetValue(node.Id,out v))
                        throw new ArgumentException($"Missing input IR node {node.Id} ({node.Name}).",nameof(inputs));
                    if(v<0 || v>mask)
                        throw new ArgumentOutOfRangeException(nameof(inputs),
                            $"Input IR node {node.Id} exceeds {node.Type.Width} bits.");
                    break;
                case HardwareOperation.Constant:
                    if(node.ConstantValue is not BigInteger literal)
                        throw new ArgumentException($"Constant IR node {node.Id} lacks literal payload.");
                    v=literal;break;
                case HardwareOperation.Add:v=A(0)+A(1);break;
                case HardwareOperation.Subtract:v=A(0)-A(1);break;
                case HardwareOperation.Multiply:
                case HardwareOperation.DspMultiplyUnsigned:v=A(0)*A(1);break;
                case HardwareOperation.And:v=A(0)&A(1);break;
                case HardwareOperation.Or:v=A(0)|A(1);break;
                case HardwareOperation.Xor:v=A(0)^A(1);break;
                case HardwareOperation.Not:v=~A(0);break;
                case HardwareOperation.Select:v=A(0)!=0?A(1):A(2);break;
                case HardwareOperation.ShiftLeft:v=A(0)<<node.Offset;break;
                case HardwareOperation.ShiftRight:
                case HardwareOperation.Slice:v=A(0)>>node.Offset;break;
                case HardwareOperation.ArithmeticShiftRight:
                    v=Signed(A(0),graph.Nodes[node.Operands[0]].Type.Width)>>node.Offset;break;
                case HardwareOperation.Resize:v=A(0);break;
                case HardwareOperation.SignExtend:
                    v=Signed(A(0),graph.Nodes[node.Operands[0]].Type.Width);break;
                case HardwareOperation.Concat:
                    v=(A(0)<<graph.Nodes[node.Operands[1]].Type.Width)|A(1);break;
                case HardwareOperation.Equal:v=A(0)==A(1)?1:0;break;
                case HardwareOperation.LessThan:v=A(0)<A(1)?1:0;break;
                case HardwareOperation.SignedLessThan:
                    var argWidth=graph.Nodes[node.Operands[0]].Type.Width;
                    v=Signed(A(0),argWidth)<Signed(A(1),argWidth)?1:0;break;
                case HardwareOperation.Lookup:
                    v=A(0)<node.Table!.Count?node.Table[(int)A(0)]:BigInteger.Zero;break;
                case HardwareOperation.MemoryRead:
                    if(memories==null || node.MemoryName==null ||
                        !memories.TryGetValue(node.MemoryName,out var cells))
                        throw new ArgumentException($"Missing RAM snapshot '{node.MemoryName}'.",nameof(memories));
                    if(cells==null)throw new ArgumentException("Null RAM snapshot.",nameof(memories));
                    v=A(0)<cells.Count?cells[(int)A(0)]:BigInteger.Zero;
                    if(v<0||v>mask)
                        throw new ArgumentOutOfRangeException(nameof(memories),
                            $"RAM '{node.MemoryName}' word exceeds {node.Type.Width} bits.");
                    break;
                default:throw new NotSupportedException(
                    $"Core IR evaluation does not support {node.Operation}.");
            }
            values.Add(node.Id,v&mask);
        }
        return new ReadOnlyDictionary<int,BigInteger>(values);
    }

    private static BigInteger Signed(BigInteger value,int width)
    {
        var sign=BigInteger.One<<(width-1);
        return (value&sign)!=0?value-(BigInteger.One<<width):value;
    }
}
