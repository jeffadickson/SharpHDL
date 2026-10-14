using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SharpHDL
{
    /// <summary>
    /// First two-state sequential .NET-JIT backend. Compiles each register's D cone
    /// into a delegate; evaluates all D inputs from the same pre-edge state, then
    /// atomically commits them. Memory-backed designs fail explicitly for now.
    /// This is not a native AOT compiler or a complete CPU simulation backend.
    /// </summary>
    public sealed class CompiledSequentialSimulator
    {
        readonly Module module;
        readonly Signal[] operands;
        readonly Signal[] registers;
        readonly CompiledCombinational[] next;
        readonly Dictionary<Signal, BigInteger> values = new();
        bool initialized;

        public CompiledSequentialSimulator(Module module)
        {
            this.module = module ?? throw new ArgumentNullException(nameof(module));
            module.Validate();
            if (module.Memories.Count != 0)
                throw new NotSupportedException("Compiled sequential RAM semantics are not yet implemented; use the reference simulator.");
            operands = module.Signals.Where(s => s.IsInput || s.Kind == "reg").ToArray();
            registers = module.Signals.Where(s => s.Kind == "reg").ToArray();
            // CompiledCombinational explicitly rejects unsupported opcodes rather
            // than silently interpreting the original graph.
            next = registers.Select(s => new CompiledCombinational(module, s.Driver, true)).ToArray();
            foreach (var input in operands.Where(s => s.IsInput)) values[input] = 0;
        }

        public void Set(Signal input, BigInteger value)
        {
            if (input == null || input.Owner != module || !input.IsInput)
                throw new ArgumentException("Not a local input.", nameof(input));
            Expr.Const(input.Width, value);
            values[input] = value;
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

        public void Tick(bool reset = false)
        {
            if (reset)
            {
                foreach (var register in registers) values[register] = register.ResetValue;
                initialized = true;
                return;
            }
            if (!initialized)
                throw new InvalidOperationException("Reset before clocking compiled sequential hardware.");
            // The operand vector is snapshotted once before evaluating any D input.
            // This guarantees nonblocking-assignment/atomic clock-edge semantics.
            var oldValues = operands.Select(s => values[s]).ToArray();
            var pending = next.Select(d => d.Evaluate(oldValues)).ToArray();
            for (var i = 0; i < registers.Length; i++) values[registers[i]] = pending[i];
        }
    }
}
