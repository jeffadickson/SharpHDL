using System;
using System.Collections.Generic;
using SharpHDL.HLS;

namespace SharpHDL.Examples;

/// <summary>
/// Instruction-stepped Z80 test hardware with a stalled byte bus. Implements the
/// documented unprefixed data/ALU/control instructions and CB register/memory group.
/// DD/FD indexing and indexed CB operations share the base datapath.
/// ED and port I/O share the byte bus; INT/NMI are recognized at retirement boundaries.
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
        var irq = b.Input("int_request", 1); var nmi = b.Input("nmi", 1);
        var r = new ScheduledRegister[8];
        string[] names = { "b", "c", "d", "e", "h", "l", "unused", "a" };
        for (int i = 0; i < 8; i++) r[i] = b.Register(names[i], 8);
        var f = b.Register("f", 8);
        var pc = b.Register("pc", 16);
        var sp = b.Register("sp", 16, 65535);
        var ix = b.Register("ix", 16); var iy = b.Register("iy", 16);
        var indexMode = b.Register("index_mode", 2);
        var displaced = b.Register("displaced", 1);
        var interruptPage = b.Register("i", 8); var refresh = b.Register("refresh", 8);
        var iff1 = b.Register("iff1", 1); var iff2 = b.Register("iff2", 1);
        var interruptMode = b.Register("interrupt_mode", 2); var eiDelay = b.Register("ei_delay", 2);
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
        var nmiPrevious = b.Register("nmi_previous", 1); var nmiPending = b.Register("nmi_pending", 1);
        var servicing = b.Register("servicing_interrupt", 1);
        var clockedStates = new List<ScheduledState>();
        ScheduledState State(string name) { var s = b.State(name); clockedStates.Add(s); return s; }
        // All states precede InState calls: scheduler encoding is then fixed.
        var idle = State("idle"); var fetch = State("fetch");
        var decode = State("decode"); var immediate = State("imm8");
        var low = State("imm_low"); var high = State("imm_high");
        var readHl = State("read_hl"); var execute = State("execute");
        var store = State("store"); var storeHigh = State("store_high");
        var readAbs = State("read_abs"); var readAbsHigh = State("read_abs_high");
        var pushHigh = State("push_high"); var pushLow = State("push_low");
        var popLow = State("pop_low"); var popHigh = State("pop_high");
        var cbFetch = State("cb_fetch"); var cbDecode = State("cb_decode");
        var cbRead = State("cb_read"); var cbExecute = State("cb_execute");
        var indexDisplacement = State("index_displacement");
        var exchangeLow = State("exchange_low"); var exchangeHigh = State("exchange_high");
        var exchangeWriteHigh = State("exchange_write_high"); var exchangeWriteLow = State("exchange_write_low");
        var edFetch = State("ed_fetch"); var edDecode = State("ed_decode");
        var edLow = State("ed_low"); var edHigh = State("ed_high"); var edExecute = State("ed_execute");
        var edStoreLow = State("ed_store_low"); var edStoreHigh = State("ed_store_high");
        var edLoadLow = State("ed_load_low"); var edLoadHigh = State("ed_load_high");
        var portRead = State("port_read"); var portWrite = State("port_write");
        var edIn = State("ed_in"); var edOut = State("ed_out");
        var edRead = State("ed_read"); var edProcess = State("ed_process"); var edWrite = State("ed_write");
        var blockIn = State("block_in"); var blockOut = State("block_out"); var blockFinish = State("block_finish");
        var irqAck = State("irq_ack"); var vectorLow = State("vector_low"); var vectorHigh = State("vector_high");
        var interruptRetire = State("interrupt_retire");
        var retire = State("retire"); var trap = State("trap");

        ScheduledValue C(int width, int value) => b.Constant(width, value);
        ScheduledValue Eq(ScheduledValue value, int n) => value.Equal(C(value.Width, n));
        ScheduledValue Bit(ScheduledValue value, int n) => value.ShiftRight(n).Resize(1);
        ScheduledValue Pair(ScheduledValue h, ScheduledValue l) => h.Resize(16).ShiftLeft(8).Or(l.Resize(16));
        var rawHl = Pair(r[4].Value, r[5].Value);
        var indexed = Eq(indexMode.Value, 0).Not();
        var selectedIndex = ScheduledValue.Select(Eq(indexMode.Value, 1), ix.Value, iy.Value);
        var pairs = new[] { Pair(r[0].Value, r[1].Value), Pair(r[2].Value, r[3].Value),
            ScheduledValue.Select(indexed, selectedIndex, rawHl), sp.Value };
        var effectiveHl = ScheduledValue.Select(indexed, address.Value, rawHl);
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
        // Indexed memory transfers use ordinary H/L, whereas pure register forms
        // use IXH/IXL or IYH/IYL. This exception is essential for DD 66/74 etc.
        var memoryLoad = Eq(op.Value.ShiftRight(6), 1).And(Eq(y, 6).Or(Eq(z, 6)));
        var indexBytes = indexed.And(memoryLoad.Not());
        regValues[4] = ScheduledValue.Select(indexBytes, selectedIndex.ShiftRight(8).Resize(8), r[4].Value);
        regValues[5] = ScheduledValue.Select(indexBytes, selectedIndex.Resize(8), r[5].Value);
        void Put(ScheduledState state, ScheduledValue index, ScheduledValue value, ScheduledValue guard, bool raw = false)
        {
            for (int i = 0; i < 8; i++)
                if (i != 6)
                {
                    var selected = guard.And(Eq(index, i));
                    if (i is 4 or 5 && !raw)
                    {
                        state.Assign(r[i], value, selected.And(indexBytes.Not()));
                        var replacement = i == 4 ? Pair(value, selectedIndex.Resize(8))
                            : Pair(selectedIndex.ShiftRight(8).Resize(8), value);
                        state.Assign(ix, replacement, selected.And(indexBytes).And(Eq(indexMode.Value, 1)));
                        state.Assign(iy, replacement, selected.And(indexBytes).And(Eq(indexMode.Value, 2)));
                    }
                    else state.Assign(r[i], value, selected);
                }
        }
        void PutPair(ScheduledState state, int index, ScheduledValue value, ScheduledValue guard, bool stack = false, bool raw = false)
        {
            if (index == 3)
            {
                if (stack) { state.Assign(r[7], value.ShiftRight(8).Resize(8), guard); state.Assign(f, value.Resize(8), guard); }
                else state.Assign(sp, value, guard);
            }
            else
            {
                var normal = index == 2 && !raw ? guard.And(indexed.Not()) : guard;
                state.Assign(r[index * 2], value.ShiftRight(8).Resize(8), normal);
                state.Assign(r[index * 2 + 1], value.Resize(8), normal);
                if (index == 2 && !raw)
                {
                    state.Assign(ix, value, guard.And(Eq(indexMode.Value, 1)));
                    state.Assign(iy, value, guard.And(Eq(indexMode.Value, 2)));
                }
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
            PutPair(idle, i, seed, load, stack: true, raw: true);
            idle.Assign(alternate[i], b.Input("init_alt" + i, 16), load);
        }
        idle.Assign(pc, b.Input("init_pc", 16), load).Assign(sp, b.Input("init_sp", 16), load)
            .Assign(ix, b.Input("init_ix", 16), load).Assign(iy, b.Input("init_iy", 16), load)
            .Assign(indexMode, C(2, 0)).Assign(displaced, C(1, 0))
            .Assign(halted, C(1, 0), load);
        var initialMode = b.Input("init_im", 2);
        idle.Assign(interruptPage, b.Input("init_i", 8), load).Assign(refresh, b.Input("init_r", 8), load)
            .Assign(iff1, b.Input("init_iff1", 1), load).Assign(iff2, b.Input("init_iff2", 1), load)
            .Assign(interruptMode, initialMode, load).Assign(eiDelay, b.Input("init_ei_delay", 2), load);
        idle.Transition("trap", load.And(Eq(initialMode, 3)));
        var incrementRefresh = refresh.Value.And(C(8, 128)).Or(refresh.Value.Add(C(8, 1)).And(C(8, 127)));
        fetch.Assign(op, data, ready).Assign(pc, pc.Value.Add(C(16, 1)), ready).Transition("decode", ready);
        fetch.Assign(refresh, incrementRefresh, ready);
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
        var needsDisplacement = ld.And(Eq(y, 6).Or(Eq(z, 6))).Or(alu.And(Eq(z, 6)))
            .Or(inc.Or(dec).Or(ldn).And(Eq(y, 6))).Or(Eq(op.Value, 0xCB));
        decode.Assign(indexMode, C(2, 1), Eq(op.Value, 0xDD)).Assign(indexMode, C(2, 2), Eq(op.Value, 0xFD))
            .Transition("fetch", Any(0xDD, 0xFD))
            .Transition("index_displacement", indexed.And(displaced.Value.Not()).And(needsDisplacement))
            .Transition("cb_fetch", Eq(op.Value, 0xCB))
            .Transition("imm_low", imm16.Or(jpcc).Or(callcc))
            .Assign(indexMode, C(2, 0), Eq(op.Value, 0xED)).Transition("ed_fetch", Eq(op.Value, 0xED))
            .Transition("imm8", ldn.Or(alun).Or(relative).Or(Any(0xD3, 0xDB)))
            .Transition("read_hl", ld.Or(alu).And(Eq(z, 6)).Or(inc.Or(dec).And(Eq(y, 6))))
            .Assign(operand, Choose(ScheduledValue.Select(inc.Or(dec), y, z), regValues))
            .Transition("execute");
        var signedDisp = ScheduledValue.Select(Bit(data, 7), data.Resize(16).Or(C(16, 0xFF00)), data.Resize(16));
        indexDisplacement.Assign(address, selectedIndex.Add(signedDisp), ready).Assign(displaced, C(1, 1), ready)
            .Assign(pc, pc.Value.Add(C(16, 1)), ready).Transition("decode", ready);

        // Register/memory load and ALU families share a single operand latch.
        Put(execute, y, operand.Value, ld.Or(ldn).And(Eq(y, 6).Not()));
        var storeReg = ld.Or(ldn).And(Eq(y, 6));
        execute.Assign(address, effectiveHl, storeReg).Assign(wd, operand.Value, storeReg).Transition("store", storeReg);
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
        execute.Assign(address, effectiveHl, modifyMem).Assign(wd, incResult, modifyMem).Transition("store", modifyMem);

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
        Instruction(0xF3, g => execute.Assign(iff1, C(1, 0), g).Assign(iff2, C(1, 0), g).Assign(eiDelay, C(2, 0), g));
        Instruction(0xFB, g => execute.Assign(iff1, C(1, 1), g).Assign(iff2, C(1, 1), g).Assign(eiDelay, C(2, 2), g));
        Instruction(0xD3, g => execute.Assign(address, Pair(a, rhs), g).Assign(wd, a, g).Transition("port_write", g));
        Instruction(0xDB, g => execute.Assign(address, Pair(a, rhs), g).Transition("port_read", g));
        portWrite.Transition("retire", ready);
        portRead.Assign(r[7], data, ready).Transition("retire", ready);
        Instruction(0x08, g => { execute.Assign(alternate[3], af, g); PutPair(execute, 3, alternate[3].Value, g, true); });
        Instruction(0xD9, g => { for (int i = 0; i < 3; i++) { execute.Assign(alternate[i], i == 2 ? rawHl : pairs[i], g); PutPair(execute, i, alternate[i].Value, g, raw: true); } });
        Instruction(0xEB, g => { PutPair(execute, 1, rawHl, g); PutPair(execute, 2, pairs[1], g, raw: true); });
        Instruction(0xE3, g => execute.Assign(word, pairs[2], g).Transition("exchange_low", g));
        exchangeLow.Assign(lo, data, ready).Transition("exchange_high", ready);
        PutPair(exchangeHigh, 2, Pair(data, lo.Value), ready); exchangeHigh.Transition("exchange_write_high", ready);
        exchangeWriteHigh.Transition("exchange_write_low", ready); exchangeWriteLow.Transition("retire", ready);
        Instruction(0xF9, g => execute.Assign(sp, pairs[2], g));
        Instruction(0xE9, g => execute.Assign(pc, pairs[2], g));
        for (int i = 0; i < 2; i++)
        {
            int index = i;
            Instruction(0x02 + 16 * i, g => execute.Assign(address, pairs[index], g).Assign(wd, a, g).Transition("store", g));
            Instruction(0x0A + 16 * i, g => execute.Assign(address, pairs[index], g).Transition("read_abs", g));
        }
        Instruction(0x32, g => execute.Assign(address, nn.Value, g).Assign(wd, a, g).Transition("store", g));
        Instruction(0x22, g => execute.Assign(address, nn.Value, g).Assign(wd, pairs[2].Resize(8), g).Transition("store", g));
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
        pushLow.Assign(sp, sp.Value.Subtract(C(16, 1)), ready)
            .Transition("interrupt_retire", ready.And(servicing.Value)).Transition("retire", ready);
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
        cbFetch.Assign(refresh, incrementRefresh, ready.And(indexed.Not()));
        var cy = cb.Value.ShiftRight(3).Resize(3); var cz = cb.Value.Resize(3);
        var cx = cb.Value.ShiftRight(6).Resize(2);
        cbDecode.Transition("cb_read", Eq(cz, 6).Or(indexed))
            .Assign(operand, Choose(cz, Array.ConvertAll(r, item => item.Value))).Transition("cb_execute");
        cbRead.Assign(operand, data, ready).Transition("cb_execute", ready);
        var v = operand.Value;
        var rotate = new[] { v.ShiftLeft(1).Or(Bit(v, 7).Resize(8)), v.ShiftRight(1).Or(Bit(v, 0).Resize(8).ShiftLeft(7)),
            v.ShiftLeft(1).Or(carry.Resize(8)), v.ShiftRight(1).Or(carry.Resize(8).ShiftLeft(7)),
            v.ShiftLeft(1), v.ArithmeticShiftRight(1), v.ShiftLeft(1).Or(C(8, 1)), v.ShiftRight(1) };
        var masks = new ScheduledValue[8];
        for (int i = 0; i < 8; i++) masks[i] = C(8, 1 << i);
        var mask = Choose(cy, masks);
        var cbResult = ScheduledValue.Select(Eq(cx, 0), Choose(cy, rotate),
            ScheduledValue.Select(Eq(cx, 2), v.And(mask.Not()), v.Or(mask)));
        var cbFlags = SZ(cbResult).Or(Parity(cbResult))
            .Or(ScheduledValue.Select(Bit(cy, 0), Bit(v, 0), Bit(v, 7)).Resize(8));
        var zero = Eq(v.And(mask), 0);
        var bitFlagSource = ScheduledValue.Select(indexed, address.Value.ShiftRight(8).Resize(8), v);
        var bitFlags = flags.And(C(8, 1)).Or(C(8, 16)).Or(bitFlagSource.And(C(8, 0x28)))
            .Or(zero.Resize(8).ShiftLeft(6)).Or(zero.Resize(8).ShiftLeft(2))
            .Or(v.And(mask).And(C(8, 128)));
        cbExecute.Assign(f, cbFlags, Eq(cx, 0)).Assign(f, bitFlags, Eq(cx, 1));
        Put(cbExecute, cz, cbResult, Eq(cx, 1).Not().And(Eq(cz, 6).Not()), raw: true);
        var cbWrite = Eq(cx, 1).Not().And(Eq(cz, 6).Or(indexed));
        cbExecute.Assign(address, effectiveHl, cbWrite).Assign(wd, cbResult, cbWrite)
            .Transition("store", cbWrite).Transition("retire");
        retire.Assign(eiDelay, eiDelay.Value.Subtract(C(2, 1)), Eq(eiDelay.Value, 0).Not()).Transition("idle");

        // ED opcodes use raw HL and ordinary register bytes; preceding DD/FD is
        // ignored. Repeat instructions retire one iteration and rewind PC so an
        // interrupt can be recognized at the architectural iteration boundary.
        edFetch.Assign(cb, data, ready).Assign(pc, pc.Value.Add(C(16, 1)), ready)
            .Assign(refresh, incrementRefresh, ready).Transition("ed_decode", ready);
        var ex = cb.Value.ShiftRight(6).Resize(2); var ey = cb.Value.ShiftRight(3).Resize(3);
        var ez = cb.Value.Resize(3); var ep = cb.Value.ShiftRight(4).Resize(2);
        var group1 = Eq(ex, 1); var block = Eq(ex, 2).And(Bit(cb.Value, 5)).And(Bit(cb.Value, 2).Not());
        var direction = ScheduledValue.Select(Bit(cb.Value, 3), C(16, 65535), C(16, 1));
        var repeat = Bit(cb.Value, 4);
        edDecode.Transition("ed_low", group1.And(Eq(ez, 3))).Transition("ed_execute");
        edLow.Assign(lo, data, ready).Assign(pc, pc.Value.Add(C(16, 1)), ready).Transition("ed_high", ready);
        edHigh.Assign(nn, Pair(data, lo.Value), ready).Assign(pc, pc.Value.Add(C(16, 1)), ready).Transition("ed_execute", ready);
        var edPair = Choose(ep, pairs);
        edExecute.Assign(address, nn.Value, group1.And(Eq(ez, 3)))
            .Assign(word, edPair, group1.And(Eq(ez, 3)).And(Bit(cb.Value, 3).Not()))
            .Transition("ed_store_low", group1.And(Eq(ez, 3)).And(Bit(cb.Value, 3).Not()))
            .Transition("ed_load_low", group1.And(Eq(ez, 3)));
        edStoreLow.Assign(address, address.Value.Add(C(16, 1)), ready).Transition("ed_store_high", ready);
        edStoreHigh.Transition("retire", ready);
        edLoadLow.Assign(lo, data, ready).Assign(address, address.Value.Add(C(16, 1)), ready).Transition("ed_load_high", ready);
        for (int i = 0; i < 4; i++) PutPair(edLoadHigh, i, Pair(data, lo.Value), ready.And(Eq(ep, i)), raw: true);
        edLoadHigh.Transition("retire", ready);
        var edAdd = group1.And(Eq(ez, 2)); var subtract16 = Bit(cb.Value, 3).Not();
        var wide16 = ScheduledValue.Select(subtract16,
            rawHl.Resize(17).Subtract(edPair.Resize(17)).Subtract(carry.Resize(17)),
            rawHl.Resize(17).Add(edPair.Resize(17)).Add(carry.Resize(17)));
        var result16 = wide16.Resize(16);
        var overflow16 = ScheduledValue.Select(subtract16, rawHl.Xor(edPair), rawHl.Xor(edPair).Not()).And(rawHl.Xor(result16));
        PutPair(edExecute, 2, result16, edAdd, raw: true);
        edExecute.Assign(f, result16.ShiftRight(8).Resize(8).And(C(8, 0xA8))
            .Or(Eq(result16, 0).Resize(8).ShiftLeft(6))
            .Or(rawHl.Xor(edPair).Xor(result16).ShiftRight(8).Resize(8).And(C(8, 16)))
            .Or(overflow16.ShiftRight(13).Resize(8).And(C(8, 4)))
            .Or(subtract16.Resize(8).ShiftLeft(1)).Or(wide16.ShiftRight(16).Resize(8)), edAdd);
        var negate = group1.And(Eq(ez, 4));
        edExecute.Assign(r[7], C(8, 0).Subtract(a), negate).Assign(f, AluFlags(C(8, 0), a, C(1, 0), true), negate);
        var returnInterrupt = group1.And(Eq(ez, 5));
        edExecute.Assign(iff1, iff2.Value, returnInterrupt).Transition("pop_low", returnInterrupt);
        edExecute.Assign(interruptMode, Choose(ey, new[] { C(2, 0), C(2, 0), C(2, 1), C(2, 2), C(2, 0), C(2, 0), C(2, 1), C(2, 2) }), group1.And(Eq(ez, 6)));
        var special = group1.And(Eq(ez, 7));
        edExecute.Assign(interruptPage, a, special.And(Eq(ey, 0))).Assign(refresh, a, special.And(Eq(ey, 1)));
        var fromSpecial = special.And(Eq(ey, 2).Or(Eq(ey, 3)));
        var specialValue = ScheduledValue.Select(Eq(ey, 2), interruptPage.Value, refresh.Value);
        edExecute.Assign(r[7], specialValue, fromSpecial).Assign(f, SZ(specialValue).Or(iff2.Value.Resize(8).ShiftLeft(2)).Or(flags.And(C(8, 1))), fromSpecial);
        edExecute.Transition("ed_in", group1.And(Eq(ez, 0))).Transition("ed_out", group1.And(Eq(ez, 1)));
        Put(edIn, ey, data, ready.And(Eq(ey, 6).Not()), raw: true);
        edIn.Assign(f, SZ(data).Or(Parity(data)).Or(flags.And(C(8, 1))), ready).Transition("retire", ready);
        edOut.Transition("retire", ready);
        var nibble = special.And(Eq(ey, 4).Or(Eq(ey, 5)));
        edExecute.Transition("block_in", block.And(Eq(ez, 2)))
            .Transition("ed_read", block.Or(nibble)).Transition("retire");
        // Undefined ED encodings are real Z80 two-byte NOPs, not successful
        // placeholder translations. Their only architectural effect here is R.
        edRead.Assign(operand, data, ready).Transition("ed_process", ready);
        blockIn.Assign(operand, data, ready).Transition("ed_process", ready);
        var isCompare = block.And(Eq(ez, 1)); var isTransfer = block.And(Eq(ez, 0));
        var isInput = block.And(Eq(ez, 2)); var isOutput = block.And(Eq(ez, 3));
        var nextBc = pairs[0].Subtract(C(16, 1)); var nextHl = rawHl.Add(direction);
        var compareResult = a.Subtract(rhs); var compareHalf = Bit(a.Xor(rhs).Xor(compareResult), 4);
        var adjusted = compareResult.Subtract(compareHalf.Resize(8));
        var compareFlags = AluFlags(a, rhs, C(1, 0), true).And(C(8, 0xD2)).Or(flags.And(C(8, 1)))
            .Or(Eq(nextBc, 0).Not().Resize(8).ShiftLeft(2)).Or(adjusted.And(C(8, 8))).Or(adjusted.And(C(8, 2)).ShiftLeft(4));
        var repeatPcFlags = pc.Value.Subtract(C(16, 2)).ShiftRight(8).Resize(8).And(C(8, 0x28));
        var compareRepeats = repeat.And(Eq(nextBc, 0).Not()).And(Eq(compareResult, 0).Not());
        compareFlags = ScheduledValue.Select(compareRepeats, compareFlags.And(C(8, 0xD7)).Or(repeatPcFlags), compareFlags);
        PutPair(edProcess, 0, nextBc, isCompare, raw: true); PutPair(edProcess, 2, nextHl, isCompare, raw: true);
        edProcess.Assign(f, compareFlags, isCompare)
            .Assign(pc, pc.Value.Subtract(C(16, 2)), isCompare.And(repeat).And(Eq(nextBc, 0).Not()).And(Eq(compareResult, 0).Not()))
            .Transition("retire", isCompare);
        var rld = Eq(ey, 5);
        var nibbleA = a.And(C(8, 0xF0)).Or(ScheduledValue.Select(rld, rhs.ShiftRight(4), rhs.And(C(8, 15))));
        var nibbleMemory = ScheduledValue.Select(rld, rhs.ShiftLeft(4).Or(a.And(C(8, 15))), a.ShiftLeft(4).Or(rhs.ShiftRight(4)));
        edProcess.Assign(r[7], nibbleA, nibble).Assign(f, SZ(nibbleA).Or(Parity(nibbleA)).Or(flags.And(C(8, 1))), nibble);
        edProcess.Assign(address, ScheduledValue.Select(isTransfer, pairs[1], rawHl), nibble.Or(isTransfer).Or(isInput))
            .Assign(wd, ScheduledValue.Select(nibble, nibbleMemory, rhs), nibble.Or(isTransfer).Or(isInput))
            .Transition("ed_write", nibble.Or(isTransfer).Or(isInput));
        // OUTI/OUTD use decremented B in the port address; INI/IND use original B.
        edProcess.Assign(r[0], r[0].Value.Subtract(C(8, 1)), isOutput)
            .Assign(address, Pair(r[0].Value.Subtract(C(8, 1)), r[1].Value), isOutput)
            .Assign(wd, rhs, isOutput).Transition("block_out", isOutput);
        edWrite.Transition("retire", ready.And(nibble)).Transition("block_finish", ready);
        blockOut.Transition("block_finish", ready);
        PutPair(blockFinish, 2, nextHl, C(1, 1), raw: true);
        PutPair(blockFinish, 1, pairs[1].Add(direction), isTransfer, raw: true);
        PutPair(blockFinish, 0, nextBc, isTransfer, raw: true);
        var sum8 = a.Add(rhs);
        var transferFlags = flags.And(C(8, 0xC1)).Or(Eq(nextBc, 0).Not().Resize(8).ShiftLeft(2))
            .Or(sum8.And(C(8, 8))).Or(sum8.And(C(8, 2)).ShiftLeft(4));
        transferFlags = ScheduledValue.Select(repeat.And(Eq(nextBc, 0).Not()), transferFlags.And(C(8, 0xD7)).Or(repeatPcFlags), transferFlags);
        blockFinish.Assign(f, transferFlags, isTransfer);
        var ioB = ScheduledValue.Select(isOutput, r[0].Value, r[0].Value.Subtract(C(8, 1)));
        blockFinish.Assign(r[0], ioB, isInput);
        var ioTerm = ScheduledValue.Select(isOutput, nextHl.Resize(8), r[1].Value.Add(direction.Resize(8)));
        var ioSum = rhs.Resize(9).Add(ioTerm.Resize(9)); var ioCarry = Bit(ioSum, 8);
        var ioFlags = SZ(ioB).Or(Bit(rhs, 7).Resize(8).ShiftLeft(1))
            .Or(ioCarry.Resize(8)).Or(ioCarry.Resize(8).ShiftLeft(4))
            .Or(Parity(ioSum.Resize(8).And(C(8, 7)).Xor(ioB)));
        // Continuing block-I/O iterations fold the internal B adjustment into
        // parity/half-carry and expose restart-PC bits, unlike terminating INI/OUTI.
        // Qualified against the pinned SingleStepTests post_IN_O_R reference.
        var parityB = ScheduledValue.Select(ioCarry,
            ScheduledValue.Select(Bit(rhs, 7), ioB.Subtract(C(8, 1)), ioB.Add(C(8, 1))), ioB).And(C(8, 7));
        var repeatParity = Bit(ioFlags, 2).Xor(Bit(Parity(parityB), 2)).Not();
        var repeatHalf = ioCarry.And(ScheduledValue.Select(Bit(rhs, 7), Eq(ioB.And(C(8, 15)), 0), Eq(ioB.And(C(8, 15)), 15)));
        var repeatIoFlags = ioFlags.And(C(8, 0xC3)).Or(repeatPcFlags)
            .Or(repeatParity.Resize(8).ShiftLeft(2)).Or(repeatHalf.Resize(8).ShiftLeft(4));
        ioFlags = ScheduledValue.Select(repeat.And(Eq(ioB, 0).Not()), repeatIoFlags, ioFlags);
        blockFinish.Assign(f, ioFlags, isInput.Or(isOutput));
        var continueBlock = ScheduledValue.Select(isTransfer, Eq(nextBc, 0).Not(), Eq(ioB, 0).Not()).And(repeat);
        blockFinish.Assign(pc, pc.Value.Subtract(C(16, 2)), continueBlock).Transition("retire");

        // NMI edges are captured throughout the microsequence, including WAIT
        // stalls. Acceptance only occurs at idle, after any instruction/repeat
        // iteration has committed. NMI wins simultaneous INT and does not clear IFF2.
        var nmiEdge = nmi.And(nmiPrevious.Value.Not());
        var acceptNmi = b.InState(idle).And(load.Not()).And(nmiPending.Value.Or(nmiEdge));
        var acceptIrq = b.InState(idle).And(load.Not()).And(acceptNmi.Not())
            .And(irq).And(iff1.Value).And(Eq(eiDelay.Value, 0));
        foreach (var state in clockedStates)
        {
            state.Assign(nmiPrevious, nmi);
            state.Assign(nmiPending, nmiPending.Value.Or(nmiEdge).And(acceptNmi.Not()));
        }
        idle.Assign(halted, C(1, 0), acceptNmi.Or(acceptIrq)).Assign(servicing, C(1, 1), acceptNmi.Or(acceptIrq))
            .Assign(iff1, C(1, 0), acceptNmi.Or(acceptIrq)).Assign(iff2, C(1, 0), acceptIrq)
            .Assign(word, pc.Value, acceptNmi).Assign(pc, C(16, 0x66), acceptNmi)
            .Assign(refresh, incrementRefresh, acceptNmi)
            .Transition("push_high", acceptNmi).Transition("irq_ack", acceptIrq)
            .Transition("fetch", step.And(load.Not()).And(halted.Value.Not()));
        // Acknowledge is a distinct stalled bus transaction. IM0 executes the
        // supplied opcode without advancing PC; later operand bytes come from RAM.
        irqAck.Assign(refresh, incrementRefresh, ready)
            .Assign(op, data, ready.And(Eq(interruptMode.Value, 0)))
            .Assign(servicing, C(1, 0), ready.And(Eq(interruptMode.Value, 0)))
            .Assign(word, pc.Value, ready.And(Eq(interruptMode.Value, 1)))
            .Assign(pc, C(16, 0x38), ready.And(Eq(interruptMode.Value, 1)))
            .Assign(address, Pair(interruptPage.Value, data), ready.And(Eq(interruptMode.Value, 2)))
            .Transition("decode", ready.And(Eq(interruptMode.Value, 0)))
            .Transition("push_high", ready.And(Eq(interruptMode.Value, 1)))
            .Transition("vector_low", ready.And(Eq(interruptMode.Value, 2)));
        vectorLow.Assign(lo, data, ready).Assign(address, address.Value.Add(C(16, 1)), ready).Transition("vector_high", ready);
        vectorHigh.Assign(word, pc.Value, ready).Assign(pc, Pair(data, lo.Value), ready).Transition("push_high", ready);
        interruptRetire.Assign(servicing, C(1, 0)).Transition("idle");

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
        foreach (var state in new[] { fetch, immediate, low, high, cbFetch, indexDisplacement }) Bus(state, pc.Value);
        Bus(readHl, effectiveHl); Bus(cbRead, effectiveHl);
        Bus(store, address.Value, true, wd.Value); Bus(storeHigh, address.Value, true, pairs[2].ShiftRight(8).Resize(8));
        Bus(readAbs, address.Value); Bus(readAbsHigh, address.Value);
        Bus(pushHigh, sp.Value.Subtract(C(16, 1)), true, word.Value.ShiftRight(8).Resize(8));
        Bus(pushLow, sp.Value.Subtract(C(16, 1)), true, word.Value.Resize(8));
        Bus(popLow, sp.Value); Bus(popHigh, sp.Value);
        Bus(exchangeLow, sp.Value); Bus(exchangeHigh, sp.Value.Add(C(16, 1)));
        Bus(exchangeWriteHigh, sp.Value.Add(C(16, 1)), true, word.Value.ShiftRight(8).Resize(8));
        Bus(exchangeWriteLow, sp.Value, true, word.Value.Resize(8));
        foreach (var state in new[] { edFetch, edLow, edHigh }) Bus(state, pc.Value);
        Bus(edStoreLow, address.Value, true, word.Value.Resize(8)); Bus(edStoreHigh, address.Value, true, word.Value.ShiftRight(8).Resize(8));
        Bus(edLoadLow, address.Value); Bus(edLoadHigh, address.Value); Bus(edRead, rawHl); Bus(edWrite, address.Value, true, wd.Value);
        Bus(portRead, address.Value); Bus(portWrite, address.Value, true, wd.Value);
        Bus(edIn, pairs[0]); Bus(edOut, pairs[0], true, ScheduledValue.Select(Eq(ey, 6), C(8, 0), Choose(ey, Array.ConvertAll(r, item => item.Value))));
        Bus(blockIn, pairs[0]); Bus(blockOut, address.Value, true, wd.Value);
        Bus(irqAck, C(16, 0)); Bus(vectorLow, address.Value); Bus(vectorHigh, address.Value);
        b.Output("bus_io", b.InState(portRead).Or(b.InState(portWrite)).Or(b.InState(edIn)).Or(b.InState(edOut)).Or(b.InState(blockIn)).Or(b.InState(blockOut)).Or(b.InState(irqAck)));
        b.Output("bus_irq_ack", b.InState(irqAck)); b.Output("interrupt_done", b.InState(interruptRetire));
        b.Output("reti", b.InState(popHigh).And(ready).And(Eq(op.Value, 0xED)).And(Eq(cb.Value, 0x4D)));
        b.Output("bus_valid", busValid); b.Output("bus_write", busWrite);
        b.Output("bus_address", busAddress); b.Output("bus_wdata", busData);
        b.Output("instruction_done", b.InState(retire)); b.Output("idle", b.InState(idle));
        b.Output("fault", b.InState(trap)); b.Output("halted_out", halted.Value);
        b.Output("pc_out", pc.Value); b.Output("sp_out", sp.Value); b.Output("af_out", af);
        for (int i = 0; i < 3; i++) b.Output(new[] { "bc_out", "de_out", "hl_out" }[i], i == 2 ? rawHl : pairs[i]);
        b.Output("ix_out", ix.Value); b.Output("iy_out", iy.Value);
        b.Output("i_out", interruptPage.Value); b.Output("r_out", refresh.Value);
        b.Output("iff1_out", iff1.Value); b.Output("iff2_out", iff2.Value); b.Output("im_out", interruptMode.Value);
        b.Output("ei_delay_out", eiDelay.Value);
        for (int i = 0; i < 4; i++) b.Output("alt_out" + i, alternate[i].Value);
        b.Output("opcode_out", op.Value);
        return b.Lower();
    }
}
