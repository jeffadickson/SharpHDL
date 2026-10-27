using SharpHDL.Examples;
using SharpHDL.HLS;
using SharpHDL.Simulation;
using SharpHDL.Verification;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Z80 instructions execute in generated hardware rather than a host emulator.</summary>
public sealed class Z80Tests
{
    private static readonly Lazy<ScheduledHardwareDesign> Design = new(Z80TestCore.Build);
    private static HardwareGraphMachine Machine()
    {
        var d = Design.Value;
        return new(d.Graph, d.Inputs, d.Outputs, d.NextState, d.ResetValues, d.Memories);
    }

    /// <summary>Pinned independent state/RAM vectors, including arbitrary initial flags and stalls.</summary>
    [Fact]
    public void Published_instruction_vectors_match_architectural_results()
    {
        var machine = Machine();
        int count = Z80Campaign.Run(Path.Combine(AppContext.BaseDirectory, "Z80Vectors.json"), machine);
        Assert.Equal(840, count);
    }

    /// <summary>DD/FD forms include signed displacements, H/L exceptions and indexed CB copies.</summary>
    [Fact]
    public void Indexed_instruction_vectors_match_independent_results()
    {
        var m = Machine();
        int count = Z80Campaign.Run(Path.Combine(AppContext.BaseDirectory, "Z80IndexedVectors.json"), m);
        Assert.True(count >= 900);
    }

    /// <summary>All ED corpus files plus immediate I/O check independent state/RAM/port results.</summary>
    [Fact]
    public void Ed_and_io_vectors_match_independent_results()
        => Assert.Equal(672, Z80Campaign.Run(Path.Combine(AppContext.BaseDirectory, "Z80EdVectors.json"), Machine()));

    /// <summary>Repeat-path flags and restart PCs deserve a separately replayable regression.</summary>
    [Fact]
    public void Ed_repeat_vectors_match_independent_results()
        => Assert.Equal(64, Z80Campaign.Run(Path.Combine(AppContext.BaseDirectory, "Z80EdVectors.json"), Machine(), "ED B"));

    /// <summary>Every base opcode and every CB opcode has independent sampled state/RAM expectations.</summary>
    [Fact]
    public void Every_base_and_cb_opcode_has_independent_vectors()
        => Assert.Equal(1016, Z80Campaign.Run(Path.Combine(AppContext.BaseDirectory, "Z80CompleteVectors.json"), Machine()));

    /// <summary>The last repeated prefix selects the index and every prefix increments low-seven-bit R.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Repeated_index_prefixes_select_the_last_register(bool inverse)
    {
        var m = Machine(); m.SetInput("init_ix", 65535); m.SetInput("init_iy", 1);
        m.SetInput("init_r", 254); m.SetInput("load_state", 1); m.Tick(); m.SetInput("load_state", 0);
        m.SetInput("step", 1); m.Tick(); m.SetInput("step", 0);
        byte[] bytes = { inverse ? (byte)0xFD : (byte)0xDD, inverse ? (byte)0xDD : (byte)0xFD, 0x21, 0x34, 0x12 };
        for (int edge = 0; edge < 32 && m.ReadOutput("instruction_done") == 0; edge++)
        {
            m.SetInput("bus_ready", 1);
            if (m.ReadOutput("bus_valid") != 0) m.SetInput("bus_rdata", bytes[(int)m.ReadOutput("bus_address")]);
            m.Tick();
        }
        Assert.Equal(1, (int)m.ReadOutput("instruction_done")); Assert.Equal(5, (int)m.ReadOutput("pc_out"));
        Assert.Equal(129, (int)m.ReadOutput("r_out"));
        Assert.Equal(inverse ? 0x1234 : 65535, (int)m.ReadOutput("ix_out"));
        Assert.Equal(inverse ? 1 : 0x1234, (int)m.ReadOutput("iy_out"));
    }

    /// <summary>Interrupt mode, priority, EI/HALT and stalled-write scenarios have independent expectations.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void Interrupts_follow_architectural_boundaries(int scenario)
    {
        var m = Machine(); IReadOnlyDictionary<string, System.Numerics.BigInteger>? snapshot = null;
        Z80InterruptCampaign.Run(scenario, (n, v) => { m.SetInput(n, v); snapshot = null; },
            n => (int)(snapshot ??= m.ReadOutputs())[n],
            reset => { if (reset) m.Reset(); else m.Tick(); snapshot = null; });
    }

    /// <summary>Reserved debug IM state rejects rather than entering an unserviceable acknowledge.</summary>
    [Fact]
    public void Reserved_interrupt_mode_faults_and_reset_recovers()
    {
        var m = Machine(); m.SetInput("init_im", 3); m.SetInput("load_state", 1); m.Tick();
        Assert.Equal(1, (int)m.ReadOutput("fault")); Assert.Equal(0, (int)m.ReadOutput("bus_valid"));
        m.SetInput("load_state", 0); m.Reset(); Assert.Equal(0, (int)m.ReadOutput("fault"));
    }

    /// <summary>Batched observations equal individual reads and remain immutable across ticks/reset.</summary>
    [Fact]
    public void Batched_outputs_are_consistent_immutable_snapshots()
    {
        var m = Machine(); var before = m.ReadOutputs();
        foreach (var pair in before) Assert.Equal(pair.Value, m.ReadOutput(pair.Key));
        m.SetInput("step", 1); m.Tick(); var after = m.ReadOutputs();
        Assert.Equal(0, (int)before["bus_valid"]); Assert.Equal(1, (int)after["bus_valid"]);
        m.Reset(); Assert.Equal(1, (int)after["bus_valid"]); Assert.Equal(0, (int)m.ReadOutput("bus_valid"));
    }

    /// <summary>Actual machine-code loop composes instructions, stack operations and memory writes.</summary>
    [Fact]
    public void Machine_code_program_composes_loop_call_ram_and_halt()
    {
        var m = Machine();
        Assert.InRange(Z80Campaign.RunProgram((n, v) => m.SetInput(n, v), n => (int)m.ReadOutput(n),
            reset => { if (reset) m.Reset(); else m.Tick(); }), 100, 2047);
    }

    /// <summary>Independent integer/signed-range oracle covers random ALU inputs and carry.</summary>
    [FsCheck.Xunit.Property(MaxTest = 64, Replay = "1145655947,296144285")]
    public void Arithmetic_flags_match_integer_reference(byte left, byte right, bool carry)
    {
        for (int operation = 0; operation < 8; operation++)
        {
            var m = Machine();
            m.SetInput("init_af", (left << 8) | (carry ? 1 : 0));
            m.SetInput("init_bc", right << 8); m.SetInput("load_state", 1); m.Tick();
            m.SetInput("load_state", 0); m.SetInput("step", 1); m.Tick(); m.SetInput("step", 0);
            m.SetInput("bus_ready", 1); m.SetInput("bus_rdata", 0x80 | operation << 3);
            for (int edge = 0; edge < 3; edge++) m.Tick();
            Assert.Equal(1, (int)m.ReadOutput("instruction_done"));
            int ci = operation is 1 or 3 && carry ? 1 : 0;
            bool subtract = operation is 2 or 3 or 7;
            int number = operation switch
            {
                0 or 1 => left + right + ci,
                2 or 3 or 7 => left - right - ci,
                4 => left & right, 5 => left ^ right, _ => left | right
            };
            int value = number & 255;
            int flags = value & 0xA8;
            if (value == 0) flags |= 0x40;
            if (operation < 4 || operation == 7)
            {
                int half = subtract ? (left & 15) - (right & 15) - ci : (left & 15) + (right & 15) + ci;
                if (half is < 0 or > 15) flags |= 0x10;
                int signedLeft = left < 128 ? left : left - 256;
                int signedRight = right < 128 ? right : right - 256;
                int signed = subtract ? signedLeft - signedRight - ci : signedLeft + signedRight + ci;
                if (signed is < -128 or > 127) flags |= 4;
                if (number is < 0 or > 255) flags |= 1;
                if (subtract) flags |= 2;
                if (operation == 7) flags = (flags & 0xD7) | (right & 0x28);
            }
            else
            {
                if (System.Numerics.BitOperations.PopCount((uint)value) % 2 == 0) flags |= 4;
                if (operation == 4) flags |= 16;
            }
            Assert.Equal(((operation == 7 ? left : value) << 8) | flags, (int)m.ReadOutput("af_out"));
        }
    }

    /// <summary>Common NMOS SLL behavior shifts in one, for registers and memory.</summary>
    [Theory]
    [InlineData(0x30)] [InlineData(0x36)] [InlineData(0x37)]
    public void Cb_sll_shifts_in_one(int opcode)
    {
        var m = Machine(); m.SetInput("step", 1); m.Tick(); m.SetInput("step", 0);
        m.SetInput("bus_ready", 1); m.SetInput("bus_rdata", 0xCB); m.Tick(); m.Tick();
        m.SetInput("bus_rdata", opcode); m.Tick(); m.Tick();
        for (int edge = 0; edge < 5 && m.ReadOutput("instruction_done") == 0; edge++) m.Tick();
        Assert.Equal(0, (int)m.ReadOutput("fault")); Assert.Equal(1, (int)m.ReadOutput("instruction_done"));
    }

    /// <summary>CALL straddles FFFF/0000 and pushes a return address across SP zero.</summary>
    [Fact]
    public void Stack_and_immediate_addresses_wrap_at_16_bits()
    {
        var m = Machine(); var memory = new byte[65536];
        memory[65535] = 0xCD; memory[0] = 0x34; memory[1] = 0x12;
        m.SetInput("init_pc", 65535); m.SetInput("init_sp", 1); m.SetInput("load_state", 1); m.Tick();
        m.SetInput("load_state", 0); m.SetInput("step", 1); m.Tick(); m.SetInput("step", 0);
        var reads = new List<int>(); var writes = new List<int>();
        for (int edge = 0; edge < 32 && m.ReadOutput("instruction_done") == 0; edge++)
        {
            m.SetInput("bus_ready", 1);
            if (m.ReadOutput("bus_valid") != 0)
            {
                int address = (int)m.ReadOutput("bus_address");
                if (m.ReadOutput("bus_write") != 0) { writes.Add(address); memory[address] = (byte)m.ReadOutput("bus_wdata"); }
                else { reads.Add(address); m.SetInput("bus_rdata", memory[address]); }
            }
            m.Tick();
        }
        Assert.Equal(1, (int)m.ReadOutput("instruction_done"));
        Assert.Equal(new[] { 65535, 0, 1 }, reads); Assert.Equal(new[] { 0, 65535 }, writes);
        Assert.Equal(0x1234, (int)m.ReadOutput("pc_out")); Assert.Equal(65535, (int)m.ReadOutput("sp_out"));
        Assert.Equal(0, memory[0]); Assert.Equal(2, memory[65535]);
    }

    /// <summary>Undefined ED encodings behave as Z80 two-byte NOPs.</summary>
    [Theory]
    [InlineData(0x00)] [InlineData(0xFF)] [InlineData(0xED)]
    public void Undefined_ed_encodings_are_two_byte_nops(int opcode)
    {
        var m = Machine(); m.SetInput("step", 1); m.Tick(); m.SetInput("step", 0);
        m.SetInput("bus_ready", 1); m.SetInput("bus_rdata", 0xED); m.Tick(); m.Tick();
        m.SetInput("bus_rdata", opcode);
        for (int edge = 0; edge < 12 && m.ReadOutput("instruction_done") == 0; edge++) m.Tick();
        Assert.Equal(0, (int)m.ReadOutput("fault"));
        Assert.Equal(0, (int)m.ReadOutput("bus_valid"));
        Assert.Equal(1, (int)m.ReadOutput("instruction_done"));
        Assert.Equal(2, (int)m.ReadOutput("pc_out")); Assert.Equal(2, (int)m.ReadOutput("r_out"));
        m.Reset(); Assert.Equal(0, (int)m.ReadOutput("fault"));
        Assert.Equal(0, (int)m.ReadOutput("pc_out")); Assert.Equal(65535, (int)m.ReadOutput("sp_out"));
    }

    /// <summary>Reset cancels a stalled write; HALT stops fetch until debug load/reset.</summary>
    [Fact]
    public void Reset_aborts_stalled_store_and_halt_stops_fetch()
    {
        var m = Machine(); m.SetInput("step", 1); m.Tick();
        m.SetInput("bus_ready", 1); m.SetInput("bus_rdata", 0x02);
        for (int edge = 0; edge < 3; edge++) m.Tick();
        Assert.Equal(1, (int)m.ReadOutput("bus_write"));
        m.SetInput("bus_ready", 0); m.Tick(); m.Reset();
        Assert.Equal(0, (int)m.ReadOutput("bus_valid"));
        m.SetInput("step", 1); m.Tick(); m.SetInput("bus_ready", 1); m.SetInput("bus_rdata", 0x76);
        for (int edge = 0; edge < 12; edge++) m.Tick();
        Assert.Equal(1, (int)m.ReadOutput("halted_out"));
        Assert.Equal(0, (int)m.ReadOutput("bus_valid"));
        Assert.Equal(1, (int)m.ReadOutput("pc_out"));
    }
}
