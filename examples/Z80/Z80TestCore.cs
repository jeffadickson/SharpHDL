using System;
using System.Collections.Generic;
using SharpHDL.HLS;

namespace SharpHDL.Examples;

/// <summary>
/// Instruction-stepped Z80 test hardware with a stalled byte bus. Implements the
/// documented unprefixed data/ALU/control instructions and CB register/memory group.
/// DD/FD/ED, interrupts, I/O, EX (SP),HL and undocumented SLL trap explicitly.
/// This is an architectural test design, not a T-state/pin-compatible replacement.
/// </summary>
public static class Z80TestCore
{
    /// <summary>
    /// Elaborate synthesizable Core IR. Transactions commit only on bus_valid AND
    /// bus_ready. Hold step high to run; otherwise each sampled step executes one
    /// instruction. load_state is accepted only while idle and overrides step.
    /// Synchronous reset clears registers, sets SP=FFFF, and aborts pending traffic.
    /// </summary>
    public static ScheduledHardwareDesign Build()
    {
        var b = new ScheduledMachineBuilder("Z80TestCore");
        var step = b.Input("step", 1);
        var load = b.Input("load_state", 1);
        var ready = b.Input("bus_ready", 1);
        var data = b.Input("bus_rdata", 8);
        var r = new ScheduledRegister[8];
        string[] names = { "b", "c", "d", "e", "h", "l", "unused", "a" };
        for (int i = 0; i < 8; i++) r[i] = b.Register(names[i], 8);
        var f = b.Register("f", 8);
        var pc = b.Register("pc", 16);
        var sp = b.Register("sp", 16, 65535);
        var alternate = new ScheduledRegister[4];
        for (int i = 0; i < 4; i++) alternate[i] = b.Register("alt" + i, 16);
        var op = b.Register("opcode", 8);
        var cb = b.Register("cb_opcode", 8);
        var operand = b.Register("operand", 8);
        var lo = b.Register("low", 8);
        var nn = b.Register("immediate", 16);
        var address = b.Register("address", 16);
        var wd = b.Register("write_data", 8);
        var word = b.Register("word", 16);
        var halted = b.Register("halted", 1);
        // All states precede InState calls: scheduler encoding is then fixed.
        var idle = b.State("idle"); var fetch = b.State("fetch");
        var decode = b.State("decode"); var immediate = b.State("imm8");
        var low = b.State("imm_low"); var high = b.State("imm_high");
        var readHl = b.State("read_hl"); var execute = b.State("execute");
        var store = b.State("store"); var storeHigh = b.State("store_high");
        var readAbs = b.State("read_abs"); var readAbsHigh = b.State("read_abs_high");
        var pushHigh = b.State("push_high"); var pushLow = b.State("push_low");
        var popLow = b.State("pop_low"); var popHigh = b.State("pop_high");
        var cbFetch = b.State("cb_fetch"); var cbDecode = b.State("cb_decode");
        var cbRead = b.State("cb_read"); var cbExecute = b.State("cb_execute");
        var retire = b.State("retire"); var trap = b.State("trap");

        ScheduledValue C(int width, int value) => b.Constant(width, value);
        ScheduledValue Eq(ScheduledValue value, int n) => value.Equal(C(value.Width, n));
        ScheduledValue Bit(ScheduledValue value, int n) => value.ShiftRight(n).Resize(1);
        ScheduledValue Pair(ScheduledValue h, ScheduledValue l) => h.Resize(16).ShiftLeft(8).Or(l.Resize(16));
        var pairs = new[] { Pair(r[0].Value, r[1].Value), Pair(r[2].Value, r[3].Value),
            Pair(r[4].Value, r[5].Value), sp.Value };
        var af = Pair(r[7].Value, f.Value);
        var y = op.Value.ShiftRight(3).Resize(3);
        var z = op.Value.Resize(3);
        var p = op.Value.ShiftRight(4).Resize(2);
        ScheduledValue Choose(ScheduledValue index, IReadOnlyList<ScheduledValue> values)
        {
            var result = values[0];
            for (int i = 1; i < values.Count; i++) result = ScheduledValue.Select(Eq(index, i), values[i], result);
            return result;
        }
        var regValues = Array.ConvertAll(r, item => item.Value);
        void Put(ScheduledState state, ScheduledValue index, ScheduledValue value, ScheduledValue guard)
        {
            for (int i = 0; i < 8; i++)
                if (i != 6) state.Assign(r[i], value, guard.And(Eq(index, i)));
        }
        void PutPair(ScheduledState state, int index, ScheduledValue value, ScheduledValue guard, bool stack = false)
        {
            if (index == 3)
            {
                if (stack) { state.Assign(r[7], value.ShiftRight(8).Resize(8), guard); state.Assign(f, value.Resize(8), guard); }
                else state.Assign(sp, value, guard);
            }
            else
            {
                state.Assign(r[index * 2], value.ShiftRight(8).Resize(8), guard);
                state.Assign(r[index * 2 + 1], value.Resize(8), guard);
            }
        }
        ScheduledValue Any(params int[] codes)
        {
            var result = C(1, 0);
            foreach (int code in codes) result = result.Or(Eq(op.Value, code));
            return result;
        }
        var a = r[7].Value; var flags = f.Value;
        var carry = Bit(flags, 0);
        // Z80 flag positions: S Z F5 H F3 P/V N C. Preserve undocumented data
        // bits where well-defined here; BIT (HL) only promises documented flags.
        ScheduledValue SZ(ScheduledValue value) => value.And(C(8, 0xA8)).Or(Eq(value, 0).Resize(8).ShiftLeft(6));
        ScheduledValue Parity(ScheduledValue value)
        {
            var odd = Bit(value, 0);
            for (int i = 1; i < 8; i++) odd = odd.Xor(Bit(value, i));
            return odd.Not().Resize(8).ShiftLeft(2);
        }
        ScheduledValue AluFlags(ScheduledValue left, ScheduledValue right, ScheduledValue cin, bool sub)
        {
            var wide = sub ? left.Resize(9).Subtract(right.Resize(9)).Subtract(cin.Resize(9))
                : left.Resize(9).Add(right.Resize(9)).Add(cin.Resize(9));
            var result = wide.Resize(8);
            var overflow = (sub ? left.Xor(right) : left.Xor(right).Not()).And(left.Xor(result));
            return SZ(result).Or(left.Xor(right).Xor(result).And(C(8, 16)))
                .Or(overflow.ShiftRight(5).And(C(8, 4)))
                .Or(wide.ShiftRight(8).Resize(8)).Or(C(8, sub ? 2 : 0));
        }
        var conditions = new[] { Bit(flags, 6).Not(), Bit(flags, 6), carry.Not(), carry,
            Bit(flags, 2).Not(), Bit(flags, 2), Bit(flags, 7).Not(), Bit(flags, 7) };

        // Debug initialization is a hardware input interface, not simulator state mutation.
        for (int i = 0; i < 4; i++)
        {
            var seed = b.Input("init_" + new[] { "bc", "de", "hl", "af" }[i], 16);
            PutPair(idle, i, seed, load, stack: true);
            idle.Assign(alternate[i], b.Input("init_alt" + i, 16), load);
        }
        idle.Assign(pc, b.Input("init_pc", 16), load).Assign(sp, b.Input("init_sp", 16), load)
            .Assign(halted, C(1, 0), load)
            .Transition("fetch", step.And(load.Not()).And(halted.Value.Not()));
        fetch.Assign(op, data, ready).Assign(pc, pc.Value.Add(C(16, 1)), ready).Transition("decode", ready);
        immediate.Assign(operand, data, ready).Assign(pc, pc.Value.Add(C(16, 1)), ready).Transition("execute", ready);
        low.Assign(lo, data, ready).Assign(pc, pc.Value.Add(C(16, 1)), ready).Transition("imm_high", ready);
        high.Assign(nn, Pair(data, lo.Value), ready).Assign(pc, pc.Value.Add(C(16, 1)), ready).Transition("execute", ready);
        readHl.Assign(operand, data, ready).Transition("execute", ready);

        var ld = Eq(op.Value.ShiftRight(6), 1).And(Eq(op.Value, 0x76).Not());
        var alu = Eq(op.Value.ShiftRight(6), 2);
        var inc = Eq(op.Value.And(C(8, 0xC7)), 4);
        var dec = Eq(op.Value.And(C(8, 0xC7)), 5);
        var ldn = Eq(op.Value.And(C(8, 0xC7)), 6);
        var alun = Eq(op.Value.And(C(8, 0xC7)), 0xC6);
        var imm16 = Eq(op.Value.And(C(8, 0xCF)), 1).Or(Any(0x22, 0x2A, 0x32, 0x3A, 0xC3, 0xCD));
        var jpcc = Eq(op.Value.And(C(8, 0xC7)), 0xC2);
        var callcc = Eq(op.Value.And(C(8, 0xC7)), 0xC4);
        var relative = Any(0x10, 0x18, 0x20, 0x28, 0x30, 0x38);
        decode.Transition("cb_fetch", Eq(op.Value, 0xCB))
            .Transition("imm_low", imm16.Or(jpcc).Or(callcc))
            .Transition("imm8", ldn.Or(alun).Or(relative))
            .Transition("read_hl", ld.Or(alu).And(Eq(z, 6)).Or(inc.Or(dec).And(Eq(y, 6))))
            .Assign(operand, Choose(ScheduledValue.Select(inc.Or(dec), y, z), regValues))
            .Transition("execute");

        // Register/memory load and ALU families share a single operand latch.
        Put(execute, y, operand.Value, ld.Or(ldn).And(Eq(y, 6).Not()));
        var storeReg = ld.Or(ldn).And(Eq(y, 6));
        execute.Assign(address, pairs[2], storeReg).Assign(wd, operand.Value, storeReg).Transition("store", storeReg);
        var arithmetic = alu.Or(alun);
        var rhs = operand.Value;
        var results = new ScheduledValue[8]; var resultFlags = new ScheduledValue[8];
        for (int i = 0; i < 8; i++)
        {
            var ci = i is 1 or 3 ? carry : C(1, 0);
            bool sub = i is 2 or 3 or 7;
            results[i] = i < 4 || i == 7
                ? (sub ? a.Subtract(rhs).Subtract(ci.Resize(8)) : a.Add(rhs).Add(ci.Resize(8)))
                : i == 4 ? a.And(rhs) : i == 5 ? a.Xor(rhs) : a.Or(rhs);
            resultFlags[i] = i < 4 || i == 7 ? AluFlags(a, rhs, ci, sub)
                : SZ(results[i]).Or(Parity(results[i])).Or(C(8, i == 4 ? 16 : 0));
        }
        // CP's undocumented flag bits come from its operand, not its discarded result.
        resultFlags[7] = resultFlags[7].And(C(8, 0xD7)).Or(rhs.And(C(8, 0x28)));
        execute.Assign(r[7], Choose(y, results), arithmetic.And(Eq(y, 7).Not()))
            .Assign(f, Choose(y, resultFlags), arithmetic);
        var incResult = ScheduledValue.Select(dec, rhs.Subtract(C(8, 1)), rhs.Add(C(8, 1)));
        var incFlags = ScheduledValue.Select(dec, AluFlags(rhs, C(8, 1), C(1, 0), true), AluFlags(rhs, C(8, 1), C(1, 0), false))
            .And(C(8, 0xFE)).Or(flags.And(C(8, 1)));
        Put(execute, y, incResult, inc.Or(dec).And(Eq(y, 6).Not()));
        execute.Assign(f, incFlags, inc.Or(dec));
        var modifyMem = inc.Or(dec).And(Eq(y, 6));
        execute.Assign(address, pairs[2], modifyMem).Assign(wd, incResult, modifyMem).Transition("store", modifyMem);

        var supported = ld.Or(ldn).Or(arithmetic).Or(inc).Or(dec);
        // Exact-opcode elaboration only builds hardware; no instruction executes in C#.
        void Instruction(int code, Action<ScheduledValue> action)
        { var guard = Eq(op.Value, code); supported = supported.Or(guard); action(guard); }
        for (int i = 0; i < 4; i++)
        {
            int index = i;
            Instruction(0x01 + 16 * i, g => PutPair(execute, index, nn.Value, g));
            Instruction(0x03 + 16 * i, g => PutPair(execute, index, pairs[index].Add(C(16, 1)), g));
            Instruction(0x0B + 16 * i, g => PutPair(execute, index, pairs[index].Subtract(C(16, 1)), g));
            Instruction(0x09 + 16 * i, g =>
            {
                var sum = pairs[2].Resize(17).Add(pairs[index].Resize(17));
                PutPair(execute, 2, sum.Resize(16), g);
                execute.Assign(f, flags.And(C(8, 0xC4)).Or(sum.ShiftRight(8).Resize(8).And(C(8, 0x28)))
                    .Or(pairs[2].Xor(pairs[index]).Xor(sum.Resize(16)).ShiftRight(8).Resize(8).And(C(8, 16)))
                    .Or(sum.ShiftRight(16).Resize(8)), g);
            });
        }
        Instruction(0x00, _ => { });
        Instruction(0x76, g => execute.Assign(halted, C(1, 1), g));
        Instruction(0x08, g => { execute.Assign(alternate[3], af, g); PutPair(execute, 3, alternate[3].Value, g, true); });
        Instruction(0xD9, g => { for (int i = 0; i < 3; i++) { execute.Assign(alternate[i], pairs[i], g); PutPair(execute, i, alternate[i].Value, g); } });
        Instruction(0xEB, g => { PutPair(execute, 1, pairs[2], g); PutPair(execute, 2, pairs[1], g); });
        Instruction(0xF9, g => execute.Assign(sp, pairs[2], g));
        Instruction(0xE9, g => execute.Assign(pc, pairs[2], g));
        for (int i = 0; i < 2; i++)
        {
            int index = i;
            Instruction(0x02 + 16 * i, g => execute.Assign(address, pairs[index], g).Assign(wd, a, g).Transition("store", g));
            Instruction(0x0A + 16 * i, g => execute.Assign(address, pairs[index], g).Transition("read_abs", g));
        }
        Instruction(0x32, g => execute.Assign(address, nn.Value, g).Assign(wd, a, g).Transition("store", g));
        Instruction(0x22, g => execute.Assign(address, nn.Value, g).Assign(wd, r[5].Value, g).Transition("store", g));
        Instruction(0x3A, g => execute.Assign(address, nn.Value, g).Transition("read_abs", g));
        Instruction(0x2A, g => execute.Assign(address, nn.Value, g).Transition("read_abs", g));
        store.Assign(address, address.Value.Add(C(16, 1)), ready.And(Eq(op.Value, 0x22)))
            .Transition("store_high", ready.And(Eq(op.Value, 0x22))).Transition("retire", ready);
        storeHigh.Transition("retire", ready);
        readAbs.Assign(lo, data, ready.And(Eq(op.Value, 0x2A)))
            .Assign(address, address.Value.Add(C(16, 1)), ready.And(Eq(op.Value, 0x2A)))
            .Assign(r[7], data, ready.And(Eq(op.Value, 0x2A).Not()))
            .Transition("read_abs_high", ready.And(Eq(op.Value, 0x2A))).Transition("retire", ready);
        PutPair(readAbsHigh, 2, Pair(data, lo.Value), ready); readAbsHigh.Transition("retire", ready);

        // Branch displacement is a signed byte added to the post-immediate PC.
        var displacement = ScheduledValue.Select(Bit(rhs, 7), rhs.Resize(16).Or(C(16, 0xFF00)), rhs.Resize(16));
        Instruction(0x18, g => execute.Assign(pc, pc.Value.Add(displacement), g));
        Instruction(0x10, g => execute.Assign(r[0], r[0].Value.Subtract(C(8, 1)), g)
            .Assign(pc, pc.Value.Add(displacement), g.And(Eq(r[0].Value, 1).Not())));
        for (int i = 0; i < 4; i++)
        { int index = i; Instruction(0x20 + i * 8, g => execute.Assign(pc, pc.Value.Add(displacement), g.And(conditions[index]))); }
        Instruction(0xC3, g => execute.Assign(pc, nn.Value, g));
        for (int i = 0; i < 8; i++)
        {
            int index = i;
            Instruction(0xC2 + 8 * i, g => execute.Assign(pc, nn.Value, g.And(conditions[index])));
            Instruction(0xC4 + 8 * i, g => BeginCall(g.And(conditions[index]), nn.Value));
            Instruction(0xC0 + 8 * i, g => execute.Transition("pop_low", g.And(conditions[index])));
            Instruction(0xC7 + 8 * i, g => BeginCall(g, C(16, index * 8)));
        }
        void BeginCall(ScheduledValue guard, ScheduledValue target)
            => execute.Assign(word, pc.Value, guard).Assign(pc, target, guard).Transition("push_high", guard);
        Instruction(0xCD, g => BeginCall(g, nn.Value));
        Instruction(0xC9, g => execute.Transition("pop_low", g));
        for (int i = 0; i < 4; i++)
        {
            int index = i;
            Instruction(0xC5 + 16 * i, g => execute.Assign(word, index == 3 ? af : pairs[index], g).Transition("push_high", g));
            Instruction(0xC1 + 16 * i, g => execute.Transition("pop_low", g));
            PutPair(popHigh, i, Pair(data, lo.Value), ready.And(Eq(op.Value, 0xC1 + 16 * i)), true);
        }
        pushHigh.Assign(sp, sp.Value.Subtract(C(16, 1)), ready).Transition("push_low", ready);
        pushLow.Assign(sp, sp.Value.Subtract(C(16, 1)), ready).Transition("retire", ready);
        popLow.Assign(lo, data, ready).Assign(sp, sp.Value.Add(C(16, 1)), ready).Transition("pop_high", ready);
        var popPair = Eq(op.Value.And(C(8, 0xCF)), 0xC1);
        popHigh.Assign(pc, Pair(data, lo.Value), ready.And(popPair.Not()))
            .Assign(sp, sp.Value.Add(C(16, 1)), ready).Transition("retire", ready);

        // Accumulator rotates preserve S/Z/PV. Their carry is the outgoing bit.
        for (int i = 0; i < 4; i++)
        {
            int kind = i;
            Instruction(0x07 + 8 * i, g =>
            {
                bool right = (kind & 1) != 0;
                var outgoing = Bit(a, right ? 0 : 7);
                var incoming = kind < 2 ? outgoing : carry;
                var value = right ? a.ShiftRight(1).Or(incoming.Resize(8).ShiftLeft(7))
                    : a.ShiftLeft(1).Or(incoming.Resize(8));
                execute.Assign(r[7], value, g).Assign(f, flags.And(C(8, 0xC4)).Or(value.And(C(8, 0x28))).Or(outgoing.Resize(8)), g);
            });
        }
        Instruction(0x2F, g => execute.Assign(r[7], a.Not(), g).Assign(f, flags.And(C(8, 0xC5)).Or(a.Not().And(C(8, 0x28))).Or(C(8, 0x12)), g));
        Instruction(0x37, g => execute.Assign(f, flags.And(C(8, 0xC4)).Or(a.And(C(8, 0x28))).Or(C(8, 1)), g));
        Instruction(0x3F, g => execute.Assign(f, flags.And(C(8, 0xC4)).Or(a.And(C(8, 0x28)))
            .Or(carry.Resize(8).ShiftLeft(4)).Or(carry.Not().Resize(8)), g));
        Instruction(0x27, g =>
        {
            var correctionLow = Bit(flags, 4).Or(a.And(C(8, 15)).LessThan(C(8, 10)).Not());
            var correctionHigh = carry.Or(C(8, 0x99).LessThan(a));
            var correction = ScheduledValue.Select(correctionLow, C(8, 6), C(8, 0))
                .Or(ScheduledValue.Select(correctionHigh, C(8, 0x60), C(8, 0)));
            var value = ScheduledValue.Select(Bit(flags, 1), a.Subtract(correction), a.Add(correction));
            execute.Assign(r[7], value, g).Assign(f, SZ(value).Or(Parity(value))
                .Or(a.Xor(value).And(C(8, 16))).Or(flags.And(C(8, 2))).Or(correctionHigh.Resize(8)), g);
        });
        execute.Transition("retire", supported).Transition("trap");

        // CB decode uses a separate opcode latch so memory writes cannot alias
        // base instruction 22's second-byte store behavior.
        cbFetch.Assign(cb, data, ready).Assign(pc, pc.Value.Add(C(16, 1)), ready).Transition("cb_decode", ready);
        var cy = cb.Value.ShiftRight(3).Resize(3); var cz = cb.Value.Resize(3);
        var cx = cb.Value.ShiftRight(6).Resize(2);
        cbDecode.Transition("trap", Eq(cx, 0).And(Eq(cy, 6)))
            .Transition("cb_read", Eq(cz, 6)).Assign(operand, Choose(cz, regValues)).Transition("cb_execute");
        cbRead.Assign(operand, data, ready).Transition("cb_execute", ready);
        var v = operand.Value;
        var rotate = new[] { v.ShiftLeft(1).Or(Bit(v, 7).Resize(8)), v.ShiftRight(1).Or(Bit(v, 0).Resize(8).ShiftLeft(7)),
            v.ShiftLeft(1).Or(carry.Resize(8)), v.ShiftRight(1).Or(carry.Resize(8).ShiftLeft(7)),
            v.ShiftLeft(1), v.ArithmeticShiftRight(1), v, v.ShiftRight(1) };
        var masks = new ScheduledValue[8];
        for (int i = 0; i < 8; i++) masks[i] = C(8, 1 << i);
        var mask = Choose(cy, masks);
        var cbResult = ScheduledValue.Select(Eq(cx, 0), Choose(cy, rotate),
            ScheduledValue.Select(Eq(cx, 2), v.And(mask.Not()), v.Or(mask)));
        var cbFlags = SZ(cbResult).Or(Parity(cbResult))
            .Or(ScheduledValue.Select(Bit(cy, 0), Bit(v, 0), Bit(v, 7)).Resize(8));
        var zero = Eq(v.And(mask), 0);
        var bitFlags = flags.And(C(8, 1)).Or(C(8, 16)).Or(v.And(C(8, 0x28)))
            .Or(zero.Resize(8).ShiftLeft(6)).Or(zero.Resize(8).ShiftLeft(2))
            .Or(v.And(mask).And(C(8, 128)));
        cbExecute.Assign(f, cbFlags, Eq(cx, 0)).Assign(f, bitFlags, Eq(cx, 1));
        Put(cbExecute, cz, cbResult, Eq(cx, 1).Not().And(Eq(cz, 6).Not()));
        var cbWrite = Eq(cx, 1).Not().And(Eq(cz, 6));
        cbExecute.Assign(address, pairs[2], cbWrite).Assign(wd, cbResult, cbWrite)
            .Transition("store", cbWrite).Transition("retire");
        retire.Transition("idle");

        // Exactly one request is outstanding. All request fields are functions
        // of stable registers/state; no ready-dependent combinational requests.
        var busValid = C(1, 0); var busWrite = C(1, 0);
        var busAddress = C(16, 0); var busData = C(8, 0);
        void Bus(ScheduledState state, ScheduledValue addr, bool write = false, ScheduledValue? value = null)
        {
            var active = b.InState(state); busValid = busValid.Or(active);
            if (write) busWrite = busWrite.Or(active);
            busAddress = ScheduledValue.Select(active, addr, busAddress);
            busData = ScheduledValue.Select(active, value ?? C(8, 0), busData);
        }
        foreach (var state in new[] { fetch, immediate, low, high, cbFetch }) Bus(state, pc.Value);
        Bus(readHl, pairs[2]); Bus(cbRead, pairs[2]);
        Bus(store, address.Value, true, wd.Value); Bus(storeHigh, address.Value, true, r[4].Value);
        Bus(readAbs, address.Value); Bus(readAbsHigh, address.Value);
        Bus(pushHigh, sp.Value.Subtract(C(16, 1)), true, word.Value.ShiftRight(8).Resize(8));
        Bus(pushLow, sp.Value.Subtract(C(16, 1)), true, word.Value.Resize(8));
        Bus(popLow, sp.Value); Bus(popHigh, sp.Value);
        b.Output("bus_valid", busValid); b.Output("bus_write", busWrite);
        b.Output("bus_address", busAddress); b.Output("bus_wdata", busData);
        b.Output("instruction_done", b.InState(retire)); b.Output("idle", b.InState(idle));
        b.Output("fault", b.InState(trap)); b.Output("halted_out", halted.Value);
        b.Output("pc_out", pc.Value); b.Output("sp_out", sp.Value); b.Output("af_out", af);
        for (int i = 0; i < 3; i++) b.Output(new[] { "bc_out", "de_out", "hl_out" }[i], pairs[i]);
        for (int i = 0; i < 4; i++) b.Output("alt_out" + i, alternate[i].Value);
        b.Output("opcode_out", op.Value);
        return b.Lower();
    }
}
