using System;
using System.Numerics;
using SharpHDL;
using Xunit;

namespace SharpHDL.UnitTests;

public sealed class SimulatorSnapshotTests
{
    [Fact]
    public void Snapshot_restores_registers_inputs_and_ram_with_repeatable_replay()
    {
        var m = new Module("SnapshotRam");
        var address = m.Input("address", 4);
        var write = m.Input("write", 1);
        var data = m.Input("data", 8);
        var count = m.Register("count", 8, 0);
        m.Next(count, count + Expr.Const(8, 1));
        var ram = SynchronousRam.Create(m, "memory", 8, 16, address, write, data,
            Expr.Const(1, 1));
        var sim = m.Simulate();
        sim.Tick(true);
        sim.Set(address, 3);
        sim.Set(data, 42);
        sim.Set(write, 1);
        sim.Tick();
        sim.Set(write, 0);
        sim.Tick(); // synchronous read of cell written on previous edge
        Assert.Equal(new BigInteger(42), sim.Read(ram.ReadData));
        var checkpoint = sim.CaptureSnapshot();
        var savedCount = sim.Read(count);
        sim.Set(data, 91);
        sim.Set(write, 1);
        sim.Tick();
        sim.Set(write, 0);
        sim.Tick();
        Assert.Equal(new BigInteger(91), sim.Read(ram.ReadData));
        sim.RestoreSnapshot(checkpoint);
        Assert.Equal(savedCount, sim.Read(count));
        Assert.Equal(new BigInteger(42), sim.Read(ram.ReadData));
        sim.Tick();
        Assert.Equal(new BigInteger(42), sim.Read(ram.ReadData));
        // The same immutable checkpoint must work more than once.
        sim.RestoreSnapshot(checkpoint);
        Assert.Equal(savedCount, sim.Read(count));
        Assert.Equal(new BigInteger(42), sim.Read(ram.ReadData));
    }

    [Fact]
    public void Snapshot_rejects_a_different_elaborated_module()
    {
        var a = new Module("A");
        var ar = a.Register("r", 8, 0);
        a.Next(ar, ar);
        var b = new Module("B");
        var br = b.Register("r", 8, 0);
        b.Next(br, br);
        var snapshot = a.Simulate().CaptureSnapshot();
        Assert.Throws<ArgumentException>(() => b.Simulate().RestoreSnapshot(snapshot));
    }

    [Fact]
    public void Snapshot_before_reset_preserves_uninitialized_register_semantics()
    {
        var m = new Module("BeforeReset");
        var r = m.Register("r", 8, 0);
        m.Next(r, r + Expr.Const(8, 1));
        var sim = m.Simulate();
        var snapshot = sim.CaptureSnapshot();
        sim.Tick(true);
        Assert.Equal(BigInteger.Zero, sim.Read(r));
        sim.RestoreSnapshot(snapshot);
        Assert.Throws<InvalidOperationException>(() => sim.Read(r));
    }
}
