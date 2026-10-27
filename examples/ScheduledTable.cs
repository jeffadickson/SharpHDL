using System;
using SharpHDL.HLS;

namespace SharpHDL.Examples;

/// <summary>A hand-scheduled ready/valid table with conditional modulo-16-bit addition.</summary>
public static class ScheduledTable
{
    /// <summary>
    /// Build a table with 2^addressWidth zero-initialized words. Each accepted request
    /// returns the selected word, adding delta and committing it first when update=1.
    /// Reset aborts the pending response and preserves already committed table words.
    /// </summary>
    public static ScheduledHardwareDesign Build(int addressWidth = 4)
    {
        if (addressWidth is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(addressWidth));
        var b = new ScheduledMachineBuilder("ScheduledTable" + addressWidth);
        var valid = b.Input("request_valid", 1);
        var address = b.Input("request_address", addressWidth);
        var delta = b.Input("request_delta", 16);
        var update = b.Input("request_update", 1);
        var ready = b.Input("response_ready", 1);
        var key = b.Register("key", addressWidth);
        var amount = b.Register("amount", 16);
        var write = b.Register("write", 1);
        var word = b.Register("word", 16);
        var result = b.Register("result", 16);
        var table = b.Memory("table", 16, 1 << addressWidth);
        var idle = b.State("idle");
        var read = b.State("read");
        var commit = b.State("commit");
        var respond = b.State("respond");
        var one = b.Constant(1, 1);
        idle.Assign(key, address, valid).Assign(amount, delta, valid).Assign(write, update, valid)
            .Transition("read", valid);
        read.Read(table, key.Value, word, one).Transition("commit");
        var next = ScheduledValue.Select(write.Value, word.Value.Add(amount.Value), word.Value);
        commit.Write(table, key.Value, next, write.Value).Assign(result, next).Transition("respond");
        respond.Transition("idle", ready);
        b.Output("request_ready", b.InState(idle));
        b.Output("response_valid", b.InState(respond));
        b.Output("response_data", result.Value);
        return b.Lower();
    }
}
