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
        File.WriteAllText(Path.Combine(directory,"ScheduledPlannedBanks.schedule.json"),
            JsonSerializer.Serialize(ScheduledPlannedBanks.Plan().Cycles,new JsonSerializerOptions{WriteIndented=true}));
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
