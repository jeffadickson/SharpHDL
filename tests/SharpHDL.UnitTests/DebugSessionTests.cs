using System;
using System.Numerics;
using NSubstitute;
using SharpHDL;
using Xunit;

namespace SharpHDL.UnitTests;

public sealed class DebugSessionTests
{
    [Fact]
    public void Reset_then_step_exposes_post_edge_values_and_notifies_once()
    {
        var module = new Module("DebugAccumulator");
        var enable = module.Input("enable", 1);
        var data = module.Input("data", 8);
        var count = module.Register("count", 8, 0);
        module.NextWhen(count, enable, count + data);
        var observer = Substitute.For<IHardwareDebugObserver>();
        var debug = new HardwareDebugSession(module, observer);

        Assert.Throws<InvalidOperationException>(() => debug.Read(count));
        debug.Step(reset: true);
        Assert.Equal(BigInteger.Zero, debug.Read(count));
        debug.Set(enable, 1);
        debug.Set(data, 7);
        debug.Step();
        Assert.Equal(new BigInteger(7), debug.Read(count));
        debug.Set(data, 250);
        debug.Step();
        Assert.Equal(new BigInteger(1), debug.Read(count)); // wrap modulo 256

        Assert.Equal(3, debug.Cycle);
        Received.InOrder(() =>
        {
            observer.AfterEdge(1, true);
            observer.AfterEdge(2, false);
            observer.AfterEdge(3, false);
        });
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 17)]
    [InlineData(2, 34)]
    public void Counter_reference_oracle_matches_each_edge(int steps, int expected)
    {
        var m = new Module("DebugCounter");
        var count = m.Register("count", 8, 0);
        m.Next(count, count + Expr.Const(8, 17));
        var observer = Substitute.For<IHardwareDebugObserver>();
        var session = new HardwareDebugSession(m, observer);
        session.Step(true);
        for (var i = 0; i < steps; i++) session.Step();
        Assert.Equal(new BigInteger(expected), session.Read(count));
        observer.Received(steps + 1).AfterEdge(Arg.Any<long>(), Arg.Any<bool>());
    }

    [Fact]
    public void Debug_session_rejects_missing_observer()
    {
        Assert.Throws<ArgumentNullException>(() => new HardwareDebugSession(new Module("MissingObserver"), null!));
    }
    [Fact]
    public void Watch_traces_and_breakpoint_stop_on_post_edge_value()
    {
        var module = new Module("WatchCounter");
        var count = module.Register("count", 8, 0);
        module.Next(count, count + Expr.Const(8, 1));
        var observer = Substitute.For<IHardwareDebugObserver>();
        var debug = new HardwareDebugSession(module, observer);
        debug.Watch("count", count);
        var bp = debug.BreakWhen("three", count, 3);
        debug.Step(true);
        Assert.Equal(3, debug.Run(10));
        Assert.Equal(bp.Id, debug.LastBreakpointId);
        Assert.Equal(4, debug.Cycle);
        Assert.Equal(new BigInteger(3), debug.Read(count));
        Assert.Equal(new BigInteger(3), debug.Trace[^1].Value);
        Assert.True(debug.RemoveBreakpoint(bp.Id));
        debug.ClearTrace();
        Assert.Empty(debug.Trace);
        Assert.Equal(2, debug.Run(2));
        observer.Received(6).AfterEdge(Arg.Any<long>(), Arg.Any<bool>());
    }

    [Fact]
    public void Change_breakpoint_arms_after_first_sample()
    {
        var module = new Module("ChangeCounter");
        var count = module.Register("count", 8, 0);
        module.Next(count, count + Expr.Const(8, 1));
        var debug = new HardwareDebugSession(module, Substitute.For<IHardwareDebugObserver>());
        var bp = debug.BreakOnChange("count changed", count);
        debug.Step(true);
        Assert.Null(debug.LastBreakpointId);
        Assert.Equal(1, debug.Run(10));
        Assert.Equal(bp.Id, debug.LastBreakpointId);
    }

    [Fact]
    public void Breakpoint_rejects_out_of_range_values()
    {
        var module = new Module("Range");
        var count = module.Register("count", 8, 0);
        module.Next(count, count);
        var debug = new HardwareDebugSession(module, Substitute.For<IHardwareDebugObserver>());
        Assert.Throws<ArgumentOutOfRangeException>(() => debug.BreakWhen("invalid", count, 256));
        Assert.Throws<ArgumentOutOfRangeException>(() => debug.Run(-1));
    }

    [Fact]
    public void Trace_retention_is_bounded_and_keeps_latest_samples()
    {
        var m = new Module("BoundedTrace");
        var q = m.Register("q", 8, 0);
        m.Next(q, q + Expr.Const(8, 1));
        var session = new HardwareDebugSession(m, Substitute.For<IHardwareDebugObserver>())
        {
            MaxTraceSamples = 3
        };
        session.Watch("q", q);
        session.Step(true);
        session.Run(6);
        Assert.Equal(3, session.Trace.Count);
        Assert.Equal(new long[] { 5, 6, 7 }, session.Trace.Select(x => x.Cycle).ToArray());
        Assert.Equal(new BigInteger(6), session.Trace[^1].Value);
        session.MaxTraceSamples = 0;
        session.Step();
        Assert.Equal(3, session.Trace.Count);
        session.ClearTrace();
        Assert.Empty(session.Trace);
    }

    [Fact]
    public void Reset_rearms_breakpoints_without_triggering()
    {
        var m = new Module("ResetRearm");
        var q = m.Register("q", 8, 0);
        m.Next(q, q + Expr.Const(8, 1));
        var session = new HardwareDebugSession(m, Substitute.For<IHardwareDebugObserver>());
        session.BreakWhen("zero", q, 0);
        session.Step(true);
        Assert.Null(session.LastBreakpointId);
        session.Step();
        Assert.Null(session.LastBreakpointId);
        session.Step(true);
        Assert.Null(session.LastBreakpointId);
    }

}
