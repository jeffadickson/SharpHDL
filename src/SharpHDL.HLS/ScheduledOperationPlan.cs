using System.Collections.ObjectModel;

namespace SharpHDL.HLS;

/// <summary>A one-edge operation reserving one unit of a named bank/operator resource.</summary>
public sealed class ScheduledOperation
{
    /// <summary>Create a declaration; dependency names identify values available on a later edge.</summary>
    public ScheduledOperation(string name,string resource,params string[] dependencies)
    {
        if(string.IsNullOrWhiteSpace(name)||string.IsNullOrWhiteSpace(resource))
            throw new ArgumentException("Operation and resource names are required.");
        ArgumentNullException.ThrowIfNull(dependencies);
        if(dependencies.Any(string.IsNullOrWhiteSpace))throw new ArgumentException("Dependency names are required.");
        Name=name;Resource=resource;Dependencies=Array.AsReadOnly(dependencies.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }
    /// <summary>Unique stable operation identifier, used as the arbitration priority.</summary>
    public string Name { get; }
    /// <summary>Physical single-port bank or finite-capacity operator identifier.</summary>
    public string Resource { get; }
    /// <summary>Operations that must finish on an earlier edge.</summary>
    public IReadOnlyList<string> Dependencies { get; }
}

/// <summary>
/// Deterministic, greedy static arbitration of one-edge operations. This provides
/// legal schedules, not optimal scheduling, multi-cycle operators or dynamic request arbitration.
/// Memory alias/order hazards must be expressed as dependencies by the caller.
/// </summary>
public sealed class ScheduledOperationPlan
{
    private readonly ScheduledOperation[] operations;
    private ScheduledOperationPlan(ScheduledOperation[] operations,Dictionary<string,int> cycles)
    {
        this.operations=operations;
        Cycles=new ReadOnlyDictionary<string,int>(cycles);
        CycleCount=cycles.Values.Max()+1;
    }
    /// <summary>Canonical operation names and assigned zero-based execution edges.</summary>
    public IReadOnlyDictionary<string,int> Cycles { get; }
    /// <summary>Number of execution edges before the completion state.</summary>
    public int CycleCount { get; }

    /// <summary>
    /// Plan at most 4095 operations with ordinal-name priority among ready requests.
    /// QuikGraph-backed component analysis validates the dependency graph. RAM banks
    /// should each have capacity one; arithmetic instances may declare larger capacity.
    /// </summary>
    public static ScheduledOperationPlan Create(IEnumerable<ScheduledOperation> operations,IReadOnlyDictionary<string,int> capacities)
    {
        var items=ValidateDeclarations(operations,capacities);
        var ids=items.Select((op,index)=>(op.Name,index:index+1)).ToDictionary(p=>p.Name,p=>p.index,StringComparer.Ordinal);
        var successors=Enumerable.Range(0,items.Length+1).ToDictionary(i=>i,_=>new List<int>());
        foreach(var op in items)
        {
            if(op.Dependencies.Count==0)successors[0].Add(ids[op.Name]);
            foreach(string dependency in op.Dependencies)successors[ids[dependency]].Add(ids[op.Name]);
        }
        var graph=InstructionControlFlow.Analyze(successors.ToDictionary(p=>p.Key,p=>(IReadOnlyList<int>)p.Value));
        // Check all components: a disconnected cycle is not reachable from the
        // synthetic root and must still be rejected instead of stalling planning.
        if(graph.StrongComponents.Any(c=>c.Count>1||graph.Successors[c[0]].Contains(c[0])))
            throw new ArgumentException("Operation dependencies contain a cycle.",nameof(operations));
        var assigned=new Dictionary<string,int>(StringComparer.Ordinal);
        // A legal DAG with positive capacities schedules at least one operation
        // per edge. Bound work even if an external capacity provider is inconsistent.
        for(int cycle=0;cycle<items.Length&&assigned.Count<items.Length;cycle++)
        {
            int before=assigned.Count;
            var used=new Dictionary<string,int>(StringComparer.Ordinal);
            foreach(var op in items)
                if(!assigned.ContainsKey(op.Name)&&op.Dependencies.All(d=>assigned.TryGetValue(d,out int prior)&&prior<cycle))
                {
                    used.TryGetValue(op.Resource,out int count);
                    if(count>=capacities[op.Resource])continue;
                    assigned.Add(op.Name,cycle);used[op.Resource]=count+1;
                }
            if(assigned.Count==before)throw new InvalidOperationException("Resource configuration made no scheduling progress.");
        }
        if(assigned.Count!=items.Length)throw new InvalidOperationException("Scheduling exceeded its operation-count edge budget.");
        ValidateAssignments(items,capacities,assigned);
        // Dictionary enumeration is canonical even when readiness changed order.
        return new ScheduledOperationPlan(items,items.ToDictionary(op=>op.Name,op=>assigned[op.Name],StringComparer.Ordinal));
    }

    /// <summary>Reject missing/extra operations, backward dependencies and resource oversubscription in a proposed schedule.</summary>
    public static void ValidateAssignments(IEnumerable<ScheduledOperation> operations,IReadOnlyDictionary<string,int> capacities,
        IReadOnlyDictionary<string,int> cycles)
    {
        var items=ValidateDeclarations(operations,capacities);ArgumentNullException.ThrowIfNull(cycles);
        var assignedNames=cycles.Keys.ToHashSet(StringComparer.Ordinal);
        if(cycles.Count!=items.Length||items.Any(op=>!assignedNames.Contains(op.Name)||!cycles.TryGetValue(op.Name,out int edge)||edge<0||edge>=4095))
            throw new ArgumentException("Schedule must assign each operation an edge in 0..4094.",nameof(cycles));
        foreach(var op in items)
            if(op.Dependencies.Any(d=>cycles[d]>=cycles[op.Name]))
                throw new ArgumentException($"Operation '{op.Name}' needs an earlier dependency edge.",nameof(cycles));
        foreach(var group in items.GroupBy(op=>(cycles[op.Name],op.Resource)))
            if(group.Count()>capacities[group.Key.Resource])
                throw new ArgumentException($"Resource '{group.Key.Resource}' conflicts at edge {group.Key.Item1}.",nameof(cycles));
    }

    private static ScheduledOperation[] ValidateDeclarations(IEnumerable<ScheduledOperation> operations,IReadOnlyDictionary<string,int> capacities)
    {
        ArgumentNullException.ThrowIfNull(operations);ArgumentNullException.ThrowIfNull(capacities);
        var items=operations.Take(4096).ToArray();
        if(items.Length is <1 or >4095||items.Any(op=>op is null))throw new ArgumentException("Expected 1..4095 non-null operations.",nameof(operations));
        if(capacities.Any(p=>string.IsNullOrWhiteSpace(p.Key)||p.Value<1))throw new ArgumentException("Resource capacities must be positive.",nameof(capacities));
        items=items.OrderBy(op=>op.Name,StringComparer.Ordinal).ToArray();
        var names=items.Select(op=>op.Name).ToHashSet(StringComparer.Ordinal);
        if(names.Count!=items.Length)throw new ArgumentException("Operation names must be unique.",nameof(operations));
        if(items.Sum(op=>(long)Math.Max(1,op.Dependencies.Count))>InstructionControlFlow.MaxEdges)
            throw new ArgumentException("Dependency graph exceeds the edge budget.",nameof(operations));
        foreach(var op in items)
        {
            // Names are ordinal, even if a caller supplied a case-insensitive map.
            // Otherwise two aliases could each receive the same physical capacity.
            if(!capacities.Keys.Contains(op.Resource,StringComparer.Ordinal))throw new ArgumentException($"Unknown resource '{op.Resource}'.",nameof(capacities));
            if(op.Dependencies.Any(d=>!names.Contains(d)))throw new ArgumentException($"Unknown dependency of '{op.Name}'.",nameof(operations));
        }
        return items;
    }

    /// <summary>
    /// Emit execution states followed by a non-repeating completion state. Declare
    /// memories/registers before this call and state predicates afterward. The
    /// callback emits actual Read/Write/Compute/Assign operations; Lower still
    /// independently rejects physical resource conflicts. Callback failures propagate.
    /// </summary>
    public ScheduledState EmitStates(ScheduledMachineBuilder builder,string prefix,Action<ScheduledState,string> emit)
    {
        ArgumentNullException.ThrowIfNull(builder);ArgumentNullException.ThrowIfNull(emit);
        if(string.IsNullOrWhiteSpace(prefix))throw new ArgumentException("State prefix required.",nameof(prefix));
        var states=Enumerable.Range(0,CycleCount).Select(c=>builder.State(prefix+"_"+c)).ToArray();
        var done=builder.State(prefix+"_done");
        foreach(var op in operations)emit(states[Cycles[op.Name]],op.Name);
        for(int cycle=0;cycle<states.Length;cycle++)states[cycle].Transition(cycle+1<states.Length?states[cycle+1].Name:done.Name);
        done.Transition(done.Name);return done;
    }
}
