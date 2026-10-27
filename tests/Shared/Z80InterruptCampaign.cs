using System;
using System.Collections.Generic;

namespace SharpHDL.Verification;

/// <summary>Architecturally specified interrupt scenarios reusable in managed and RTL replay.</summary>
public static class Z80InterruptCampaign
{
    /// <summary>Check one of seven mode/priority/delay/stall scenarios; throw on a mismatch.</summary>
    public static void Run(int scenario, Action<string, int> drive, Func<string, int> read, Action<bool> clock)
    {
        if (scenario is < 0 or > 6) throw new ArgumentOutOfRangeException(nameof(scenario));
        var bus = new Bus(drive, read, clock);
        if (scenario < 4)
        {
            int mode = scenario == 3 ? 0 : scenario;
            bus.Seed(0x1234, 1, mode, true);
            int handler = mode == 0 ? (scenario == 3 ? 0x5678 : 8) : mode == 1 ? 0x38 : 0x5678;
            bus.Memory[handler] = 0xED; bus.Memory[handler + 1] = 0x4D;
            bus.Memory[65535] = 0x78; bus.Memory[0] = 0x56;
            bus.Memory[0x1234] = 0x78; bus.Memory[0x1235] = 0x56;
            bus.Acknowledge = scenario == 3 ? 0xCD : mode == 0 ? 0xCF : 0xFF;
            drive("int_request", 1);
            bus.Until(() => read(mode == 0 ? "instruction_done" : "interrupt_done") == 1);
            bus.Check("pc_out", handler); bus.Check("sp_out", 65535);
            bus.Check("iff1_out", 0); bus.Check("iff2_out", 0);
            int returnPc = scenario == 3 ? 0x1236 : 0x1234;
            bus.Require(bus.Memory[65535] == (returnPc & 255) && bus.Memory[0] == 0x12, "stack return bytes/wrap");
            bus.Require(bus.Acks == 1, "exactly one interrupt acknowledge");
            drive("int_request", 0); bus.Start();
            bus.Until(() => read("instruction_done") == 1);
            bus.Check("pc_out", returnPc); bus.Check("sp_out", 1);
            bus.Require(bus.RetiSeen, "RETI daisy-chain indication");
            return;
        }
        if (scenario == 4)
        {
            bus.Seed(0x2222, 0x1000, 1, true);
            bus.Memory[0x66] = 0xED; bus.Memory[0x67] = 0x45;
            drive("int_request", 1); drive("nmi", 1);
            bus.Until(() => read("interrupt_done") == 1);
            bus.Check("pc_out", 0x66); bus.Check("iff1_out", 0); bus.Check("iff2_out", 1);
            bus.Require(bus.Acks == 0, "NMI priority over simultaneous INT");
            bus.Start(); bus.Until(() => read("instruction_done") == 1);
            bus.Check("pc_out", 0x2222); bus.Check("iff1_out", 1); bus.Check("sp_out", 0x1000);
            bus.Until(() => read("interrupt_done") == 1);
            bus.Check("pc_out", 0x38); bus.Require(bus.Acks == 1, "pending INT after RETN");
            drive("int_request", 0);
            for (int i = 0; i < 12; i++) bus.Tick();
            bus.Check("sp_out", 0x0FFE); bus.Check("idle", 1); // held NMI must not retrigger
            return;
        }
        if (scenario == 5)
        {
            bus.Seed(0, 0x1000, 1, false);
            bus.Memory[0] = 0xFB; bus.Memory[1] = 0x76; // EI; HALT
            drive("int_request", 1); drive("step", 1);
            bus.Until(() => read("interrupt_done") == 1);
            drive("step", 0);
            bus.Check("pc_out", 0x38); bus.Check("halted_out", 0);
            bus.Require(bus.Retired == 2 && bus.Acks == 1, "EI blocks INT through following HALT");
            bus.Require(bus.Memory[0x0FFE] == 2 && bus.Memory[0x0FFF] == 0, "HALT return address");
            return;
        }
        bus.Seed(0, 0x1000, 1, true, 0x5A00, 0x8000);
        bus.Memory[0] = 0x77; // LD(HL),A
        bus.Start(); bus.Until(() => read("bus_valid") == 1 && read("bus_write") == 1);
        drive("nmi", 1); bus.Tick(false); drive("nmi", 0); bus.Tick(false);
        bus.Check("bus_valid", 1); bus.Check("bus_address", 0x8000); bus.Check("bus_wdata", 0x5A);
        bus.Require(bus.Writes.Count == 0, "no store during WAIT");
        bus.Tick(true); bus.Until(() => read("interrupt_done") == 1);
        bus.Check("pc_out", 0x66);
        bus.Require(bus.Memory[0x8000] == 0x5A && bus.Memory[0x0FFE] == 1, "NMI waits for committed instruction");
        bus.Require(bus.Writes.FindAll(address => address == 0x8000).Count == 1, "single accepted store");
    }

    private sealed class Bus(Action<string, int> drive, Func<string, int> read, Action<bool> clock)
    {
        internal readonly byte[] Memory = new byte[65536];
        internal readonly List<int> Writes = new();
        internal int Acknowledge = 0xFF;
        internal int Acks, Retired;
        internal bool RetiSeen;
        private int edges;

        internal void Seed(int pc, int sp, int mode, bool enabled, int af = 0, int hl = 0)
        {
            drive("step", 0); drive("nmi", 0); drive("int_request", 0); drive("load_state", 0);
            clock(true);
            foreach (string name in new[] { "bc", "de", "ix", "iy", "alt0", "alt1", "alt2", "alt3", "r", "ei_delay" }) drive("init_" + name, 0);
            drive("init_pc", pc); drive("init_sp", sp); drive("init_im", mode); drive("init_i", 255);
            drive("init_af", af); drive("init_hl", hl);
            drive("init_iff1", enabled ? 1 : 0); drive("init_iff2", enabled ? 1 : 0);
            drive("load_state", 1); clock(false); drive("load_state", 0);
        }
        internal void Start()
        {
            Until(() => read("idle") == 1);
            drive("step", 1); Tick(); drive("step", 0);
        }
        internal void Tick(bool? forceReady = null)
        {
            bool ready = forceReady ?? edges % 4 == 3;
            drive("bus_ready", ready ? 1 : 0);
            if (read("instruction_done") != 0) Retired++;
            if (read("reti") != 0) RetiSeen = true;
            if (read("bus_valid") != 0)
            {
                int address = read("bus_address");
                if (read("bus_irq_ack") != 0)
                {
                    drive("bus_rdata", Acknowledge); if (ready) Acks++;
                }
                else if (read("bus_io") != 0) throw new InvalidOperationException("Unexpected port operation in interrupt campaign.");
                else if (read("bus_write") != 0)
                {
                    if (ready) { Memory[address] = checked((byte)read("bus_wdata")); Writes.Add(address); }
                }
                else drive("bus_rdata", Memory[address]);
            }
            clock(false); edges++;
            Require(read("fault") == 0, "unexpected fault");
        }
        internal void Until(Func<bool> condition)
        {
            for (int edge = 0; edge < 256; edge++) { if (condition()) return; Tick(); }
            throw new InvalidOperationException("Interrupt campaign watchdog expired.");
        }
        internal void Check(string name, int expected) => Require(read(name) == expected, $"{name}: expected {expected:X4}, got {read(name):X4}");
        internal void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
