using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Linq;
using System.Numerics;

namespace SharpHDL.Core;

/// <summary>Validated, immutable, backend-neutral directed acyclic combinational IR.
/// Nodes are ordered topologically; a Register node denotes a state boundary,
/// rather than a combinational dependency. This is an incremental IR foundation.</summary>
public sealed class HardwareGraph
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion => CurrentSchemaVersion;
    public IReadOnlyList<HardwareNode> Nodes { get; }
    /// <summary>IDs of input-shaped nodes that represent pre-edge register state.</summary>
    public IReadOnlySet<int> StateInputNodeIds { get; }

    public HardwareGraph(IEnumerable<HardwareNode> nodes,IEnumerable<int>? stateInputNodeIds=null)
    {
        if (nodes == null) throw new ArgumentNullException(nameof(nodes));
        var list = nodes.ToArray();
        var known = new Dictionary<int, HardwareNode>();
        foreach (var node in list)
        {
            if (node == null) throw new ArgumentException("Null IR node.");
            node.Type.Validate();
            if (node.Id < 0 || known.ContainsKey(node.Id))
                throw new ArgumentException("Node IDs must be unique nonnegative integers.");
            if (node.Operands == null) throw new ArgumentException("Missing node operands.");
            foreach (var id in node.Operands)
                if (!known.ContainsKey(id))
                    throw new ArgumentException($"Node {node.Id} references unknown or non-topological operand {id}.");
            var args = node.Operands.Select(id => known[id]).ToArray();
            int width = node.Type.Width;
            switch (node.Operation)
            {
                case HardwareOperation.Constant:
                    Require(args.Length == 0 && node.ConstantValue.HasValue,node,"constant requires a literal");
                    Require(node.ConstantValue!.Value >= 0 &&
                        node.ConstantValue.Value < (BigInteger.One << width),node,"literal out of range");
                    break;
                case HardwareOperation.Input:
                    Require(args.Length == 0,node,"input has no operands");break;
                case HardwareOperation.Not:
                case HardwareOperation.Register:
                    Require(args.Length == 1 && args[0].Type.Width == width,node,"unary width mismatch");break;
                case HardwareOperation.Add:
                case HardwareOperation.Subtract:
                case HardwareOperation.Multiply:
                case HardwareOperation.And:
                case HardwareOperation.Or:
                case HardwareOperation.Xor:
                    Require(args.Length == 2 && args.All(a => a.Type.Width == width),node,"binary width mismatch");break;
                case HardwareOperation.DspMultiplyUnsigned:
                    Require(args.Length == 2 && args[0].Type.Width <= 26 &&
                        args[1].Type.Width <= 17 &&
                        width == args[0].Type.Width + args[1].Type.Width,
                        node,"DSP48E2 unsigned operand/result widths invalid");break;
                case HardwareOperation.Equal:
                case HardwareOperation.LessThan:
                case HardwareOperation.SignedLessThan:
                    Require(args.Length == 2 && args[0].Type.Width == args[1].Type.Width &&
                        width == 1,node,"comparison width mismatch");break;
                case HardwareOperation.Select:
                    Require(args.Length == 3 && args[0].Type.Width == 1 &&
                        args[1].Type.Width == width && args[2].Type.Width == width,node,"mux width mismatch");break;
                case HardwareOperation.ShiftLeft:
                case HardwareOperation.ShiftRight:
                case HardwareOperation.ArithmeticShiftRight:
                    Require(args.Length == 1 && args[0].Type.Width == width &&
                        node.Offset >= 0,node,"constant shift width/offset invalid");break;
                case HardwareOperation.Slice:
                    Require(args.Length == 1 && node.Offset >= 0 &&
                        (long)node.Offset + width <= args[0].Type.Width,node,"slice range invalid");break;
                case HardwareOperation.Resize:
                    Require(args.Length == 1,node,"resize requires one operand");break;
                case HardwareOperation.SignExtend:
                    Require(args.Length == 1 && width >= args[0].Type.Width,
                        node,"sign extension cannot narrow");break;
                case HardwareOperation.Concat:
                    Require(args.Length == 2 &&
                        (long)args[0].Type.Width + args[1].Type.Width == width,
                        node,"concat width mismatch");break;
                case HardwareOperation.Lookup:
                    Require(args.Length == 1 && node.Table != null &&
                        node.Table.Count > 0 && node.Table.Count <= 65536 &&
                        new BigInteger(node.Table.Count) <= (BigInteger.One << args[0].Type.Width) &&
                        node.Table.All(v => v >= 0 && v < (BigInteger.One << width)),
                        node,"ROM table/address/element invalid");break;
                case HardwareOperation.MemoryRead:
                    Require(args.Length == 1 && !string.IsNullOrWhiteSpace(node.MemoryName),
                        node,"RAM read requires address and memory identifier");break;
                default:
                    throw new NotSupportedException($"Operation {node.Operation} is not yet supported by the version 1 graph validator.");
            }
            known.Add(node.Id, node);
        }
        var stateIds=(stateInputNodeIds ?? Array.Empty<int>()).ToHashSet();
        if(stateIds.Any(id=>!known.TryGetValue(id,out var n) || n.Operation!=HardwareOperation.Input))
            throw new ArgumentException("State boundaries must reference existing input nodes.",nameof(stateInputNodeIds));
        StateInputNodeIds=stateIds.ToFrozenSet();
        Nodes = Array.AsReadOnly(list.Select(n =>
            n with { Operands = Array.AsReadOnly(n.Operands.ToArray()),
                Table = n.Table == null ? null : Array.AsReadOnly(n.Table.ToArray()) }).ToArray());
    }

    private static void Require(bool ok, HardwareNode node, string reason)
    {
        if (!ok) throw new ArgumentException($"IR node {node.Id}: {reason}");
    }
}
