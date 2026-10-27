using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Numerics;
using System.Linq;
using SharpHDL.Simulation;

namespace SharpHDL.Verification;

/// <summary>Independent published instruction-state oracle and hostile bus stalls.</summary>
public static class Z80Campaign
{
    /// <summary>Use batched immutable output snapshots for managed execution.</summary>
    public static int Run(string vectorFile, HardwareGraphMachine machine, string? prefix = null)
    {
        IReadOnlyDictionary<string, BigInteger>? snapshot = null;
        return Run(vectorFile, (n, v) => { machine.SetInput(n, v); snapshot = null; },
            n => (int)(snapshot ??= machine.ReadOutputs())[n],
            reset => { if (reset) machine.Reset(); else machine.Tick(); snapshot = null; }, prefix: prefix);
    }
    /// <summary>Run a real multi-instruction loop, subroutine, RAM stores and HALT.</summary>
    public static int RunProgram(Action<string, int> drive, Func<string, int> read, Action<bool> clock)
    {
        var memory = new byte[65536];
        // LD SP,FF00; LD HL,8000; LD B,10; XOR A; ADD A,B; DJNZ -3;
        // CALL 0020; HALT. Subroutine: LD(HL),A; INC HL; LD(HL),A5; RET.
        byte[] main = { 0x31, 0x00, 0xFF, 0x21, 0x00, 0x80, 0x06, 10, 0xAF, 0x80, 0x10, 0xFD, 0xCD, 0x20, 0, 0x76 };
        byte[] sub = { 0x77, 0x23, 0x36, 0xA5, 0xC9 };
        main.CopyTo(memory, 0); sub.CopyTo(memory, 0x20);
        drive("load_state", 0); drive("step", 1); drive("bus_ready", 0); clock(true);
        int retired = 0;
        for (int edge = 0; edge < 2048; edge++)
        {
            if (read("fault") != 0) throw new InvalidOperationException("Program trapped.");
            if (read("instruction_done") != 0) retired++;
            if (read("halted_out") != 0 && read("idle") != 0)
            {
                if (retired != 30 || memory[0x8000] != 55 || memory[0x8001] != 0xA5 ||
                    memory[0xFEFE] != 15 || memory[0xFEFF] != 0 || read("pc_out") != 16 ||
                    read("sp_out") != 0xFF00 || read("hl_out") != 0x8001 || read("bc_out") != 0)
                    throw new InvalidOperationException("Program architectural/RAM result mismatch.");
                return edge;
            }
            bool accept = edge % 4 == 3;
            drive("bus_ready", accept ? 1 : 0);
            if (read("bus_valid") != 0)
            {
                int address = read("bus_address");
                if (read("bus_write") == 0) drive("bus_rdata", memory[address]);
                else if (accept) memory[address] = checked((byte)read("bus_wdata"));
            }
            clock(false);
        }
        throw new InvalidOperationException("Program watchdog expired.");
    }

    /// <summary>
    /// Replay pinned upstream instruction vectors through public debug/bus ports.
    /// Only documented flags are compared for BIT(HL), SCF and CCF; upstream also
    /// models undocumented WZ/Q-dependent flags which this test core does not.
    /// Return the number of instructions checked; throw with the vector name on failure.
    /// </summary>
    public static int Run(string vectorFile, Action<string, int> drive, Func<string, int> read,
        Action<bool> clock, int maximum = int.MaxValue, string? prefix = null)
    {
        using var vectors = JsonDocument.Parse(File.ReadAllText(vectorFile));
        int count = 0;
        foreach (var vector in vectors.RootElement.EnumerateArray())
        {
            if (count == maximum) break;
            string name = vector.GetProperty("name").GetString()!;
            if (prefix != null && !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var initial = vector.GetProperty("initial"); var final = vector.GetProperty("final");
            int I(string key) => initial.GetProperty(key).GetInt32();
            int F(string key) => final.GetProperty(key).GetInt32();
            var memory = new Dictionary<int, int>();
            foreach (var cell in initial.GetProperty("ram").EnumerateArray()) memory[cell[0].GetInt32()] = cell[1].GetInt32();
            clock(true); drive("step", 0); drive("bus_ready", 0); drive("load_state", 1);
            foreach (string pair in new[] { "af", "bc", "de", "hl" })
                drive("init_" + pair, (I(pair[..1]) << 8) | I(pair[1..]));
            drive("init_pc", I("pc")); drive("init_sp", I("sp"));
            drive("init_ix", I("ix")); drive("init_iy", I("iy"));
            drive("init_i", I("i")); drive("init_r", I("r"));
            drive("init_iff1", I("iff1")); drive("init_iff2", I("iff2"));
            drive("init_im", I("im")); drive("init_ei_delay", I("ei"));
            var portAccesses = vector.TryGetProperty("ports", out var portList) ? portList.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
            int portIndex = 0;
            foreach (var (pair, index) in new[] { ("bc_", 0), ("de_", 1), ("hl_", 2), ("af_", 3) })
                drive("init_alt" + index, I(pair));
            clock(false); drive("load_state", 0); drive("step", 1); clock(false); drive("step", 0);
            bool done = false;
            for (int edge = 0; edge < 128; edge++)
            {
                if (read("fault") != 0) throw new InvalidOperationException(name + ": unexpected opcode fault");
                if (read("instruction_done") != 0) { done = true; break; }
                int valid = read("bus_valid");
                int addr = read("bus_address"); int write = read("bus_write"); int value = read("bus_wdata");
                // Three stalled edges out of every five; includes fetch, operands,
                // read/modify/write and both stack bytes. Completion remains bounded.
                bool accept = edge % 5 >= 3;
                drive("bus_ready", accept ? 1 : 0);
                if (valid != 0)
                {
                    if (read("bus_io") != 0)
                    {
                        if (portIndex >= portAccesses.Length) throw new InvalidOperationException(name + ": unexpected port access");
                        var port = portAccesses[portIndex];
                        if (addr != port[0].GetInt32() || (write == 0 ? "r" : "w") != port[2].GetString())
                            throw new InvalidOperationException($"{name}: wrong port address/direction {addr:X4}");
                        if (write == 0) drive("bus_rdata", port[1].GetInt32());
                        else if (value != port[1].GetInt32()) throw new InvalidOperationException(name + ": wrong port write byte");
                        if (accept) portIndex++;
                    }
                    else if (write == 0)
                    {
                        if (!memory.TryGetValue(addr, out int datum))
                            throw new InvalidOperationException($"{name}: unexpected read {addr:X4}");
                        drive("bus_rdata", datum);
                    }
                    else if (accept) memory[addr] = value;
                }
                clock(false);
                if (valid != 0 && !accept && (read("bus_valid") != 1 || read("bus_address") != addr ||
                    read("bus_write") != write || read("bus_wdata") != value))
                    throw new InvalidOperationException(name + ": stalled bus request changed");
            }
            if (!done) throw new InvalidOperationException(name + ": instruction watchdog expired");
            if (portIndex != portAccesses.Length) throw new InvalidOperationException(name + ": missing port transaction");
            void Check(string port, int expected, int mask = 65535)
            {
                int actual = read(port);
                if ((actual & mask) != (expected & mask))
                    throw new InvalidOperationException($"{name}: {port}={actual:X4}, expected {expected:X4}, mask={mask:X4}");
            }
            Check("pc_out", F("pc")); Check("sp_out", F("sp"));
            Check("ix_out", F("ix")); Check("iy_out", F("iy"));
            Check("i_out", F("i")); Check("r_out", F("r"));
            Check("iff1_out", F("iff1")); Check("iff2_out", F("iff2")); Check("im_out", F("im"));
            foreach (string pair in new[] { "bc", "de", "hl" }) Check(pair + "_out", (F(pair[..1]) << 8) | F(pair[1..]));
            int flagMask = name.StartsWith("CB ", StringComparison.OrdinalIgnoreCase) &&
                (memory[I("pc")] == 0xCB && (memory[(I("pc") + 1) & 65535] & 0xC7) == 0x46)
                || memory[I("pc")] is 0x37 or 0x3F ? 0xFFD7 : 0xFFFF;
            Check("af_out", (F("a") << 8) | F("f"), flagMask);
            foreach (var (pair, index) in new[] { ("bc_", 0), ("de_", 1), ("hl_", 2), ("af_", 3) })
                Check("alt_out" + index, F(pair));
            foreach (var cell in final.GetProperty("ram").EnumerateArray())
                if (!memory.TryGetValue(cell[0].GetInt32(), out int actual) || actual != cell[1].GetInt32())
                    throw new InvalidOperationException(name + ": RAM mismatch at " + cell[0].GetInt32().ToString("X4"));
            count++;
        }
        return count;
    }
}
