namespace SharpHDL.Simulation;

/// <summary>Execution engine selection contract. Legacy engines are not yet migrated to this assembly.</summary>
public enum SimulationBackend { ReferenceInterpreter, CompiledDotNet, ExternalVerilator }
public sealed record SimulationOptions(SimulationBackend Backend = SimulationBackend.ReferenceInterpreter, bool FourState = false);
