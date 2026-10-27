using System.Reflection;
using System.Reflection.Emit;
using SharpHDL.HLS;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Graph contracts and a separate transitive-closure oracle for the library adapter.</summary>
public sealed class InstructionControlFlowTests
{
    private static Dictionary<int, IReadOnlyList<int>> Graph(params int[][] edges) =>
        edges.Select((targets, vertex) => (targets, vertex))
            .ToDictionary(pair => pair.vertex, pair => (IReadOnlyList<int>)pair.targets);

    /// <summary>Diamond reachability excludes an unreachable cycle without losing its component.</summary>
    [Fact]
    public void Diamond_and_unreachable_cycle_have_distinct_analysis()
    {
        var flow = InstructionControlFlow.Analyze(Graph([1, 2], [3], [3], [], [5], [4]));
        Assert.Equal(new[] { 0, 1, 2, 3 }, flow.ReachableOffsets);
        Assert.Equal(new[] { "0", "1", "2", "3", "4,5" }, Groups(flow.StrongComponents));
        Assert.Empty(flow.LoopComponents);
        Assert.False(flow.HasReachableLoops);
    }

    /// <summary>Multi-node cycles and singleton self edges are loops only when reachable.</summary>
    [Fact]
    public void Reachable_cycles_and_nonzero_entry_are_reported()
    {
        var flow = InstructionControlFlow.Analyze(Graph([], [2], [1, 3], [3]), 1);
        Assert.Equal(1, flow.EntryOffset);
        Assert.Equal(new[] { 1, 2, 3 }, flow.ReachableOffsets);
        Assert.Equal(new[] { "1,2", "3" }, Groups(flow.LoopComponents));
        Assert.True(flow.HasReachableLoops);
    }

    /// <summary>Input order and duplicate edges have no observable effect.</summary>
    [Fact]
    public void Canonical_order_is_independent_of_insertion_order()
    {
        var first = Graph([2, 1, 2], [3], [3, 2], []);
        var reversed = first.Reverse().ToDictionary(pair => pair.Key,
            pair => (IReadOnlyList<int>)pair.Value.Reverse().ToArray());
        var a = InstructionControlFlow.Analyze(first);
        var b = InstructionControlFlow.Analyze(reversed);
        Assert.Equal(a.Successors.Keys, b.Successors.Keys);
        foreach (int key in a.Successors.Keys) Assert.Equal(a.Successors[key], b.Successors[key]);
        Assert.Equal(new[] { 1, 2 }, a.Successors[0]);
        Assert.Equal(a.ReachableOffsets, b.ReachableOffsets);
        Assert.Equal(Groups(a.StrongComponents), Groups(b.StrongComponents));
        Assert.Equal(Groups(a.LoopComponents), Groups(b.LoopComponents));
    }

    /// <summary>Result owns deeply read-only snapshots, including nested component lists.</summary>
    [Fact]
    public void Snapshots_survive_input_mutation_and_reject_output_mutation()
    {
        int[] edge = [1];
        var input = Graph(edge, [1]);
        var flow = InstructionControlFlow.Analyze(input);
        edge[0] = 0; input.Clear();
        Assert.Equal(new[] { 1 }, flow.Successors[0]);
        Assert.Equal(new[] { 0, 1 }, flow.ReachableOffsets);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<int, IReadOnlyList<int>>)flow.Successors).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<int>)flow.Successors[0])[0] = 0);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)flow.ReachableOffsets).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<IReadOnlyList<int>>)flow.StrongComponents).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<int>)flow.StrongComponents[0]).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<IReadOnlyList<int>>)flow.LoopComponents).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<int>)flow.LoopComponents[0]).Clear());
    }

    /// <summary>Invalid graph contracts fail before any algorithm is run.</summary>
    [Fact]
    public void Malformed_inputs_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => InstructionControlFlow.Analyze(null!));
        Assert.Contains("1..4096", Assert.Throws<ArgumentException>(() => InstructionControlFlow.Analyze(Graph())).Message);
        foreach(int entry in new[] { -1, 1 })
        {
            var error = Assert.Throws<ArgumentException>(() => InstructionControlFlow.Analyze(Graph(Array.Empty<int>()), entry));
            Assert.Equal("entryOffset", error.ParamName);
            Assert.Contains("Entry offset", error.Message);
        }
        Assert.Throws<ArgumentException>(() => InstructionControlFlow.Analyze(new Dictionary<int, IReadOnlyList<int>> { [0] = [], [-1] = [] }));
        Assert.Contains("successor lists non-null", Assert.Throws<ArgumentException>(() => InstructionControlFlow.Analyze(new Dictionary<int, IReadOnlyList<int>> { [0] = null! })).Message);
        var absent = Assert.Throws<ArgumentException>(() => InstructionControlFlow.Analyze(Graph([4])));
        Assert.Contains("0 targets undeclared offset 4", absent.Message);
        Assert.Throws<ArgumentException>(() => InstructionControlFlow.Analyze(Graph([-1])));
    }

    /// <summary>Exact resource boundaries are accepted; one beyond is rejected.</summary>
    [Fact]
    public void Resource_limits_are_bounded_and_inclusive()
    {
        var chain = Enumerable.Range(0, InstructionControlFlow.MaxNodes).ToDictionary(i => i,
            i => (IReadOnlyList<int>)(i + 1 == InstructionControlFlow.MaxNodes ? [] : new[] { i + 1 }));
        var flow = InstructionControlFlow.Analyze(chain);
        Assert.Equal(InstructionControlFlow.MaxNodes, flow.ReachableOffsets.Count);
        Assert.Equal(InstructionControlFlow.MaxNodes, flow.StrongComponents.Count);
        Assert.False(flow.HasReachableLoops);
        chain.Add(InstructionControlFlow.MaxNodes, []);
        Assert.Contains("1..4096", Assert.Throws<ArgumentException>(() => InstructionControlFlow.Analyze(chain)).Message);
        var duplicates = Graph(new int[InstructionControlFlow.MaxEdges]);
        Assert.Equal(new[] { 0 }, InstructionControlFlow.Analyze(duplicates).Successors[0]);
        Assert.True(InstructionControlFlow.Analyze(duplicates).HasReachableLoops);
        duplicates[0] = new int[InstructionControlFlow.MaxEdges + 1];
        Assert.Contains("65536 edge entries", Assert.Throws<ArgumentException>(() => InstructionControlFlow.Analyze(duplicates)).Message);
    }

    /// <summary>
    /// Floyd-Warshall closure supplies an independent oracle for random graphs of
    /// up to eight nodes; identity paths count for reachability, but not as cycles.
    /// FsCheck retains seed and shrinks any counterexample.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 200, Replay = "1145655947,296144285")]
    public void Random_graphs_match_independent_transitive_closure(ulong bits, byte size, byte entry)
    {
        int count = 1 + size % 8, root = entry % count;
        var input = new Dictionary<int, IReadOnlyList<int>>();
        var closure = new bool[count, count];
        for (int i = 0; i < count; i++)
        {
            var targets = new List<int>();
            for (int j = 0; j < count; j++)
                if ((bits & (1UL << (i * count + j))) != 0) { targets.Add(j); closure[i, j] = true; }
            input.Add(i, targets);
            closure[i, i] = true;
        }
        for (int k = 0; k < count; k++)
            for (int i = 0; i < count; i++)
                for (int j = 0; j < count; j++) closure[i, j] |= closure[i, k] && closure[k, j];
        var expectedComponents = new List<IReadOnlyList<int>>();
        var assigned = new HashSet<int>();
        for (int i = 0; i < count; i++)
            if (!assigned.Contains(i))
            {
                int[] members = Enumerable.Range(0, count).Where(j => closure[i, j] && closure[j, i]).ToArray();
                expectedComponents.Add(members);
                assigned.UnionWith(members);
            }
        var expectedLoops = expectedComponents.Where(c => closure[root, c[0]] &&
            (c.Count > 1 || input[c[0]].Contains(c[0]))).ToArray();
        var actual = InstructionControlFlow.Analyze(input, root);
        Assert.Equal(Enumerable.Range(0, count).Where(i => closure[root, i]), actual.ReachableOffsets);
        Assert.Equal(Groups(expectedComponents), Groups(actual.StrongComponents));
        Assert.Equal(Groups(expectedLoops), Groups(actual.LoopComponents));
        Assert.Equal(expectedLoops.Length != 0, actual.HasReachableLoops);
    }

    /// <summary>Compiler graph describes original reachable instructions without adding wait/idle states.</summary>
    [Fact]
    public void Scheduled_compilation_exposes_actual_instruction_flow()
    {
        var loop = CilCompiler.BuildScheduledSequential(typeof(Kernels).GetMethod(nameof(Kernels.Loop))!, "FlowLoop");
        Assert.True(loop.ControlFlow.HasReachableLoops);
        Assert.Equal(loop.StateOffsets.Values.Distinct().OrderBy(i => i), loop.ControlFlow.ReachableOffsets);
        var straight = CilCompiler.BuildScheduledSequential(typeof(Kernels).GetMethod(nameof(Kernels.Straight))!, "FlowStraight");
        Assert.False(straight.ControlFlow.HasReachableLoops);
        Assert.Equal(straight.StateOffsets.Values.Distinct().OrderBy(i => i), straight.ControlFlow.ReachableOffsets);
    }

    /// <summary>Targets into an operand or past the body are rejected at the branch, even in unreachable code.</summary>
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)]
    public void Malformed_instruction_boundaries_report_origin(bool pastEnd, bool unreachable)
    {
        var type = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("FlowBad" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect)
            .DefineDynamicModule("main").DefineType("Kernel", TypeAttributes.Public);
        var method = type.DefineMethod("Bad", MethodAttributes.Public | MethodAttributes.Static, typeof(uint), Type.EmptyTypes);
        var il = method.GetILGenerator();
        if (unreachable) { il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret); }
        int origin = il.ILOffset;
        // br.s occupies two bytes; -1 points into its own operand rather than an instruction.
        il.Emit(OpCodes.Br_S, pastEnd ? (sbyte)127 : (sbyte)-1);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
        var error = Assert.Throws<NotSupportedException>(() => CilCompiler.BuildScheduledSequential(type.CreateType()!.GetMethod("Bad")!, "BadFlow"));
        var diagnostic = CilDiagnostic.FromException(error)!;
        Assert.Equal(origin, diagnostic.IlOffset);
        Assert.Contains("instruction boundary", diagnostic.Reason);
    }

    private static class Kernels
    {
        // Runtime-bounded loop fixture with an actual backwards CIL edge.
        public static uint Loop(uint count) { uint sum = 0; while (count != 0) { sum += count; count--; } return sum; }
        // Acyclic branch fixture.
        public static uint Straight(uint value) => value == 0 ? 7U : value + 1;
    }

    private static string[] Groups(IEnumerable<IReadOnlyList<int>> groups) => groups.Select(c => string.Join(",", c)).ToArray();
}
