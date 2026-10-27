using System;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        Console.WriteLine("CIL loop: native/legacy/Core agree, including partial writes on bounds fault.");
    }
}
