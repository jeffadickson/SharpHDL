using SharpHDL.HLS;
using SharpHDL.Simulation;

namespace SharpHDL.Verification;

/// <summary>Fixed 256-edge workload shared by correctness tests and JIT/Native AOT benchmarks.</summary>
public static class SimulatorObservationWorkload
{
    /// <summary>Deterministic checksum of the independently specified four-edge table protocol.</summary>
    public static ulong Expected()
    {
        ulong sum=14695981039346656037UL;
        for(int edge=1;edge<=256;edge++)sum=Mix(sum,edge%4==0?1:0,edge%4==3?1:0,(uint)((edge+1)/4));
        return sum;
    }
    /// <summary>Construct a fresh machine, run 256 edges and observe all ports using the selected existing API.</summary>
    public static ulong Run(ScheduledHardwareDesign design,bool snapshot)
    {
        var sim=new HardwareGraphMachine(design.Graph,design.Inputs,design.Outputs,design.NextState,design.ResetValues,design.Memories);
        sim.SetInput("request_valid",1);sim.SetInput("request_update",1);sim.SetInput("request_delta",1);sim.SetInput("response_ready",1);
        ulong sum=14695981039346656037UL;
        for(int edge=0;edge<256;edge++)
        {
            sim.Tick();int ready,valid;uint data;
            if(snapshot)
            {
                var values=sim.ReadOutputs();ready=(int)values["request_ready"];valid=(int)values["response_valid"];data=(uint)values["response_data"];
            }
            else{ready=(int)sim.ReadOutput("request_ready");valid=(int)sim.ReadOutput("response_valid");data=(uint)sim.ReadOutput("response_data");}
            sum=Mix(sum,ready,valid,data);
        }
        if(sum!=Expected()||sim.ReadOutput("response_data")!=64)throw new InvalidOperationException("Simulator baseline violates independent protocol/checksum.");
        return sum;
    }
    private static ulong Mix(ulong sum,int ready,int valid,uint data)=>unchecked((sum^(data|((ulong)(uint)valid<<32)|((ulong)(uint)ready<<33)))*1099511628211UL);
}
