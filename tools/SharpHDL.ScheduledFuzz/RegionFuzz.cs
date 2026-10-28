using SharpHDL.Examples;
using SharpHDL.HLS;
using SharpHDL.Simulation;

/// <summary>Coverage-guided reset/operand cases against real automatically scheduled iterative hardware.</summary>
internal static class RegionFuzz
{
    private static readonly Lazy<ScheduledHardwareDesign> Design=new(ScheduledIterativeBanks.Build);
    internal static void Check(ReadOnlySpan<byte> bytes)
    {
        var d=Design.Value;var machine=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        int left=bytes[0],right=bytes.Length>1?bytes[1]:0,phase=bytes.Length>2?bytes[2]%21:0;
        machine.SetInput("left",17);machine.SetInput("right",23);
        for(int edge=0;edge<phase;edge++)machine.Tick();machine.Reset();
        machine.SetInput("left",left);machine.SetInput("right",right);machine.Tick();
        machine.SetInput("left",255);machine.SetInput("right",0);
        for(int edge=1;edge<20;edge++)machine.Tick();
        if(machine.ReadOutput("done")!=1||(int)machine.ReadOutput("result")!=((left*right+left*3)&255))
            throw new InvalidOperationException("Iterative schedule differs from independent modular arithmetic.");
    }
}
