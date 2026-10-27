using System.Buffers.Binary;
using System.Numerics;
using SharpFuzz;
using SharpHDL.Examples;
using SharpHDL.Simulation;

// The native arithmetic oracle is deliberately outside instrumented hardware
// assemblies. SharpFuzz/AFL++ owns mutation, coverage feedback and crash retention.
// Instrumented construction must follow SharpFuzz's shared-memory initialization.
var lazyDesign = new Lazy<SharpHDL.HLS.ScheduledHardwareDesign>(Z80TestCore.Build);
Fuzzer.OutOfProcess.Run(stream =>
{
    var bytes = new byte[16]; int length = stream.ReadAtLeast(bytes, 8, throwOnEndOfStream: false);
    if (length < 8) return; // Explicit eight-byte minimum input format.
    var design = lazyDesign.Value;
    int left = bytes[0], right = bytes[1], flags = bytes[2], operation = bytes[3] & 7;
    int form = bytes[4] % 3;
    int index = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(5, 2));
    int address = (index + unchecked((sbyte)bytes[7])) & 65535;
    int pc = address <= 3 ? 0x1000 : 0; // Keep code/data distinct, including wrapping addresses.
    var memory = new Dictionary<int, int>();
    if (form == 0) memory[pc] = 0x80 | operation << 3; // ALU A,B
    else
    {
        memory[pc] = form == 1 ? 0xDD : 0xFD;
        memory[pc + 1] = 0x86 | operation << 3;
        memory[pc + 2] = bytes[7]; memory[address] = right;
    }
    var m = new HardwareGraphMachine(design.Graph, design.Inputs, design.Outputs, design.NextState, design.ResetValues, design.Memories);
    m.SetInput("init_pc", pc); m.SetInput("init_af", left << 8 | flags); m.SetInput("init_bc", right << 8);
    m.SetInput("init_ix", index); m.SetInput("init_iy", index); m.SetInput("load_state", 1); m.Tick();
    m.SetInput("load_state", 0); m.SetInput("step", 1); m.Tick(); m.SetInput("step", 0);
    for (int edge = 0; edge < 64; edge++)
    {
        var state = m.ReadOutputs();
        if (state["instruction_done"] != 0)
        {
            if ((int)state["af_out"] != Reference(left, right, (flags & 1) != 0, operation) ||
                (int)state["pc_out"] != pc + (form == 0 ? 1 : 3) || state["fault"] != 0)
                throw new InvalidOperationException("Z80 ALU/reference mismatch.");
            return;
        }
        bool ready = edge % 4 == 3;
        m.SetInput("bus_ready", ready ? 1 : 0);
        if (state["bus_valid"] != 0)
        {
            if (state["bus_write"] != 0 || state["bus_io"] != 0 || !memory.TryGetValue((int)state["bus_address"], out int value))
                throw new InvalidOperationException("Unexpected Z80 fuzz bus transaction.");
            m.SetInput("bus_rdata", value);
        }
        m.Tick();
        if (!ready && state["bus_valid"] != 0)
        {
            var after = m.ReadOutputs();
            foreach (string field in new[] { "bus_valid", "bus_address", "bus_write", "bus_wdata", "bus_io" })
                if (state[field] != after[field]) throw new InvalidOperationException("Z80 stalled request changed.");
        }
    }
    throw new InvalidOperationException("Z80 fuzz instruction watchdog expired.");
});

// Signed range and nibble arithmetic provide an independent host reference,
// rather than reproducing the hardware XOR-based carry/overflow equations.
static int Reference(int left, int right, bool carry, int operation)
{
    int ci = operation is 1 or 3 && carry ? 1 : 0;
    bool sub = operation is 2 or 3 or 7;
    int number = operation switch { 0 or 1 => left + right + ci, 2 or 3 or 7 => left - right - ci,
        4 => left & right, 5 => left ^ right, _ => left | right };
    int value = number & 255, flags = value & 0xA8;
    if (value == 0) flags |= 64;
    if (operation < 4 || operation == 7)
    {
        int half = sub ? (left & 15) - (right & 15) - ci : (left & 15) + (right & 15) + ci;
        if (half is < 0 or > 15) flags |= 16;
        int sl = left < 128 ? left : left - 256, sr = right < 128 ? right : right - 256;
        int signed = sub ? sl - sr - ci : sl + sr + ci;
        if (signed is < -128 or > 127) flags |= 4;
        if (number is < 0 or > 255) flags |= 1;
        if (sub) flags |= 2;
        if (operation == 7) flags = flags & 0xD7 | right & 0x28;
    }
    else
    {
        if (BitOperations.PopCount((uint)value) % 2 == 0) flags |= 4;
        if (operation == 4) flags |= 16;
    }
    return (operation == 7 ? left : value) << 8 | flags;
}
