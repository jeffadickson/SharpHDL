using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SharpHDL
{
    /// <summary>
    /// First two-state sequential .NET-JIT backend. Compiles each register's D cone
    /// into a delegate; evaluates all D inputs and RAM writes from pre-edge state,
    /// then commits memory and registers atomically.
    /// This is .NET JIT execution, not native AOT or a verified whole-CPU benchmark.
    /// </summary>
    public sealed class CompiledSequentialSimulator
    {
        readonly Module module;
        readonly Signal[] operands;
        readonly Signal[] registers;
        readonly CompiledCombinational fusedNext;
        readonly SynchronousRam[] rams;
        readonly Dictionary<SynchronousRam, BigInteger[]> ramImages = new();
        readonly CompiledCombinational[] ramAddresses;
        readonly CompiledCombinational[] ramEnables;
        readonly CompiledCombinational[] ramWriteData;
        readonly Dictionary<Signal, BigInteger> values = new();
        readonly Dictionary<Expr, CompiledCombinational> probes = new();
        readonly BigInteger[] operandBuffer;
        readonly BigInteger[] pendingBuffer;
        readonly int[] writeOwners;
        readonly int[] writeAddresses;
        readonly BigInteger[] writeData;
        // One fused JIT delegate replaces per-register delegates; its current
        // array-valued result remains an allocation until direct-buffer lowering.
        readonly Dictionary<Signal,int> operandIndexes;
        bool initialized;

        public CompiledSequentialSimulator(Module module)
        {
            this.module = module ?? throw new ArgumentNullException(nameof(module));
            module.Validate();
            rams = module.Memories.ToArray();
            foreach (var ram in rams) ramImages.Add(ram, (BigInteger[])ram.Initial.Clone());
            operands = module.Signals.Where(s => s.IsInput || s.Kind == "reg").ToArray();
            registers = module.Signals.Where(s => s.Kind == "reg").ToArray();
            operandBuffer = new BigInteger[operands.Length];
            pendingBuffer = new BigInteger[registers.Length];
            writeOwners = new int[rams.Length];
            writeAddresses = new int[rams.Length];
            writeData = new BigInteger[rams.Length];
            operandIndexes = operands.Select((signal,index)=>(signal,index)).ToDictionary(x=>x.signal,x=>x.index);
            // CompiledCombinational explicitly rejects unsupported opcodes rather
            // than silently interpreting the original graph.
            fusedNext = new CompiledCombinational(module, registers.Select(s => s.Driver).ToArray(), true, ramImages, true);
            ramAddresses = rams.Select(r => new CompiledCombinational(module, r.Address, true, ramImages)).ToArray();
            ramEnables = rams.Select(r => new CompiledCombinational(module, r.WriteEnable, true, ramImages)).ToArray();
            ramWriteData = rams.Select(r => new CompiledCombinational(module, r.WriteData, true, ramImages)).ToArray();
            foreach (var input in operands.Where(s => s.IsInput)) values[input] = 0;
        }

        public void Set(Signal input, BigInteger value)
        {
            if (input == null || input.Owner != module || !input.IsInput)
                throw new ArgumentException("Not a local input.", nameof(input));
            Expr.Const(input.Width, value);
            values[input] = value;
            operandBuffer[operandIndexes[input]] = value;
        }

        public BigInteger Read(Signal signal)
        {
            if (signal == null || signal.Owner != module ||
                !(signal.IsInput || signal.Kind == "reg"))
                throw new ArgumentException("Read accepts local inputs or registers only.", nameof(signal));
            if (!values.TryGetValue(signal, out var value))
                throw new InvalidOperationException("Reset before reading registers.");
            return value;
        }

        /// <summary>Read any local hardware expression (including output ports)
        /// using compiled combinational logic over the current register/input state.</summary>
        public BigInteger Read(Expr expression)
        {
            if (expression is Signal s && (s.IsInput || s.Kind == "reg")) return Read(s);
            if (expression == null) throw new ArgumentNullException(nameof(expression));
            if (!initialized) throw new InvalidOperationException("Reset before reading compiled hardware outputs.");
            if (!probes.TryGetValue(expression, out var evaluator))
            {
                evaluator = new CompiledCombinational(module, expression, true, ramImages);
                probes.Add(expression, evaluator);
            }
            // Values in the operand buffer are refreshed on writes and clock commits.
            return evaluator.EvaluateUnchecked(operandBuffer);
        }

        public void Tick(bool reset = false)
        {
            if (reset)
            {
                foreach (var register in registers)
                {
                    values[register] = register.ResetValue;
                    operandBuffer[operandIndexes[register]] = register.ResetValue;
                }
                initialized = true;
                return;
            }
            if (!initialized)
                throw new InvalidOperationException("Reset before clocking compiled sequential hardware.");
            // The operand vector is snapshotted once before evaluating any D input.
            // This guarantees nonblocking-assignment/atomic clock-edge semantics.
            var oldValues = operandBuffer;
            var evaluated = fusedNext.EvaluateVectorUnchecked(oldValues);
            Array.Copy(evaluated, pendingBuffer, pendingBuffer.Length);
            // Fixed-size per-edge scratch avoids List allocations on every clock.
            var writeCount = 0;
            for (var i = 0; i < rams.Length; i++)
            {
                if (ramEnables[i].EvaluateUnchecked(oldValues).IsZero) continue;
                var address = ramAddresses[i].EvaluateUnchecked(oldValues);
                if (address >= rams[i].Depth) continue;
                writeOwners[writeCount] = i;
                writeAddresses[writeCount] = (int)address;
                writeData[writeCount] = ramWriteData[i].EvaluateUnchecked(oldValues);
                writeCount++;
            }
            // Same contract as Simulator.Tick: reads and writes sample the old state;
            // RAM contents update before register commit, without affecting sampled D inputs.
            // RAMs are scanned in index order, so retain the owner index alongside each write.
            // (A disabled or out-of-range port does not create a write.)
            for (var i = 0; i < writeCount; i++)
                ramImages[rams[writeOwners[i]]][writeAddresses[i]] = writeData[i];
            for (var i = 0; i < registers.Length; i++)
            {
                values[registers[i]] = pendingBuffer[i];
                operandBuffer[operandIndexes[registers[i]]] = pendingBuffer[i];
            }
        }
    }
}
