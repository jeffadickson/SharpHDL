using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SharpHDL;
using SharpHDL.Simulation;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>
/// Whole TinyRisc16 structural design exported through the canonical Core IR.
/// Checks CPU, ROM and RAM cones against the existing simulator without
/// assuming that the new evaluator has replaced production execution.
/// </summary>
public sealed class TinyRiscCoreIrTests
{
    [Fact]
    public void Real_TinyRisc16_rom_ram_and_state_export_match_reference()
    {
        // NOP, NOP, HALT. Drive run continuously; exercise UART/switch inputs.
        var m=TinyRisc.Build(new ushort[]{0xf001,0xf001,0xf000},"CoreTinyRisc");
        var export=m.ExportCoreGraph();
        Assert.NotEmpty(export.Memories);
        Assert.Contains(export.Graph.Nodes,n=>n.Operation==SharpHDL.Core.HardwareOperation.Lookup);
        Assert.Contains(export.Graph.Nodes,n=>n.Operation==SharpHDL.Core.HardwareOperation.MemoryRead);

        var sim=m.Simulate();
        sim.Tick(true);
        var state=export.ResetValues.ToDictionary(p=>p.Key,p=>p.Value);
        IReadOnlyDictionary<string,IReadOnlyList<BigInteger>> memory=
            export.Memories.ToDictionary(p=>p.Key,p=>p.Value.Initial);
        var rng=new Random(2026);
        for(int cycle=0;cycle<60;cycle++)
        {
            var inputs=new Dictionary<int,BigInteger>();
            foreach(var (name,id) in export.Inputs)
            {
                if(state.TryGetValue(name,out var register))
                {
                    inputs[id]=register;
                    continue;
                }
                var port=m.Ports.Single(p=>p.Name==name);
                BigInteger value=name switch
                {
                    "run"=>1,
                    "step"=>0,
                    "switches"=>rng.Next(65536),
                    "rx_valid"=>0,
                    "rx_data"=>rng.Next(256),
                    "tx_ready"=>1,
                    _=>0
                };
                sim.Set(port,value);
                inputs[id]=value;
            }
            var values=HardwareGraphEvaluator.Evaluate(export.Graph,inputs,memory);
            foreach(var (name,id) in export.Outputs)
                Assert.Equal(sim.Read(m.Ports.Single(p=>p.Name==name)),values[id]);
            var next=export.NextState.ToDictionary(p=>p.Key,p=>values[p.Value]);
            var nextMemory=HardwareMemoryTransitions.Commit(export.Graph,export.Memories,memory,values);
            sim.Tick();
            state=next;
            memory=nextMemory;
        }
    }
}
