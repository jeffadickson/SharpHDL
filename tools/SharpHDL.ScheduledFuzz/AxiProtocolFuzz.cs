using SharpHDL;
using SharpHDL.Simulation;
using SharpHDL.Verification;
using System.Numerics;

/// <summary>Bounded protocol fuzz integration; SharpFuzz owns mutation and corpus management.</summary>
internal static class AxiProtocolFuzz
{
    // Only construct instrumented graphs inside the initialized fuzz callback.
    // Immutable exports are reused, but each case gets fresh runtime/checker state.
    private static readonly Lazy<CoreGraphExport> Lite=new(()=>AxiConformanceCampaign.BuildLite().ExportCoreGraph());
    private static readonly Lazy<CoreGraphExport> Stream=new(()=>AxiConformanceCampaign.BuildStream().ExportCoreGraph());
    internal static void Run(ReadOnlySpan<byte> bytes)
    {
        byte[] commands=bytes[..Math.Min(bytes.Length,8)].ToArray();
        Run(Lite.Value,commands,true);Run(Stream.Value,commands,false);
    }
    private static void Run(CoreGraphExport graph,byte[] commands,bool lite)
    {
        var machine=new HardwareGraphMachine(graph.Graph,graph.Inputs,graph.Outputs,graph.NextState,graph.ResetValues,graph.Memories);
        IReadOnlyDictionary<string,BigInteger>? snapshot=null;
        void Drive(string name,int value){machine.SetInput(name,unchecked((uint)value));snapshot=null;}
        int Read(string name)=>unchecked((int)(uint)(snapshot??=machine.ReadOutputs())[name]);
        void Clock(bool reset){if(reset)machine.Reset();else machine.Tick();snapshot=null;}
        if(lite)AxiConformanceCampaign.LiteTraffic(commands,Drive,Read,Clock);
        else AxiConformanceCampaign.StreamTraffic(commands,Drive,Read,Clock);
    }
}
