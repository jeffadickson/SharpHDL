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
    [Fact]
    public void Public_compatibility_factory_runs_real_TinyRisc_with_independent_Core_machine()
    {
        var module=TinyRisc.Build(new ushort[]{0xf001,0xf001,0xf000},"CoreMachineTinyRisc");
        var reference=module.Simulate();
        reference.Tick(true);
        var core=module.SimulateCore();
        var inputs=module.Ports.Where(p=>p.IsInput).ToDictionary(p=>p.Name,p=>p);
        var outputs=module.Ports.Where(p=>p.IsOutput).ToArray();
        for(int cycle=0;cycle<80;cycle++)
        {
            foreach(var (name,port) in inputs)
            {
                BigInteger value=name switch
                {
                    "run"=>1,
                    "tx_ready"=>1,
                    "switches"=>(cycle*127)&65535,
                    "rx_data"=>(cycle*11)&255,
                    _=>0
                };
                reference.Set(port,value);
                core.SetInput(name,value);
            }
            foreach(var output in outputs)
                Assert.Equal(reference.Read(output),core.ReadOutput(output.Name));
            reference.Tick();
            core.Tick();
        }
        Assert.Equal(80,core.Cycle);
        core.Reset();
        Assert.Equal(0,core.Cycle);
    }

}
