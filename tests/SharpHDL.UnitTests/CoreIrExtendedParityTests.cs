using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SharpHDL;
using SharpHDL.Simulation;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Canonical IR differential coverage for bitfields, signed arithmetic,
/// ROM, direct DSP and synchronous read-before-write RAM.</summary>
public sealed class CoreIrExtendedParityTests
{
    [Fact]
    public void Bitfields_signed_comparisons_and_rom_match_legacy()
    {
        var m=new Module("CoreBitfields");
        var a=m.Input("a",8);
        var b=m.Input("b",8);
        var s=m.Output("slice",4);
        var shl=m.Output("shiftLeft",8);
        var shr=m.Output("shiftRight",8);
        var asr=m.Output("arithmeticShift",8);
        var ext=m.Output("signExtended",16);
        var joined=m.Output("joined",16);
        var equal=m.Output("equal",1);
        var less=m.Output("less",1);
        var signedLess=m.Output("signedLess",1);
        var rom=m.Output("rom",8);
        m.Drive(s,a.Slice(6,3));
        m.Drive(shl,a<<2);
        m.Drive(shr,a>>3);
        m.Drive(asr,a.ArithmeticShift(2));
        m.Drive(ext,a.SignExtend(16));
        m.Drive(joined,Expr.Concat(a,b));
        m.Drive(equal,a.Eq(b));
        m.Drive(less,a.Lt(b));
        m.Drive(signedLess,a.SignedLt(b));
        m.Drive(rom,Expr.Lookup(8,a.Slice(1,0),new BigInteger[]{17,33,91,255}));
        var export=m.ExportCoreGraph();
        var sim=m.Simulate();
        sim.Tick(true);
        var rng=new Random(402);
        for(int i=0;i<300;i++)
        {
            var av=rng.Next(256);
            var bv=rng.Next(256);
            sim.Set(a,av);sim.Set(b,bv);
            var result=HardwareGraphEvaluator.Evaluate(export.Graph,new Dictionary<int,BigInteger>
            {
                [export.Inputs["a"]]=av,[export.Inputs["b"]]=bv
            });
            foreach(var (name,node) in export.Outputs)
            {
                var signal=m.Ports.Single(p=>p.Name==name);
                Assert.Equal(sim.Read(signal),result[node]);
            }
        }
    }

    [Fact]
    public void Direct_dsp_expression_matches_legacy_reference()
    {
        var m=new Module("CoreDirectDsp");
        var a=m.Input("a",16);
        var b=m.Input("b",16);
        var output=m.Output("product",32);
        m.Drive(output,Dsp48E2.MultiplyUnsigned(a,b));
        var graph=m.ExportCoreGraph();
        var sim=m.Simulate();
        sim.Tick(true);
        foreach(var av in new[]{0,1,123,65535})
        foreach(var bv in new[]{0,3,419,65535})
        {
            sim.Set(a,av);sim.Set(b,bv);
            var result=HardwareGraphEvaluator.Evaluate(graph.Graph,new Dictionary<int,BigInteger>
            {
                [graph.Inputs["a"]]=av,[graph.Inputs["b"]]=bv
            });
            Assert.Equal(sim.Read(output),result[graph.Outputs["product"]]);
        }
    }

    [Fact]
    public void Ram_read_before_write_and_output_register_match_legacy()
    {
        var m=new Module("CoreRam");
        var addr=m.Input("address",2);
        var write=m.Input("write",1);
        var read=m.Input("read",1);
        var data=m.Input("data",8);
        var ram=SynchronousRam.Create(m,"ram",8,4,addr,write,data,read,
            new BigInteger[]{11,22,33,44});
        var output=m.Output("result",8);
        m.Drive(output,ram.ReadData);
        var graph=m.ExportCoreGraph();
        var sim=m.Simulate();
        sim.Tick(true);
        IReadOnlyList<BigInteger> words=graph.Memories["ram"].Initial;
        var port=graph.Memories["ram"];
        var rng=new Random(783);
        for(int edge=0;edge<180;edge++)
        {
            var address=rng.Next(4);
            var wen=edge%3==0?1:0;
            var ren=edge%7==0?0:1;
            var value=rng.Next(256);
            sim.Set(addr,address);sim.Set(write,wen);sim.Set(read,ren);sim.Set(data,value);
            var inputs=new Dictionary<int,BigInteger>
            {
                [graph.Inputs["address"]]=address,[graph.Inputs["write"]]=wen,
                [graph.Inputs["read"]]=ren,[graph.Inputs["data"]]=value,
                [graph.Inputs["ram_read_data"]]=sim.Read(ram.ReadData)
            };
            var memory=new Dictionary<string,IReadOnlyList<BigInteger>>
            {["ram"]=words};
            var values=HardwareGraphEvaluator.Evaluate(graph.Graph,inputs,memory);
            Assert.Equal(sim.Read(output),values[graph.Outputs["result"]]);
            var next=values[graph.NextState["ram_read_data"]];
            var after=HardwareMemoryTransitions.Commit(graph.Graph,graph.Memories,memory,values);
            sim.Tick();
            Assert.Equal(next,sim.Read(ram.ReadData));
            words=after["ram"];
        }
        Assert.Throws<ArgumentException>(()=>HardwareGraphEvaluator.Evaluate(
            graph.Graph,new Dictionary<int,BigInteger>(),null));
    }
    [Fact]
    public void Clocked_core_machine_matches_reference_with_registers_and_ram()
    {
        var m=new Module("CoreClockedMachine");
        var address=m.Input("address",2);
        var write=m.Input("write",1);
        var data=m.Input("data",8);
        var ram=SynchronousRam.Create(m,"ram",8,4,address,write,data,
            Expr.Const(1,1),new BigInteger[]{5,6,7,8});
        var count=m.Register("count",8,0);
        m.Next(count,count+Expr.Const(8,1));
        var result=m.Output("result",8);
        m.Drive(result,ram.ReadData+count);
        var export=m.ExportCoreGraph();
        var machine=new HardwareGraphMachine(export.Graph,export.Inputs,export.Outputs,
            export.NextState,export.ResetValues,export.Memories);
        var legacy=m.Simulate();
        legacy.Tick(true);
        for(int edge=0;edge<120;edge++)
        {
            int addr=edge%4,enable=edge%3==0?1:0,word=(edge*7)&255;
            machine.SetInput("address",addr);
            machine.SetInput("write",enable);
            machine.SetInput("data",word);
            legacy.Set(address,addr);
            legacy.Set(write,enable);
            legacy.Set(data,word);
            Assert.Equal(legacy.Read(result),machine.ReadOutput("result"));
            legacy.Tick();
            machine.Tick();
            Assert.Equal(legacy.Read(count),machine.ReadRegister("count"));
        }
        machine.Reset();
        Assert.Equal(BigInteger.Zero,machine.ReadRegister("count"));
        // Reset does not erase the previously written RAM cells.
        machine.SetInput("address",3);
        machine.SetInput("write",0);
        machine.SetInput("data",0);
        machine.Tick();
        Assert.Equal(1,machine.Cycle);
    }

}
