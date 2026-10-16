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
        return BuildDatapath(m, new Expr[] { m.Input("coefficient0", 16),
            m.Input("coefficient1", 16), m.Input("coefficient2", 16),
            m.Input("coefficient3", 16) });
    }

    /// <summary>
    /// Build the same FIR datapath with AXI4-Lite software-writable coefficients.
    /// Four 32-bit registers at byte offsets 0, 4, 8 and 12 hold unsigned
    /// coefficients in bits 15:0; upper bits are reserved and ignored.
    /// The initial impulse response is [1,0,0,0]. Changes affect only
    /// subsequently accepted AXI4-Stream samples.
    /// </summary>
    public static Module BuildWithAxiLite()
    {
        var m = new Module("AxiFirConfigured");
        var port = AxiLitePort.Slave(m, "s_axi", 8);
        var bank = new AxiLiteRegisterBank(m, port, "fir_csr");
        var coefficients = new Expr[4];
        for (int i = 0; i < coefficients.Length; i++)
            coefficients[i] = bank.AddRegister("coefficient" + i,
                (uint)(4 * i), i == 0 ? 1u : 0u).Value.Slice(15, 0);
        bank.Build();
        return BuildDatapath(m, coefficients);
    }

    /// <summary>
    /// Build a FIR with double-buffered AXI4-Lite coefficients. Software writes
    /// staging words at 0x00..0x0C, then writes any nonzero byte strobe to the
    /// APPLY doorbell at 0x10. All four active coefficients update together
    /// on the doorbell's write-commit edge. Read-only active values appear at
    /// 0x20..0x2C. Staged writes never alter the running filter.
    /// </summary>
    /// <remarks>
    /// A simultaneous sample handshake and APPLY edge uses the old active
    /// coefficient bank because both registers sample pre-edge values.
    /// The next accepted sample uses the new bank. APPLY snapshots staging
    /// values as they existed before the commit edge. Do not issue a staging
    /// write and APPLY in the same outstanding AXI write transaction.
    /// </remarks>
    public static Module BuildWithAtomicAxiLite()
    {
        var m = new Module("AxiFirAtomicConfigured");
        var port = AxiLitePort.Slave(m, "s_axi", 8);
        var bank = new AxiLiteRegisterBank(m, port, "fir_csr");
        var staging = new Expr[4];
        var active = new Signal[4];
        for (int i = 0; i < 4; i++)
        {
            uint initial = i == 0 ? 1u : 0u;
            staging[i] = bank.AddRegister("staging" + i,
                (uint)(4 * i), initial).Value.Slice(15, 0);
            active[i] = m.Register("active_coefficient" + i, 16, initial);
            bank.AddStatus("active" + i, (uint)(0x20 + 4 * i),
                active[i].Resize(32));
        }
        var apply = bank.AddRegister("apply", 0x10);
        for (int i = 0; i < 4; i++)
            m.NextWhen(active[i], apply.Written, staging[i]);
        bank.Build();
        return BuildDatapath(m, active);
    }

    /// <summary>
    /// Shared structural datapath for external and AXI4-Lite coefficients.
    /// The coefficient expressions are sampled combinationally only on
    /// accepted input transactions, with registered output isolation.
    /// </summary>
    private static Module BuildDatapath(Module m, Expr[] coefficients)
    {
        var input = Stream.Input(m, "s_axis", 16, 8);
        var output = Stream.Output(m, "m_axis", 16, 8);
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
        var sum = sample * coefficients[0] + h1 * coefficients[1] +
            h2 * coefficients[2] + h3 * coefficients[3];
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
