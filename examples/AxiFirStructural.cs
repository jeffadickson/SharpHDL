using SharpHDL;

namespace SharpHDL.Examples;

/// <summary>
/// Structural four-tap, modulo-2^16 FIR with one output register.
/// The three historical samples are registers; a BRAM-backed variant
/// requires a separate multicycle memory-access scheduler.
/// </summary>
public static class AxiFirStructural
{
    /// <summary>Build a one-sample-per-clock-capable AXI4-Stream FIR datapath.</summary>
    public static Module Build()
    {
        var m = new Module("AxiFirStructural");
        var input = Stream.Input(m, "s_axis", 16, 8);
        var output = Stream.Output(m, "m_axis", 16, 8);
        var c0 = m.Input("coefficient0", 16);
        var c1 = m.Input("coefficient1", 16);
        var c2 = m.Input("coefficient2", 16);
        var c3 = m.Input("coefficient3", 16);
        var h1 = m.Register("history1", 16, 0);
        var h2 = m.Register("history2", 16, 0);
        var h3 = m.Register("history3", 16, 0);
        var result = m.Register("result", 16, 0);
        var resultValid = m.Register("result_valid", 1, 0);
        var resultLast = m.Register("result_last", 1, 0);
        var resultUser = m.Register("result_user", 8, 0);
        var resultKeep = m.Register("result_keep", 2, 0);

        // A registered output is elastic: when stalled, data and sidebands hold.
        var canAdvance = (~resultValid) | output.Ready;
        m.Drive(input.Ready, canAdvance);
        var accept = input.Valid & canAdvance;
        var sample = input.Data;
        var sum = sample * c0 + h1 * c1 + h2 * c2 + h3 * c3;
        m.NextWhen(h3, accept, h2);
        m.NextWhen(h2, accept, h1);
        m.NextWhen(h1, accept, sample);
        m.NextWhen(result, accept, sum);
        m.NextWhen(resultLast, accept, input.Last);
        m.NextWhen(resultUser, accept, input.User);
        m.NextWhen(resultKeep, accept, input.Keep);
        m.NextWhen(resultValid, canAdvance, input.Valid);
        m.Drive(output.Data, result);
        m.Drive(output.Valid, resultValid);
        m.Drive(output.Last, resultLast);
        m.Drive(output.User, resultUser);
        m.Drive(output.Keep, resultKeep);
        return m;
    }
}
