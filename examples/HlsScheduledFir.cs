using SharpHDL.HLS;

namespace SharpHDL.Examples;

/// <summary>Example entry point for schedule-driven FIR compilation.</summary>
public static class HlsScheduledFir
{
    /// <summary>
    /// Plan and lower a 2–16-tap FIR through the reusable scheduled-machine
    /// backend into canonical Core IR.
    /// </summary>
    public static ScheduledHardwareDesign Build(int taps)
        =>HlsScheduledFirCompiler.Compile(HlsSinglePortFirPlanner.Plan(taps));
}
