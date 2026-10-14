using System;
using System.Numerics;
using NSubstitute;
using SharpHDL;
using Xunit;

namespace SharpHDL.UnitTests;

public sealed class DebugCheckpointTests
{
    [Fact]
    public void Replay_restores_cycle_watch_trace_and_breakpoint_history()
    {
        var module = new Module("CheckpointCounter");
        var count = module.Register("count", 8, 0);
        module.Next(count, count + Expr.Const(8, 1));
        var observer = Substitute.For<IHardwareDebugObserver>();
        var debug = new HardwareDebugSession(module, observer);
        debug.Watch("count", count);
        var bp = debug.BreakOnChange("changed", count);
        debug.Step(true);
        var checkpoint = debug.CaptureCheckpoint();

        Assert.Equal(1, debug.Run(5));
        Assert.Equal(bp.Id, debug.LastBreakpointId);
        Assert.Equal(new BigInteger(1), debug.Read(count));
        Assert.Equal(2, debug.Cycle);
        debug.RestoreCheckpoint(checkpoint);
        Assert.Equal(1, debug.Cycle);
        Assert.Null(debug.LastBreakpointId);
        Assert.Equal(BigInteger.Zero, debug.Read(count));
        Assert.Single(debug.Trace);
        Assert.Equal(BigInteger.Zero, debug.Trace[0].Value);

        // The change breakpoint must trigger identically after restoration.
        Assert.Equal(1, debug.Run(5));
        Assert.Equal(bp.Id, debug.LastBreakpointId);
        Assert.Equal(new BigInteger(1), debug.Read(count));
        Assert.Equal(2, debug.Cycle);
        // Restore must not invent an extra clock edge or observer notification.
        observer.Received(3).AfterEdge(Arg.Any<long>(), Arg.Any<bool>());
    }

    [Fact]
    public void Other_session_checkpoint_is_rejected()
    {
        var m = new Module("Owner");
        var q = m.Register("q", 8, 0);
        m.Next(q, q);
        var a = new HardwareDebugSession(m, Substitute.For<IHardwareDebugObserver>());
        var b = new HardwareDebugSession(m, Substitute.For<IHardwareDebugObserver>());
        Assert.Throws<ArgumentException>(() => b.RestoreCheckpoint(a.CaptureCheckpoint()));
    }

    [Fact]
    public void Changing_breakpoints_rejects_restore_without_mutating_simulator()
    {
        var m = new Module("ChangedBreakpoints");
        var q = m.Register("q", 8, 0);
        m.Next(q, q + Expr.Const(8, 1));
        var debug = new HardwareDebugSession(m, Substitute.For<IHardwareDebugObserver>());
        debug.Step(true);
        debug.BreakWhen("at 2", q, 2);
        var checkpoint = debug.CaptureCheckpoint();
        debug.Step();
        debug.BreakWhen("at 3", q, 3);
        Assert.Throws<InvalidOperationException>(() => debug.RestoreCheckpoint(checkpoint));
        Assert.Equal(new BigInteger(1), debug.Read(q));
        Assert.Equal(2, debug.Cycle);
    }
}
