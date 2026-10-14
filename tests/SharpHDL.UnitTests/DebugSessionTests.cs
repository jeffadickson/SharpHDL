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
}
