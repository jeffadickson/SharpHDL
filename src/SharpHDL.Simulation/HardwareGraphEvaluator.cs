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
                case HardwareOperation.Multiply:v=A(0)*A(1);break;
                case HardwareOperation.And:v=A(0)&A(1);break;
                case HardwareOperation.Or:v=A(0)|A(1);break;
                case HardwareOperation.Xor:v=A(0)^A(1);break;
                case HardwareOperation.Not:v=~A(0);break;
                case HardwareOperation.Select:v=A(0)!=0?A(1):A(2);break;
                default:throw new NotSupportedException(
                    $"Core IR evaluation does not support {node.Operation}.");
            }
            values.Add(node.Id,v&mask);
        }
        return new ReadOnlyDictionary<int,BigInteger>(values);
    }
}
