using System;
using SharpHDL.Simulation;

namespace SharpHDL
{
    /// <summary>
    /// Compatibility entry point for the independent canonical Core simulator.
    /// Existing Module.Simulate() remains the reference implementation.
    /// This adapter only lowers the design; all clock execution happens in
    /// SharpHDL.Simulation without a dependency on legacy Module.
    /// </summary>
    public static class CoreSimulationCompatibility
    {
        /// <summary>
        /// Export a validated Module and create a clocked canonical-IR machine.
        /// The machine is reset to register reset values on construction;
        /// RAM initial contents are preserved on subsequent Reset() calls.
        /// </summary>
        public static HardwareGraphMachine SimulateCore(this Module module)
        {
            if(module==null)throw new ArgumentNullException(nameof(module));
            var export=module.ExportCoreGraph();
            return new HardwareGraphMachine(export.Graph,export.Inputs,
                export.Outputs,export.NextState,export.ResetValues,export.Memories);
        }
    }
}
