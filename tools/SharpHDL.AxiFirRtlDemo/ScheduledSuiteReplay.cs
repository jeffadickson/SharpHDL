using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Reflection;
using System.Reflection.Emit;
using SharpHDL;
using SharpHDL.Examples;
using SharpHDL.Verification;
using SharpHDL.Core;

/// <summary>Multiple independently specified kernels through compact Core/HDL replay.</summary>
internal static class ScheduledSuiteReplay
{
    /// <summary>Runtime C# loop for native, legacy and scheduled execution.</summary>
    public static uint Update(uint[] words,uint count,uint factor)
    {
        uint sum=0;
        for(uint i=0;i<count;i++){uint value=words[i]*factor+1;words[i]=value;sum+=value;}
        return sum;
    }
    /// <summary>Actual C# shift-count masking and signed/unsigned right-shift workload.</summary>
    public static uint ShiftMix(uint value,int count)
        =>unchecked((value<<count)+(value>>count)+(uint)((int)value>>count));
    internal static void Generate(string directory)
    {
        if(!Directory.Exists(directory))Directory.CreateDirectory(directory);
        foreach(int width in new[]{1,4,8})
        {
            var replay=new CoreReplay(ScheduledTable.Build(width));
            int count=ScheduledTableCampaign.Run(width,replay.Drive,name=>(int)replay.Machine.ReadOutput(name),replay.Clock);
            replay.Save(directory);Console.WriteLine($"Table {width}: {count} independent-oracle responses.");
        }
        foreach(var implementation in new[]{HardwareMemoryImplementation.Auto,HardwareMemoryImplementation.XilinxBlock})
        {
            var replay=new CoreReplay(ScheduledBankedTable.Build(256,implementation));
            replay.Clock(true);
            for(int address=0;address<256;address++)
            {
                replay.Drive("address",address);replay.Drive("left",65535-address);replay.Drive("right",address+7);
                replay.Clock(false);replay.Clock(false);
                if(replay.Machine.ReadOutput("out_left")!=65535-address||replay.Machine.ReadOutput("out_right")!=address+7)
                    throw new InvalidOperationException("Banked array oracle mismatch.");
            }
            replay.Save(directory);
        }
        var method=typeof(ScheduledSuiteReplay).GetMethod(nameof(Update))!;
        var options=new SequentialOptions{MemoryDepth=8,MaxCycles=1000};
        var compiled=CilCompiler.BuildScheduledSequential(method,"ScheduledCilLoop",options);
        var cil=new CoreReplay(compiled.Design);
        var module=CilCompiler.BuildSequential(method,"LegacyCilLoop",options);
        var legacy=module.Simulate();var ports=module.Ports.ToDictionary(p=>p.Name);
        void Drive(string name,int value){cil.Drive(name,value);legacy.Set(ports[name],unchecked((uint)value));}
        void Clock(bool reset)
        {
            cil.Clock(reset);legacy.Tick(reset);
            foreach(string output in new[]{"busy","done","fault","timeout","result","accept"})
                if(cil.Machine.ReadOutput(output)!=legacy.Read(ports[output]))throw new InvalidOperationException("Legacy/CIL mismatch: "+output);
        }
        Clock(true);var memory=new uint[8];
        foreach(int count in new[]{0,1,8,4,9,8})
        {
            uint expected=0;
            // Independent functional model preserves earlier writes even when the ninth access faults.
            for(int i=0;i<Math.Min(count,8);i++){memory[i]=unchecked(memory[i]*3+1);expected=unchecked(expected+memory[i]);}
            Drive("start",1);Drive("arg1",count);Drive("arg2",3);Clock(false);Drive("start",0);
            int edges=0;while(cil.Machine.ReadOutput("done")==0&&edges++<1002)Clock(false);
            if(cil.Machine.ReadOutput("done")!=1||cil.Machine.ReadOutput("fault")!=(count>8?1:0)||
                count<=8&&(uint)cil.Machine.ReadOutput("result")!=expected)throw new InvalidOperationException("Native CIL oracle mismatch.");
        }
        cil.Save(directory);
        File.WriteAllText(Path.Combine(directory,"ScheduledCilLoop.il-map.json"),JsonSerializer.Serialize(compiled.StateOffsets));
        WriteSourceMap(directory,compiled);
        Console.WriteLine("CIL loop: native/legacy/Core agree, including partial writes on bounds fault.");
        GenerateShifts(directory);
        GenerateNarrowConversions(directory);
        GenerateScalarStorage(directory);
        GenerateCheckedConversions(directory);
        GenerateArrayAliases(directory);
        GenerateMultipleRams(directory);
        GenerateMemoryQualification(directory);
        var planned=new CoreReplay(ScheduledPlannedBanks.Build());
        for(int phase=0;phase<=6;phase++)
        {
            planned.Drive("left",7);planned.Drive("right",11);planned.Clock(true);
            for(int edge=0;edge<phase;edge++)planned.Clock(false);
            int left=phase*29,right=255-phase*18;
            planned.Drive("left",left);planned.Drive("right",right);planned.Clock(true);
            for(int edge=0;edge<6;edge++)planned.Clock(false);
            int expected=(left*right+left*3)&255;
            if(planned.Read("done")!=1||planned.Read("result")!=expected)
                throw new InvalidOperationException("Planned-bank independent arithmetic/reset oracle mismatch.");
            planned.Clock(false);
            if(planned.Read("result")!=expected)throw new InvalidOperationException("Completion must park.");
        }
        planned.Save(directory);
        var iterativeCompilation=ScheduledIterativeBanks.BuildWithPlan();
        var iterative=new CoreReplay(iterativeCompilation.Design);
        for(int resetPhase=0;resetPhase<=20;resetPhase++)
        {
            iterative.Drive("left",17);iterative.Drive("right",23);iterative.Clock(true);
            for(int edge=0;edge<resetPhase;edge++)iterative.Clock(false);
            iterative.Clock(true);
            int left=(resetPhase*19+7)&255,right=(resetPhase*31+11)&255;
            iterative.Drive("left",left);iterative.Drive("right",right);
            for(int edge=0;edge<20;edge++)
            {
                iterative.Clock(false);
                if(edge==0){iterative.Drive("left",255);iterative.Drive("right",0);}
                if((int)iterative.Read("done")!=(edge==19?1:0))throw new InvalidOperationException("Iterative completion latency mismatch.");
            }
            if((int)iterative.Read("result")!=((left*right+left*3)&255))throw new InvalidOperationException("Iterative numeric oracle mismatch.");
            iterative.Clock(false);iterative.Clock(false);
        }
        iterative.Save(directory);
        File.WriteAllText(Path.Combine(directory,"ScheduledIterativeBanks.schedule.json"),JsonSerializer.Serialize(new{
            iterativeCompilation.Plan.Starts,iterativeCompilation.Plan.Latencies,iterativeCompilation.Plan.Dependencies}));
        File.WriteAllText(Path.Combine(directory,"ScheduledPlannedBanks.schedule.json"),
            JsonSerializer.Serialize(ScheduledPlannedBanks.Plan().Cycles,new JsonSerializerOptions{WriteIndented=true}));
        var liteModule=AxiConformanceCampaign.BuildLite();
        var liteGraph=liteModule.ExportCoreGraph();
        var lite=new CoreReplay(liteModule.Name,liteGraph.Graph,liteGraph.Inputs,liteGraph.Outputs,
            liteGraph.NextState,liteGraph.ResetValues,liteGraph.Memories);
        AxiConformanceCampaign.LiteTraffic(Enumerable.Range(0,256).Select(i=>(byte)i).ToArray(),lite.Drive,
            name=>unchecked((int)(uint)lite.Read(name)),lite.Clock);lite.Save(directory);
        var streamModule=AxiConformanceCampaign.BuildStream();
        var streamGraph=streamModule.ExportCoreGraph();
        var stream=new CoreReplay(streamModule.Name,streamGraph.Graph,streamGraph.Inputs,streamGraph.Outputs,
            streamGraph.NextState,streamGraph.ResetValues,streamGraph.Memories);
        var traffic=Enumerable.Repeat((byte)2,12).Concat(Enumerable.Repeat((byte)3,24))
            .Concat(Enumerable.Range(0,220).Select(i=>(byte)i)).ToArray();
        AxiConformanceCampaign.StreamTraffic(traffic,stream.Drive,name=>(int)stream.Read(name),stream.Clock);
        stream.Save(directory);
    }

    private static void GenerateShifts(string directory)
    {
        GenerateShifts(directory,typeof(ScheduledSuiteReplay).GetMethod(nameof(ShiftMix))!,"ScheduledCilShifts");
        // C# inserts its own count mask. This separate raw-CIL kernel deliberately
        // omits it, exercising the frontend policy and unavailable-symbol path in RTL.
        var assembly=AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("RawShiftReplay"),AssemblyBuilderAccess.RunAndCollect);
        var type=assembly.DefineDynamicModule("main").DefineType("RawShiftKernel",TypeAttributes.Public);
        var method=type.DefineMethod("ShiftMix",MethodAttributes.Public|MethodAttributes.Static,typeof(uint),new[]{typeof(uint),typeof(int)});
        var il=method.GetILGenerator();
        foreach(var operation in new[]{OpCodes.Shl,OpCodes.Shr_Un,OpCodes.Shr})
        {
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldarg_1);il.Emit(operation);
            if(operation!=OpCodes.Shl)il.Emit(OpCodes.Add);
        }
        il.Emit(OpCodes.Ret);
        GenerateShifts(directory,type.CreateType()!.GetMethod("ShiftMix")!,"ScheduledRawCilShifts");
    }

    private static void GenerateShifts(string directory,MethodInfo method,string moduleName)
    {
        var compilation=CilCompiler.BuildScheduledSequential(method,moduleName,new SequentialOptions{MaxCycles=128});
        var replay=new CoreReplay(compilation.Design);
        var module=CilCompiler.BuildSequential(method,"LegacyShifts",new SequentialOptions{MaxCycles=128});
        var legacy=module.Simulate();var ports=module.Ports.ToDictionary(p=>p.Name);
        void Drive(string name,int value){replay.Drive(name,value);legacy.Set(ports[name],unchecked((uint)value));}
        void Clock(bool reset)
        {
            replay.Clock(reset);legacy.Tick(reset);
            foreach(string output in new[]{"busy","done","fault","timeout","result","accept"})
                if(replay.Machine.ReadOutput(output)!=legacy.Read(ports[output]))throw new InvalidOperationException("Shift legacy/Core mismatch: "+output);
        }
        Clock(true);int transactions=0;
        foreach(uint value in new uint[]{0,1,127,0x7fffffffu,0x80000000u,0xffffffffu,0xdeadbeefu})
        foreach(int count in new[]{int.MinValue,-65,-33,-32,-31,-1,0,1,7,31,32,33,63,64,int.MaxValue})
        {
            Drive("start",1);Drive("arg0",unchecked((int)value));Drive("arg1",count);Clock(false);Drive("start",0);
            int edges=0;while(replay.Machine.ReadOutput("done")==0&&edges++<129)Clock(false);
            if(replay.Machine.ReadOutput("done")!=1||replay.Machine.ReadOutput("fault")!=0||
                (uint)replay.Machine.ReadOutput("result")!=ShiftMix(value,count))throw new InvalidOperationException("Native shift oracle mismatch.");
            transactions++;
        }
        replay.Save(directory);
        File.WriteAllText(Path.Combine(directory,moduleName+".il-map.json"),JsonSerializer.Serialize(compilation.StateOffsets));
        WriteSourceMap(directory,compilation);
        Console.WriteLine($"CIL shifts {moduleName}: {transactions} native/legacy/Core transactions agree.");
    }

    private static void GenerateMemoryQualification(string directory)
    {
        foreach(var implementation in new[]{HardwareMemoryImplementation.Auto,HardwareMemoryImplementation.XilinxBlock})
        {
            var d=new MemoryCollisionExample(implementation);
            var r=new CoreReplay(d.Name,d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
            int edges=MemoryCollisionCampaign.Run(r.Drive,r.Clock,n=>(int)r.Read(n));
            r.Save(directory);Console.WriteLine(d.Name+": "+edges+" independent array-oracle edges.");
        }
        foreach(bool shared in new[]{false,true})
        {
            string name=shared?"ScheduledSharedRam":"ScheduledDistinctRam";
            var c=CilCompiler.BuildScheduledSequential(typeof(ScheduledSharedRam).GetMethod(nameof(ScheduledSharedRam.Update))!,name,
                new SequentialOptions{MemoryDepth=3,RamAliases=shared?new System.Collections.Generic.Dictionary<int,int>{{1,0}}:null});
            var r=new CoreReplay(c.Design);uint[] left=new uint[3],right=shared?left:new uint[3];r.Clock(true);
            foreach(var (index,word) in new[]{(0u,5u),(1u,uint.MaxValue),(2u,17u),(3u,4u),(0u,9u)})
            {
                uint expected=0;bool failed=false;
                try{expected=ScheduledSharedRam.Update(left,right,index,word);}catch(IndexOutOfRangeException){failed=true;}
                r.Drive("arg2",(int)index);r.Drive("arg3",unchecked((int)word));r.Drive("start",1);r.Clock(false);
                r.Drive("start",0);r.Drive("arg2",99);r.Drive("arg3",99);
                int edges=0;while(r.Read("done")==0&&edges++<1002)r.Clock(false);
                if(r.Read("done")!=1||r.Read("fault")!=(failed?1:0)||r.Read("result")!=expected)throw new InvalidOperationException("Shared RAM CLR/Core replay mismatch.");
                r.Clock(true);
            }
            r.Save(directory);WriteSourceMap(directory,c);
        }
    }

    private static void GenerateMultipleRams(string directory)
    {
        var method=typeof(ScheduledMultiRam).GetMethod(nameof(ScheduledMultiRam.Update))!;
        var options=new SequentialOptions{MaxCycles=400,MemoryDepths=new System.Collections.Generic.Dictionary<int,int>{{0,2},{1,3}},
            InitialMemories=new System.Collections.Generic.Dictionary<int,System.Numerics.BigInteger[]>{{0,[7,9]},{1,[21,22,23]}}};
        var compiled=CilCompiler.BuildScheduledSequential(method,"ScheduledGuardedRams",options);
        var replay=new CoreReplay(compiled.Design);replay.Clock(true);uint[] left=[7,9],right=[21,22,23];
        foreach(var (select,index,word) in new[]{(0u,0u,5u),(1u,1u,9u),(2u,2u,17u),(1u,2u,31u),(0u,0u,0u),(2u,2u,3u),(3u,0u,0u)})
        {
            uint expected=0;bool failed=false;
            try {expected=ScheduledMultiRam.Update(left,right,select,index,word);}catch(IndexOutOfRangeException){failed=true;}
            replay.Drive("arg2",(int)select);replay.Drive("arg3",(int)index);replay.Drive("arg4",(int)word);replay.Drive("start",1);replay.Clock(false);
            replay.Drive("start",0);replay.Drive("arg2",99);replay.Drive("arg3",99);replay.Drive("arg4",99);
            int edges=0;while(replay.Read("done")==0&&edges++<402)replay.Clock(false);
            if(replay.Read("done")!=1||replay.Read("fault")!=(failed?1:0)||replay.Read("result")!=expected)
                throw new InvalidOperationException("Guarded multiple-RAM CLR/Core mismatch.");
        }
        replay.Save(directory);WriteSourceMap(directory,compiled);
        Console.WriteLine("Guarded RAMs: seven CLR/Core capture, null, per-origin bounds and partial-write transactions agree.");
    }

    private static void GenerateArrayAliases(string directory)
    {
        var method=typeof(ScheduledArrayAliases).GetMethod(nameof(ScheduledArrayAliases.Update))!;
        var compiled=CilCompiler.BuildScheduledSequential(method,"ScheduledAliasUpdate",
            new SequentialOptions{MemoryDepth=4,InitialMemory=[2,3,4,5],MaxCycles=1000});
        var replay=new CoreReplay(compiled.Design);replay.Clock(true);uint[] memory=[2,3,4,5];
        foreach(uint count in new uint[]{0,1,4,5,0,4,2})
        {
            uint expected=0;bool failed=false;
            try { expected=ScheduledArrayAliases.Update(memory,count,3); }
            catch(IndexOutOfRangeException){failed=true;}
            replay.Drive("arg1",(int)count);replay.Drive("arg2",3);replay.Drive("start",1);replay.Clock(false);
            replay.Drive("start",0);replay.Drive("arg1",0);replay.Drive("arg2",99);
            int edges=0;while(replay.Read("done")==0&&edges++<1002)replay.Clock(false);
            if(replay.Read("done")!=1||replay.Read("fault")!=(failed?1:0)||replay.Read("result")!=expected)
                throw new InvalidOperationException("Alias C#/Core RAM capture/partial-write mismatch.");
        }
        replay.Save(directory);WriteSourceMap(directory,compiled);
        var nullable=TypedCilFixture.Create(il=>
        {
            var join=il.DefineLabel();il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Brfalse,join);
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Stloc_0);il.MarkLabel(join);
            il.Emit(OpCodes.Ldloc_0);il.Emit(OpCodes.Ldnull);il.Emit(OpCodes.Cgt_Un);
            il.Emit(OpCodes.Ldloc_0);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ceq);il.Emit(OpCodes.Add);il.Emit(OpCodes.Ret);
        },typeof(uint[]));
        var nc=CilCompiler.BuildScheduledSequential(nullable,"ScheduledAliasNull");var nr=new CoreReplay(nc.Design);nr.Clock(true);
        foreach(uint bound in new uint[]{0,1,0,1,1,0})
        {
            nr.Drive("arg1",(int)bound);nr.Drive("start",1);nr.Clock(false);nr.Drive("start",0);
            int edges=0;while(nr.Read("done")==0&&edges++<64)nr.Clock(false);
            uint expected=(uint)nullable.Invoke(null,[new uint[16],bound])!;
            if(nr.Read("done")!=1||nr.Read("fault")!=0||nr.Read("result")!=expected||expected!=2*bound)
                throw new InvalidOperationException("Nullable alias CLR/tag mismatch.");
        }
        nr.Save(directory);WriteSourceMap(directory,nc);
        Console.WriteLine("Aliases: seven persistent RAM and six nullable/reset-per-start CLR transactions agree.");
    }

    private static void GenerateCheckedConversions(string directory)
    {
        var method=NarrowCilFixture.CheckedDispatch(out var offsets);
        var compiled=CilCompiler.BuildScheduledSequential(method,"ScheduledChecked");var replay=new CoreReplay(compiled.Design);replay.Clock(true);
        for(int index=0;index<NarrowCilFixture.CheckedOpcodes.Count;index++)
        foreach(uint value in new uint[]{0,127,128,255,256,32767,32768,65535,65536,0x7fffffff,0x80000000,0xffff7fff,0xffff8000,0xffffff7f,0xffffff80,uint.MaxValue})
        {
            uint expected=0;bool overflow=false;
            try { expected=(uint)method.Invoke(null,[value,(uint)index])!; }
            catch(TargetInvocationException error) when(error.InnerException is OverflowException){overflow=true;}
            replay.Drive("arg0",unchecked((int)value));replay.Drive("arg1",index);replay.Drive("start",1);replay.Clock(false);
            replay.Drive("start",0);replay.Drive("arg0",unchecked((int)~value));replay.Drive("arg1",99);
            int edges=0;while(replay.Read("done")==0&&edges++<64)replay.Clock(false);
            if(replay.Read("done")!=1||replay.Read("fault")!=(overflow?1:0)||replay.Read("timeout")!=0||
                replay.Read("conversion_overflow")!=(overflow?1:0)||replay.Read("fault_il")!=(overflow?offsets[index]:0)||replay.Read("result")!=expected)
                throw new InvalidOperationException("Checked CLR/Core diagnostic mismatch: "+NarrowCilFixture.CheckedOpcodes[index].Name);
            replay.Clock(false);
        }
        replay.Save(directory);WriteSourceMap(directory,compiled);
        Console.WriteLine("Checked dispatch: 192 CLR/Core boundary, overflow and capture transactions agree.");
        var memoryMethod=typeof(ScheduledNarrowCil).GetMethod(nameof(ScheduledNarrowCil.CheckedStore))!;
        var memoryCompiled=CilCompiler.BuildScheduledSequential(memoryMethod,"ScheduledCheckedMemory",
            new SequentialOptions{MemoryDepth=2,InitialMemory=[0,5],MaxCycles=128});
        var memoryReplay=new CoreReplay(memoryCompiled.Design);memoryReplay.Clock(true);uint[] memory=[0,5];
        foreach(uint value in new uint[]{256,0,255,0,uint.MaxValue,0,1,0})
        {
            uint expected=0;bool failed=false;
            try { expected=ScheduledNarrowCil.CheckedStore(memory,value); }
            catch(OverflowException){failed=true;}
            memoryReplay.Drive("arg1",unchecked((int)value));memoryReplay.Drive("start",1);memoryReplay.Clock(false);
            memoryReplay.Drive("start",0);memoryReplay.Drive("arg1",unchecked((int)~value));
            int edges=0;while(memoryReplay.Read("done")==0&&edges++<130)memoryReplay.Clock(false);
            if(memoryReplay.Read("done")!=1||memoryReplay.Read("fault")!=(failed?1:0)||
                memoryReplay.Read("conversion_overflow")!=(failed?1:0)||memoryReplay.Read("result")!=expected)
                throw new InvalidOperationException("Checked C# RAM side-effect/recovery mismatch.");
        }
        memoryReplay.Save(directory);WriteSourceMap(directory,memoryCompiled);
        Console.WriteLine("Checked memory: 8 CLR/Core partial-write and recovery transactions agree.");
    }

    private static void GenerateScalarStorage(string directory)
    {
        foreach(var type in new[]{typeof(sbyte),typeof(byte),typeof(short),typeof(ushort),typeof(char)})
        foreach(bool argument in new[]{false,true})
        {
            var method=NarrowCilFixture.Storage(type,argument);
            var compiled=CilCompiler.BuildScheduledSequential(method,"Storage"+type.Name+(argument?"Argument":"Local"));
            var replay=new CoreReplay(compiled.Design);replay.Clock(true);
            foreach(uint value in new uint[]{0,127,128,255,256,32767,32768,65535,65536,0x80000000,uint.MaxValue})
            {
                replay.Drive("arg0",argument?0:unchecked((int)value));if(argument)replay.Drive("arg1",unchecked((int)value));
                replay.Drive("start",1);replay.Clock(false);replay.Drive("start",0);
                replay.Drive("arg0",0);if(argument)replay.Drive("arg1",unchecked((int)~value));
                int edges=0;while(replay.Read("done")==0&&edges++<32)replay.Clock(false);
                object?[] args=argument?[Activator.CreateInstance(type),value]:[value];
                if(replay.Read("done")!=1||replay.Read("fault")!=0||(uint)replay.Read("result")!=(uint)method.Invoke(null,args)!)
                    throw new InvalidOperationException("Narrow storage CLR/Core mismatch.");
            }
            replay.Save(directory);WriteSourceMap(directory,compiled);
        }
    }

    private static void GenerateNarrowConversions(string directory)
    {
        var cases=new[]{("conv.i1","ScheduledNarrowI1"),("conv.u1","ScheduledNarrowU1"),
            ("conv.i2","ScheduledNarrowI2"),("conv.u2","ScheduledNarrowU2"),("mix","ScheduledCilNarrow")};
        foreach(var (opcode,name) in cases)
        {
            var method=opcode=="mix"?typeof(ScheduledNarrowCil).GetMethod(nameof(ScheduledNarrowCil.Evaluate))!:NarrowCilFixture.Method(opcode);
            var compiled=CilCompiler.BuildScheduledSequential(method,name);var replay=new CoreReplay(compiled.Design);
            var native=method.CreateDelegate<Func<uint,uint>>();replay.Clock(true);
            foreach(uint value in new uint[]{0,1,0x7f,0x80,0xff,0x100,0x17f,0x180,0x7fff,0x8000,0xffff,0x10000,0x123480ff,0x7fffffff,0x80000000,0xffffffff})
            {
                replay.Drive("arg0",unchecked((int)value));replay.Drive("start",1);replay.Clock(false);
                replay.Drive("start",0);replay.Drive("arg0",unchecked((int)~value));
                int edges=0;while(replay.Read("done")==0&&edges++<128)replay.Clock(false);
                if(replay.Read("done")!=1||replay.Read("fault")!=0||(uint)replay.Read("result")!=native(value)||
                    opcode!="mix"&&native(value)!=NarrowCilFixture.Expected(value,opcode))
                    throw new InvalidOperationException("Narrow conversion CLR/arithmetic/Core mismatch: "+opcode);
            }
            replay.Save(directory);WriteSourceMap(directory,compiled);
            Console.WriteLine($"CIL conversions {name}: 16 independent boundary/capture transactions agree.");
        }
    }

    private static void WriteSourceMap(string directory,ScheduledCilCompilation compilation)
    {
        if(!Directory.Exists(directory))throw new DirectoryNotFoundException(directory);
        var map=new
        {
            Module=compilation.Design.Name,
            Method=compilation.Method.DeclaringType?.FullName+"."+compilation.Method.Name,
            StateSignal="s_scheduler_state",
            SymbolStatus=compilation.Symbols.Status.ToString(),
            States=compilation.StateEncodings.Select(pair=>new
            {
                State=pair.Key,Encoding=pair.Value,
                IlOffset=compilation.StateOffsets.TryGetValue(pair.Key,out int offset)?(int?)offset:null,
                Source=compilation.StateSources[pair.Key]
            }).ToArray()
        };
        File.WriteAllText(Path.Combine(directory,compilation.Design.Name+".source-map.json"),
            JsonSerializer.Serialize(map,new JsonSerializerOptions{WriteIndented=true}));
        // Canonical analysis contains original instructions, while the state map
        // above also represents idle and the additional synchronous RAM waits.
        File.WriteAllText(Path.Combine(directory,compilation.Design.Name+".control-flow.json"),
            JsonSerializer.Serialize(compilation.ControlFlow,new JsonSerializerOptions{WriteIndented=true}));
    }
}
