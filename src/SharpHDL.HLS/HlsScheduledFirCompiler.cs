using System;
using System.Linq;

namespace SharpHDL.HLS;

/// <summary>
/// Maps a resource-constrained FIR schedule onto the reusable scheduled-machine
/// representation. FIR policy lives here; state-machine and Core IR lowering do not.
/// </summary>
public static class HlsScheduledFirCompiler
{
    /// <summary>
    /// Compile a 16-bit unsigned streaming FIR with a shared multiplier and one
    /// synchronous single-port history RAM into canonical Core IR.
    /// </summary>
    /// <param name="schedule">Cycle-accurate FIR memory schedule.</param>
    /// <returns>A backend-neutral scheduled Core IR design.</returns>
    public static ScheduledHardwareDesign Compile(HlsSinglePortFirSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        int taps=schedule.Taps;
        if(taps<2||taps>16)throw new ArgumentOutOfRangeException(nameof(schedule),
            "The current FIR mapping supports 2..16 taps.");
        ValidateSchedule(schedule);
        int addressWidth=1,depth=2;
        while(depth<taps){depth*=2;addressWidth++;}
        int countWidth=1;while((1<<countWidth)<taps)countWidth++;

        var machine=new ScheduledMachineBuilder("HlsScheduledFir"+taps);
        var valid=machine.Input("s_axis_tvalid",1);
        var data=machine.Input("s_axis_tdata",16);
        var inputLast=machine.Input("s_axis_tlast",1);
        var inputKeep=machine.Input("s_axis_tkeep",2);
        var inputUser=machine.Input("s_axis_tuser",8);
        var ready=machine.Input("m_axis_tready",1);
        var coefficients=Enumerable.Range(0,taps)
            .Select(i=>machine.Input("coefficient"+i,16)).ToArray();
        var captured=Enumerable.Range(0,taps)
            .Select(i=>machine.Register("captured_coefficient"+i,16)).ToArray();
        var head=machine.Register("fir_head",addressWidth);
        var count=machine.Register("fir_history_count",countWidth);
        var result=machine.Register("fir_result",16);
        var last=machine.Register("fir_last",1);
        var keep=machine.Register("fir_keep",2);
        var user=machine.Register("fir_user",8);
        var readData=machine.Register("fir_read_data",16);
        var memory=machine.Memory("fir_history",16,depth);
        var multiplier=machine.Resource("fir_multiplier");
        var states=schedule.Steps.Select(step=>machine.State("cycle_"+step.Cycle)).ToArray();
        var zero16=machine.Constant(16,0);

        // Explicit operand arbitration maps every mutually exclusive FIR
        // multiply onto one canonical Core IR operation.
        ScheduledValue multiplicand=data,coefficient=coefficients[0];
        foreach(var step in schedule.Steps.Where(s=>s.Action==HlsMemoryAction.Accumulate))
        {
            var active=machine.InState(states[step.Cycle]);
            var tap=machine.Constant(countWidth,step.Tap);
            var history=ScheduledValue.Select(count.Value.LessThan(tap).Not(),readData.Value,zero16);
            multiplicand=ScheduledValue.Select(active,history,multiplicand);
            coefficient=ScheduledValue.Select(active,captured[step.Tap].Value,coefficient);
        }
        var sharedProduct=multiplicand.Multiply(coefficient);

        var accept=states[0];
        accept.Write(memory,head.Value,data,valid)
            .Use(multiplier)
            .Assign(result,sharedProduct,valid)
            .Assign(head,head.Value.Add(machine.Constant(addressWidth,1)),valid)
            .Assign(last,inputLast,valid).Assign(keep,inputKeep,valid).Assign(user,inputUser,valid)
            .Transition(states[1].Name,valid);
        for(int i=0;i<taps;i++)accept.Assign(captured[i],coefficients[i],valid);

        for(int index=1;index<schedule.Steps.Count-1;index++)
        {
            var step=schedule.Steps[index];var state=states[index];
            if(step.Action==HlsMemoryAction.IssueRead)
            {
                var tap=machine.Constant(countWidth,step.Tap);
                var historyValid=count.Value.LessThan(tap).Not();
                var address=head.Value.Subtract(machine.Constant(addressWidth,(step.Tap+1)%depth));
                state.Read(memory,address,readData,historyValid).Transition(states[index+1].Name);
            }
            else if(step.Action==HlsMemoryAction.Accumulate)
            {
                state.Use(multiplier).Assign(result,result.Value.Add(sharedProduct))
                    .Transition(states[index+1].Name);
            }
            else throw new InvalidOperationException("Unexpected operation in FIR body schedule.");
        }

        var output=states[^1];
        var saturated=count.Value.Equal(machine.Constant(countWidth,taps-1));
        output.Assign(count,ScheduledValue.Select(saturated,count.Value,
                count.Value.Add(machine.Constant(countWidth,1))),ready)
            .Transition(states[0].Name,ready);

        machine.Output("s_axis_tready",machine.InState(accept));
        machine.Output("m_axis_tvalid",machine.InState(output));
        machine.Output("m_axis_tdata",result.Value);
        machine.Output("m_axis_tlast",last.Value);
        machine.Output("m_axis_tkeep",keep.Value);
        machine.Output("m_axis_tuser",user.Value);
        return machine.Lower();
    }

    private static void ValidateSchedule(HlsSinglePortFirSchedule schedule)
    {
        int taps=schedule.Taps;
        if(schedule.InitiationInterval!=2*taps||schedule.Steps.Count!=2*taps||
           schedule.Steps[0]!=new HlsMemoryStep(0,HlsMemoryAction.AcceptAndWrite,0)||
           schedule.Steps[^1]!=new HlsMemoryStep(2*taps-1,HlsMemoryAction.Output,-1))
            throw new ArgumentException("Unsupported FIR schedule shape.",nameof(schedule));
        for(int tap=1;tap<taps;tap++)
            if(schedule.Steps[2*tap-1]!=new HlsMemoryStep(2*tap-1,HlsMemoryAction.IssueRead,tap)||
               schedule.Steps[2*tap]!=new HlsMemoryStep(2*tap,HlsMemoryAction.Accumulate,tap))
                throw new ArgumentException("Unsupported FIR RAM schedule ordering.",nameof(schedule));
    }
}
