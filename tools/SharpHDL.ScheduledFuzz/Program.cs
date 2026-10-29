using SharpFuzz;
using SharpHDL.Examples;
using SharpHDL.Simulation;
using System.Buffers.Binary;
using System.Text;
using SharpHDL;
using System.Reflection;
using System.Reflection.Emit;
using SharpHDL.HLS;

// SharpFuzz/AFL++ owns generation, coverage feedback and crash retention.
// This bounded harness translates bytes into protocol traffic and an independent array model.
Fuzzer.OutOfProcess.Run(stream =>
{
    var bytes=new byte[256];int length=stream.ReadAtLeast(bytes,1,throwOnEndOfStream:false);
    if(length>0)FlowFuzzOracle.Check(bytes.AsSpan(0,length));
    if(length>0)EquivalenceFuzz.Check(bytes.AsSpan(0,length));
    if(length>0)RegionFuzz.Check(bytes.AsSpan(0,length));
    NarrowCilFuzz.Check(bytes.AsSpan(0,length));
    TypedAliasFuzz.Check(bytes.AsSpan(0,length));
    GuardedRamFuzz.Check(bytes.AsSpan(0,length));
    if(length>0)AxiProtocolFuzz.Run(bytes.AsSpan(0,length));
    if(length>=8)
    {
        // BCL endian decoding avoids a custom input parser; the count remains a signed CLR bit pattern.
        uint operand=BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0,4));
        int count=BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4,4));
        var shift=CilCompiler.BuildScheduledSequential(ShiftFuzzKernel.RawMethod,"FuzzShifts").Design;
        var machine=new HardwareGraphMachine(shift.Graph,shift.Inputs,shift.Outputs,shift.NextState,shift.ResetValues,shift.Memories);
        machine.SetInput("arg0",operand);machine.SetInput("arg1",unchecked((uint)count));
        machine.SetInput("start",1);machine.Tick();machine.SetInput("start",0);
        for(int edge=0;edge<128&&machine.ReadOutput("done")==0;edge++)machine.Tick();
        if(machine.ReadOutput("done")!=1||machine.ReadOutput("fault")!=0||
            (uint)machine.ReadOutput("result")!=ShiftFuzzKernel.Evaluate(operand,count))
            throw new InvalidOperationException("Native/scheduled shift mismatch.");
    }
    var offset=PortablePdbSourceMap.ParseIlOffset(Encoding.ASCII.GetString(bytes,0,length));
    if(offset<0)throw new InvalidOperationException("A parsed IL offset must never be negative.");
    var d=ScheduledTable.Build(2);
    var sim=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
    int[] table=new int[4];int phase=0,key=0,delta=0,result=0;bool update=false;
    sim.Reset();
    for(int i=0;i<length;i++)
    {
        int value=bytes[i];bool reset=(value&128)!=0;
        bool valid=(value&1)!=0,ready=(value&2)!=0;
        sim.SetInput("request_valid",valid?1:0);sim.SetInput("request_address",value&3);
        sim.SetInput("request_delta",value*257);sim.SetInput("request_update",(value>>2)&1);
        sim.SetInput("response_ready",ready?1:0);
        if((int)sim.ReadOutput("request_ready")!=(phase==0?1:0) ||
            (int)sim.ReadOutput("response_valid")!=(phase==3?1:0) ||
            phase==3 && (int)sim.ReadOutput("response_data")!=result)
            throw new InvalidOperationException($"Table mismatch at byte {i}.");
        if(reset){phase=0;sim.Reset();continue;}
        if(phase==0 && valid){key=value&3;delta=value*257;update=(value&4)!=0;phase=1;}
        else if(phase==1)phase=2;
        else if(phase==2){result=update?(table[key]+delta)&65535:table[key];if(update)table[key]=result;phase=3;}
        else if(phase==3 && ready)phase=0;
        sim.Tick();
    }
});

/// <summary>Fuzz adapter for valid/malformed graphs with an independent closure oracle.</summary>
internal static class FlowFuzzOracle
{
    internal static void Check(ReadOnlySpan<byte> bytes)
    {
        int count=1+bytes[0]%8;
        var lists=Enumerable.Range(0,count).Select(_=>new List<int>()).ToArray();
        bool invalid=false;
        for(int i=1;i<bytes.Length;i++)
        {
            // count is deliberately a possible target, exercising absent-vertex
            // validation without asking the fuzzer to manufacture CLR collections.
            int target=bytes[i]%(count+1);lists[i%count].Add(target);invalid|=target==count;
        }
        var graph=Enumerable.Range(0,count).ToDictionary(i=>i,i=>(IReadOnlyList<int>)lists[i]);
        InstructionControlFlow flow;
        try { flow=InstructionControlFlow.Analyze(graph); }
        catch(ArgumentException) when(invalid) { return; }
        if(invalid)throw new InvalidOperationException("Malformed instruction graph was accepted.");
        var closure=new bool[count,count];
        for(int i=0;i<count;i++)
        {closure[i,i]=true;foreach(int target in lists[i])closure[i,target]=true;}
        for(int k=0;k<count;k++)for(int i=0;i<count;i++)for(int j=0;j<count;j++)
            closure[i,j]|=closure[i,k]&&closure[k,j];
        if(!flow.ReachableOffsets.SequenceEqual(Enumerable.Range(0,count).Where(i=>closure[0,i])))
            throw new InvalidOperationException("Instruction graph reachability mismatch.");
        var expected=new List<string>();var assigned=new HashSet<int>();
        for(int i=0;i<count;i++)if(!assigned.Contains(i))
        {
            int[] members=Enumerable.Range(0,count).Where(j=>closure[i,j]&&closure[j,i]).ToArray();
            assigned.UnionWith(members);expected.Add(string.Join(",",members));
        }
        if(!expected.SequenceEqual(flow.StrongComponents.Select(c=>string.Join(",",c))))
            throw new InvalidOperationException("Instruction graph component mismatch.");
        // Reuse the independently computed closure to classify all dependency
        // cycles, including those disconnected from the graph entry.
        bool cyclic=Enumerable.Range(0,count).Any(i=>lists[i].Contains(i)||
            Enumerable.Range(0,count).Any(j=>i!=j&&closure[i,j]&&closure[j,i]));
        var operations=Enumerable.Range(0,count).Select(i=>new ScheduledOperation("op"+i,"bank"+(i%2),
            lists[i].Select(j=>"op"+j).ToArray())).ToArray();
        var capacity=new Dictionary<string,int>{{"bank0",1},{"bank1",1}};
        ScheduledOperationPlan plan;
        try { plan=ScheduledOperationPlan.Create(operations,capacity); }
        catch(ArgumentException) when(cyclic) { return; }
        if(cyclic)throw new InvalidOperationException("Cyclic dependency graph was accepted.");
        foreach(var operation in operations)
            foreach(string dependency in operation.Dependencies)
                if(plan.Cycles[dependency]>=plan.Cycles[operation.Name])
                    throw new InvalidOperationException("Dependency schedule violated.");
        if(operations.GroupBy(op=>(plan.Cycles[op.Name],op.Resource)).Any(g=>g.Count()>1))
            throw new InvalidOperationException("Bank capacity exceeded.");
    }
}

/// <summary>Real C# oracle and CIL input, kept outside the instrumented production assemblies.</summary>
internal static class ShiftFuzzKernel
{
    // Raw instructions avoid a compiler-inserted count mask that would otherwise
    // prevent the campaign from challenging the frontend's own masking policy.
    internal static MethodInfo RawMethod { get; }=CreateRawMethod();
    /// <summary>Combine all three CLR shift operations with modular addition.</summary>
    public static uint Evaluate(uint value,int count)
        =>unchecked((value<<count)+(value>>count)+(uint)((int)value>>count));

    private static MethodInfo CreateRawMethod()
    {
        var assembly=AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("FuzzRawShift"),AssemblyBuilderAccess.RunAndCollect);
        var type=assembly.DefineDynamicModule("main").DefineType("Kernel",TypeAttributes.Public);
        var method=type.DefineMethod("Shift",MethodAttributes.Public|MethodAttributes.Static,typeof(uint),new[]{typeof(uint),typeof(int)});
        var il=method.GetILGenerator();
        foreach(var operation in new[]{OpCodes.Shl,OpCodes.Shr_Un,OpCodes.Shr})
        {
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldarg_1);il.Emit(operation);
            if(operation!=OpCodes.Shl)il.Emit(OpCodes.Add);
        }
        il.Emit(OpCodes.Ret);return type.CreateType()!.GetMethod("Shift")!;
    }
}
