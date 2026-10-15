using System;
using System.Collections.Generic;
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

    public HardwareGraph(IEnumerable<HardwareNode> nodes)
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
                    Require(args.Length == 0, node, "expected no operands");
                    if (node.ConstantValue is BigInteger value &&
                        (value < 0 || value >= (BigInteger.One << width)))
                        throw new ArgumentException($"IR node {node.Id}: literal out of range");
                    break;
                case HardwareOperation.Input:
                    Require(args.Length == 0, node, "expected no operands"); break;
                case HardwareOperation.Not:
                case HardwareOperation.Register:
                    Require(args.Length == 1 && args[0].Type.Width == width, node, "unary width mismatch"); break;
                case HardwareOperation.Add:
                case HardwareOperation.Subtract:
                case HardwareOperation.Multiply:
                case HardwareOperation.And:
                case HardwareOperation.Or:
                case HardwareOperation.Xor:
                    Require(args.Length == 2 && args.All(a => a.Type.Width == width), node, "binary width mismatch"); break;
                case HardwareOperation.Select:
                    Require(args.Length == 3 && args[0].Type.Width == 1 &&
                            args[1].Type.Width == width && args[2].Type.Width == width, node, "mux width mismatch"); break;
                default:
                    throw new NotSupportedException($"Operation {node.Operation} is not yet supported by the version 1 graph validator.");
            }
            known.Add(node.Id, node);
        }
        Nodes = Array.AsReadOnly(list.Select(n =>
            n with { Operands = Array.AsReadOnly(n.Operands.ToArray()) }).ToArray());
    }

    private static void Require(bool ok, HardwareNode node, string reason)
    {
        if (!ok) throw new ArgumentException($"IR node {node.Id}: {reason}");
    }
}
