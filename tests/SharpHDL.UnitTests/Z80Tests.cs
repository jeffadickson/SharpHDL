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
        int count = Z80Campaign.Run(Path.Combine(AppContext.BaseDirectory, "Z80Vectors.json"),
            (name, value) => machine.SetInput(name, value), name => (int)machine.ReadOutput(name),
            reset => { if (reset) machine.Reset(); else machine.Tick(); });
        Assert.Equal(840, count);
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

    /// <summary>Undocumented CB SLL is rejected even when its target would be RAM.</summary>
    [Theory]
    [InlineData(0x30)] [InlineData(0x36)] [InlineData(0x37)]
    public void Undocumented_cb_sll_traps_without_memory_access(int opcode)
    {
        var m = Machine(); m.SetInput("step", 1); m.Tick(); m.SetInput("step", 0);
        m.SetInput("bus_ready", 1); m.SetInput("bus_rdata", 0xCB); m.Tick(); m.Tick();
        m.SetInput("bus_rdata", opcode); m.Tick(); m.Tick();
        Assert.Equal(1, (int)m.ReadOutput("fault")); Assert.Equal(0, (int)m.ReadOutput("bus_valid"));
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

    /// <summary>Unsupported prefix/control instructions stop without performing writes.</summary>
    [Theory]
    [InlineData(0xDD)] [InlineData(0xFD)] [InlineData(0xED)]
    [InlineData(0xD3)] [InlineData(0xDB)] [InlineData(0xE3)] [InlineData(0xF3)] [InlineData(0xFB)]
    public void Unsupported_opcodes_trap_until_reset(int opcode)
    {
        var m = Machine(); m.SetInput("step", 1); m.Tick(); m.SetInput("step", 0);
        m.SetInput("bus_ready", 1); m.SetInput("bus_rdata", opcode);
        for (int edge = 0; edge < 12; edge++) m.Tick();
        Assert.Equal(1, (int)m.ReadOutput("fault"));
        Assert.Equal(opcode, (int)m.ReadOutput("opcode_out"));
        Assert.Equal(0, (int)m.ReadOutput("bus_valid"));
        Assert.Equal(0, (int)m.ReadOutput("instruction_done"));
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

