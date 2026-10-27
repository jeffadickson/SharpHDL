using System.Collections.ObjectModel;
using QuikGraph;
using QuikGraph.Algorithms.ConnectedComponents;
using QuikGraph.Algorithms.Search;

namespace SharpHDL.HLS;

/// <summary>
/// Immutable instruction-flow analysis with canonical ordering, independent of
/// dictionary insertion order. Edges describe possible control flow, not timing
/// or a proof that any path terminates or is feasible for particular data.
/// </summary>
public sealed class InstructionControlFlow
{
    /// <summary>Maximum instruction vertices accepted by this bounded analysis.</summary>
    public const int MaxNodes = 4096;
    /// <summary>Maximum input edge entries, including duplicates before normalization.</summary>
    public const int MaxEdges = 65536;

    private InstructionControlFlow(int entryOffset,
        SortedDictionary<int, IReadOnlyList<int>> successors, int[] reachable,
        IReadOnlyList<int>[] components, IReadOnlyList<int>[] loops)
    {
        EntryOffset = entryOffset;
        Successors = new ReadOnlyDictionary<int, IReadOnlyList<int>>(successors);
        ReachableOffsets = Array.AsReadOnly(reachable);
        StrongComponents = Array.AsReadOnly(components);
        LoopComponents = Array.AsReadOnly(loops);
    }

    /// <summary>Entry instruction's original nonnegative IL byte offset.</summary>
    public int EntryOffset { get; }
    /// <summary>All vertices in ascending order, with sorted distinct successor snapshots.</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<int>> Successors { get; }
    /// <summary>Offsets reachable from entry in ascending order, including entry itself.</summary>
    public IReadOnlyList<int> ReachableOffsets { get; }
    /// <summary>All strongly connected components, sorted by smallest member with sorted members.</summary>
    public IReadOnlyList<IReadOnlyList<int>> StrongComponents { get; }
    /// <summary>Reachable cyclic components, including singleton self-loops, in canonical order.</summary>
    public IReadOnlyList<IReadOnlyList<int>> LoopComponents { get; }
    /// <summary>Whether a reachable cycle exists; does not imply nontermination for runtime data.</summary>
    public bool HasReachableLoops => LoopComponents.Count != 0;

    /// <summary>
    /// Validate and snapshot a directed instruction graph, then compute reachability
    /// and components with QuikGraph. Every target must name a declared vertex.
    /// Input collections must remain stable for the duration of this call; later
    /// caller mutations cannot affect the result.
    /// </summary>
    /// <exception cref="ArgumentNullException">The graph is null.</exception>
    /// <exception cref="ArgumentException">The graph is malformed or exceeds the documented limits.</exception>
    public static InstructionControlFlow Analyze(
        IReadOnlyDictionary<int, IReadOnlyList<int>> successors, int entryOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(successors);
        if (successors.Count == 0 || successors.Count > MaxNodes)
            throw new ArgumentException($"Instruction graph must contain 1..{MaxNodes} vertices.", nameof(successors));
        if (entryOffset < 0 || !successors.ContainsKey(entryOffset))
            throw new ArgumentException("Entry offset must name a nonnegative instruction vertex.", nameof(entryOffset));

        // Bound raw input before distinct/sort allocation. Duplicate switch targets
        // still cost input work and therefore count against the edge-entry budget.
        long edgeCount = 0;
        foreach (var pair in successors)
        {
            if (pair.Key < 0 || pair.Value is null)
                throw new ArgumentException("Instruction offsets must be nonnegative and successor lists non-null.", nameof(successors));
            edgeCount += pair.Value.Count;
            if (edgeCount > MaxEdges)
                throw new ArgumentException($"Instruction graph exceeds {MaxEdges} edge entries.", nameof(successors));
        }
        var snapshot = new SortedDictionary<int, IReadOnlyList<int>>();
        foreach (var pair in successors)
        {
            var targets = pair.Value.Distinct().OrderBy(offset => offset).ToArray();
            foreach (int target in targets)
                if (!successors.ContainsKey(target))
                    throw new ArgumentException($"Instruction {pair.Key} targets undeclared offset {target}.", nameof(successors));
            snapshot.Add(pair.Key, Array.AsReadOnly(targets));
        }

        // Library traversal order is deliberately not part of the public API.
        // Canonicalize input and output so library component numbering cannot
        // change hardware state encodings or serialized analysis results.
        var graph = new AdjacencyGraph<int, Edge<int>>(false);
        graph.AddVertexRange(snapshot.Keys);
        foreach (var pair in snapshot)
            foreach (int target in pair.Value)
                graph.AddEdge(new Edge<int>(pair.Key, target));
        var reachable = new SortedSet<int>();
        var search = new BreadthFirstSearchAlgorithm<int, Edge<int>>(graph);
        search.DiscoverVertex += vertex => reachable.Add(vertex);
        search.Compute(entryOffset);
        var scc = new StronglyConnectedComponentsAlgorithm<int, Edge<int>>(graph);
        scc.Compute();
        var components = scc.Components.GroupBy(pair => pair.Value)
            .Select(group => (IReadOnlyList<int>)Array.AsReadOnly(group.Select(pair => pair.Key).OrderBy(offset => offset).ToArray()))
            .OrderBy(component => component[0]).ToArray();
        var loops = components.Where(component => reachable.Contains(component[0]) &&
            (component.Count > 1 || snapshot[component[0]].Contains(component[0]))).ToArray();
        return new InstructionControlFlow(entryOffset, snapshot, reachable.ToArray(), components, loops);
    }
}
