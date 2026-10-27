using System;
using SharpHDL.Core;
using SharpHDL.HLS;

namespace SharpHDL.Examples;

/// <summary>Two independent physical bank ports, writing then synchronously reading both words.</summary>
public static class ScheduledBankedTable
{
    /// <summary>Build a fixed two-edge transaction; data is captured at the second edge.</summary>
    public static ScheduledHardwareDesign Build(int rows=256,HardwareMemoryImplementation implementation=HardwareMemoryImplementation.Auto)
    {
        if(rows<2||rows>32768)throw new ArgumentOutOfRangeException(nameof(rows));
        int aw=1;while((1<<aw)<rows)aw++;
        var b=new ScheduledMachineBuilder("ScheduledBanks"+implementation);
        var address=b.Input("address",aw);var left=b.Input("left",16);var right=b.Input("right",16);
        var key=b.Register("key",aw);var a=b.Register("read_left",16);var c=b.Register("read_right",16);
        var banks=b.BankedMemory("storage",16,2,rows,implementation:implementation);
        var write=b.State("write");var read=b.State("read");var one=b.Constant(1,1);
        write.Write(banks.Banks[0],address,left,one).Write(banks.Banks[1],address,right,one)
            .Assign(key,address).Transition("read");
        read.Read(banks.Banks[0],key.Value,a,one).Read(banks.Banks[1],key.Value,c,one).Transition("write");
        b.Output("write_phase",b.InState(write));b.Output("out_left",a.Value);b.Output("out_right",c.Value);
        return b.Lower();
    }
}
