using System;
using System.Collections.Generic;
using SharpHDL.Core;
using SharpHDL.HLS;

namespace SharpHDL.Examples;

/// <summary>Dependency-aware two-bank example sharing one exact-width multiplier.</summary>
public static class ScheduledPlannedBanks
{
    /// <summary>Produce a six-edge static schedule; bank and operator names are logical resource keys.</summary>
    public static ScheduledOperationPlan Plan()=>ScheduledOperationPlan.Create(
        new[]{new ScheduledOperation("a","bank0"),new ScheduledOperation("b","bank1"),
            new ScheduledOperation("c","bank0","a"),new ScheduledOperation("d","bank1","b"),
            new ScheduledOperation("e","mul","c","d"),new ScheduledOperation("f","mul","c"),
            new ScheduledOperation("g","bank0","e","f"),new ScheduledOperation("h","bank0","g")},
        new Dictionary<string,int>{{"bank0",1},{"bank1",1},{"mul",1}});

    /// <summary>
    /// Compute (left*right + left*3) modulo 256 using synchronous bank reads and
    /// one shared combinational multiplier. Hold inputs through the first edge.
    /// Reset starts a new run; completion parks without repeating writes.
    /// </summary>
    public static ScheduledHardwareDesign Build()
    {
        var b=new ScheduledMachineBuilder("ScheduledPlannedBanks");
        var left=b.Input("left",8);var right=b.Input("right",8);
        var banks=b.BankedMemory("data",8,2,4);
        var x=b.Register("x",8);var y=b.Register("y",8);
        var p=b.Register("p",8);var q=b.Register("q",8);var result=b.Register("result_state",8);
        var mul=b.ArithmeticResource("shared_mul",HardwareOperation.Multiply,8);
        var one=b.Constant(1,1);var address=b.Constant(2,0);
        var done=Plan().EmitStates(b,"work",(state,name)=>
        {
            switch(name)
            {
                case "a":state.Write(banks.Banks[0],address,left,one);break;
                case "b":state.Write(banks.Banks[1],address,right,one);break;
                case "c":state.Read(banks.Banks[0],address,x,one);break;
                case "d":state.Read(banks.Banks[1],address,y,one);break;
                case "e":state.Assign(p,state.Compute(mul,x.Value,y.Value));break;
                case "f":state.Assign(q,state.Compute(mul,x.Value,b.Constant(8,3)));break;
                case "g":state.Write(banks.Banks[0],address,p.Value.Add(q.Value),one);break;
                case "h":state.Read(banks.Banks[0],address,result,one);break;
                default:throw new InvalidOperationException("Unknown planned operation.");
            }
        });
        b.Output("result",result.Value);b.Output("done",b.InState(done));return b.Lower();
    }
}
