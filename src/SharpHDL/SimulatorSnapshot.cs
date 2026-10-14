using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SharpHDL
{
    /// <summary>An immutable-by-contract in-process checkpoint for deterministic replay.
    /// A snapshot belongs to exactly one elaborated Module instance.</summary>
    public sealed class SimulatorSnapshot
    {
        internal readonly Module Owner;
        internal readonly Dictionary<Signal, BigInteger> RegistersAndInputs;
        internal readonly Dictionary<SynchronousRam, BigInteger[]> RamContents;
        internal SimulatorSnapshot(Module owner,
            Dictionary<Signal, BigInteger> registersAndInputs,
            Dictionary<SynchronousRam, BigInteger[]> ramContents)
        {
            Owner = owner;
            RegistersAndInputs = new Dictionary<Signal, BigInteger>(registersAndInputs);
            RamContents = ramContents.ToDictionary(p => p.Key, p => (BigInteger[])p.Value.Clone());
        }
    }

    public sealed partial class Simulator
    {
        /// <summary>Capture inputs, initialized registers, and all RAM words.
        /// Snapshotting before reset is supported; uninitialized registers remain uninitialized.</summary>
        public SimulatorSnapshot CaptureSnapshot()
        {
            return new SimulatorSnapshot(module, state, memories);
        }

        /// <summary>Atomically restore a checkpoint from the same elaborated module.
        /// The snapshot may be applied repeatedly and to independent simulators of that module.</summary>
        public void RestoreSnapshot(SimulatorSnapshot snapshot)
        {
            if(snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if(!ReferenceEquals(module, snapshot.Owner))
                throw new ArgumentException("Snapshot belongs to a different elaborated module.", nameof(snapshot));
            // Build copies first, so a failed validation never partially mutates live state.
            var nextState = new Dictionary<Signal, BigInteger>(snapshot.RegistersAndInputs);
            var nextMemories = snapshot.RamContents.ToDictionary(p => p.Key, p => (BigInteger[])p.Value.Clone());
            if(nextMemories.Count != memories.Count || nextMemories.Any(p =>
                !memories.ContainsKey(p.Key) || p.Value.Length != p.Key.Depth))
                throw new InvalidOperationException("Snapshot memory layout differs.");
            state.Clear();
            foreach(var item in nextState) state.Add(item.Key, item.Value);
            memories.Clear();
            foreach(var item in nextMemories) memories.Add(item.Key, item.Value);
        }
    }
}
