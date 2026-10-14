using System;
using NSubstitute;
using SharpHDL;
using Xunit;

namespace SharpHDL.UnitTests;

public sealed class HardwareEventBreakpointTests
{
    [Fact]
    public void Post_edge_ready_valid_breakpoint_only_hits_when_both_asserted()
    {
        var m = new Module("HardwareHandshake");
        var enable = m.Input("enable", 1);
        var ready = m.Input("ready", 1);
        var valid = m.Register("valid", 1, 0);
        m.Next(valid, enable);
        var session = new HardwareDebugSession(m, Substitute.For<IHardwareDebugObserver>());
        var breakpoint = session.BreakOnHandshake("transfer-ready", valid, ready);
        session.Step(true);
        session.Set(enable, 1);
        session.Set(ready, 0);
        session.Step();
        Assert.Null(session.LastBreakpointId);
        session.Set(ready, 1);
        Assert.Equal(1, session.Run(1));
        Assert.Equal(breakpoint.Id, session.LastBreakpointId);
    }

    [Fact]
    public void Rejects_multibit_hardware_conditions()
    {
        var m = new Module("BadCondition");
        var bus = m.Input("bus", 8);
        var session = new HardwareDebugSession(m, Substitute.For<IHardwareDebugObserver>());
        Assert.Throws<ArgumentException>(() => session.BreakWhenHigh("invalid", bus));
    }
}
