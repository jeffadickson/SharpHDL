using System;
using System.Collections.Generic;
using System.Numerics;

namespace SharpHDL
{
    public interface IHardwareDebugObserver
    {
        void AfterEdge(long cycle, bool reset);
    }

    public sealed class HardwareTraceSample
    {
        public long Cycle { get; }
        public string Name { get; }
        public BigInteger Value { get; }
        public HardwareTraceSample(long cycle, string name, BigInteger value)
        { Cycle = cycle; Name = name; Value = value; }
    }

    public sealed class HardwareBreakpoint
    {
        public int Id { get; }
        public string Name { get; }
        public Expr Expression { get; }
        public BigInteger Value { get; }
        public bool OnChange { get; }
        internal BigInteger? LastValue;
        internal HardwareBreakpoint(int id, string name, Expr expression, BigInteger value, bool onChange)
        { Id = id; Name = name; Expression = expression; Value = value; OnChange = onChange; }
    }

    /// <summary>Hardware clock-debugging facade. Reads and triggers are post-edge.</summary>
    public sealed partial class HardwareDebugSession
    {
        readonly Simulator simulator;
        readonly IHardwareDebugObserver observer;
        readonly List<HardwareBreakpoint> breakpoints = new List<HardwareBreakpoint>();
        readonly Dictionary<string, Expr> watches = new Dictionary<string, Expr>(StringComparer.Ordinal);
        readonly List<HardwareTraceSample> trace = new List<HardwareTraceSample>();
        int nextBreakpoint = 1;
        public long Cycle { get; private set; }
        public int? LastBreakpointId { get; private set; }
        public IReadOnlyList<HardwareTraceSample> Trace => trace.AsReadOnly();
        // Zero disables sample retention while leaving breakpoint evaluation active.
        public int MaxTraceSamples { get; set; } = 100000;
        public HardwareDebugSession(Module module, IHardwareDebugObserver observer)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (observer == null) throw new ArgumentNullException(nameof(observer));
            simulator = module.Simulate();
            this.observer = observer;
        }
        public void Set(Signal input, BigInteger value) { simulator.Set(input, value); }
        public BigInteger Read(Expr expression) { return simulator.Read(expression); }
        public void Watch(string name, Expr expression)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Watch name is required.", nameof(name));
            if (expression == null) throw new ArgumentNullException(nameof(expression));
            watches.Add(name, expression);
        }
        public HardwareBreakpoint BreakWhen(string name, Expr expression, BigInteger value)
        {
            if (expression == null) throw new ArgumentNullException(nameof(expression));
            if (value < 0 || value > Expr.Mask(expression.Width)) throw new ArgumentOutOfRangeException(nameof(value));
            var bp = new HardwareBreakpoint(nextBreakpoint++, name, expression, value, false);
            breakpoints.Add(bp); return bp;
        }
        /// <summary>Stop on a post-edge asserted one-bit hardware condition.</summary>
        public HardwareBreakpoint BreakWhenHigh(string name, Expr condition)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            if (condition.Width != 1) throw new ArgumentException("Breakpoint condition must be one bit.", nameof(condition));
            return BreakWhen(name, condition, BigInteger.One);
        }
        /// <summary>Stop when hardware ready and valid are both asserted after a clock edge.
        /// For edge-accurate transfer detection across the sampling boundary, watch the
        /// pre-edge handshake too; this is a post-edge inspection breakpoint.</summary>
        public HardwareBreakpoint BreakOnHandshake(string name, Expr valid, Expr ready)
        {
            if (valid == null || ready == null) throw new ArgumentNullException(valid == null ? nameof(valid) : nameof(ready));
            if (valid.Width != 1 || ready.Width != 1) throw new ArgumentException("Handshake signals must be one bit.");
            return BreakWhenHigh(name, valid & ready);
        }
        public HardwareBreakpoint BreakOnChange(string name, Expr expression)
        {
            if (expression == null) throw new ArgumentNullException(nameof(expression));
            var bp = new HardwareBreakpoint(nextBreakpoint++, name, expression, BigInteger.Zero, true);
            breakpoints.Add(bp); return bp;
        }
        public bool RemoveBreakpoint(int id) => breakpoints.RemoveAll(x => x.Id == id) != 0;
        public void ClearTrace() { trace.Clear(); }
        public void Step(bool reset = false)
        {
            if (MaxTraceSamples < 0) throw new ArgumentOutOfRangeException(nameof(MaxTraceSamples));
            simulator.Tick(reset);
            checked { Cycle++; }
            LastBreakpointId = null;
            foreach (var watch in watches)
                {
                    var value = simulator.Read(watch.Value);
                    if (MaxTraceSamples > 0)
                    {
                        trace.Add(new HardwareTraceSample(Cycle, watch.Key, value));
                        if (trace.Count > MaxTraceSamples) trace.RemoveRange(0, trace.Count - MaxTraceSamples);
                    }
                }
            foreach (var bp in breakpoints)
            {
                var value = simulator.Read(bp.Expression);
                // Change detection is armed by the first observed sample.
                bool hit = bp.OnChange ? bp.LastValue.HasValue && bp.LastValue.Value != value : value == bp.Value;
                bp.LastValue = value;
                // Reset establishes a new breakpoint baseline, rather than triggering stops.
                if (reset) hit = false;
                if (hit && !LastBreakpointId.HasValue) LastBreakpointId = bp.Id;
            }
            observer.AfterEdge(Cycle, reset);
        }
        public int Run(int maximumEdges)
        {
            if (maximumEdges < 0) throw new ArgumentOutOfRangeException(nameof(maximumEdges));
            for (int i = 0; i < maximumEdges; i++)
            {
                Step();
                if (LastBreakpointId.HasValue) return i + 1;
            }
            return maximumEdges;
        }
    }
}
