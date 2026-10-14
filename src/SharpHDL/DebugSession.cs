using System;
using System.Numerics;

namespace SharpHDL
{
    /// <summary>A minimal hardware-clock debug seam, separate from CLR debugging.</summary>
    public interface IHardwareDebugObserver
    {
        void AfterEdge(long cycle, bool reset);
    }

    public sealed class HardwareDebugSession
    {
        readonly Simulator simulator;
        readonly IHardwareDebugObserver observer;
        public long Cycle { get; private set; }
        public HardwareDebugSession(Module module, IHardwareDebugObserver observer)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (observer == null) throw new ArgumentNullException(nameof(observer));
            simulator = module.Simulate();
            this.observer = observer;
        }
        public void Set(Signal input, BigInteger value) { simulator.Set(input, value); }
        public BigInteger Read(Expr expression) { return simulator.Read(expression); }
        public void Step(bool reset = false)
        {
            simulator.Tick(reset);
            checked { Cycle++; }
            observer.AfterEdge(Cycle, reset);
        }
    }
