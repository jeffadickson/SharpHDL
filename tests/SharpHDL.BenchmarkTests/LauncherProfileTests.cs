using SharpHDL.Benchmarking;
using Xunit;

namespace SharpHDL.BenchmarkTests;

/// <summary>Pure profile admission tests never invoke a native compiler.</summary>
public sealed class LauncherProfileTests
{
    /// <summary>Both ordinary and controlled invocations default to managed execution.</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void Native_compilation_requires_an_explicit_switch(bool qualified)
    {
        var args=qualified?new[]{"--filter","*Observed*","--qualified-baseline"}:new[]{"--filter","*Observed*"};
        var profile=BaselineRunOptions.Parse(args);args[0]="changed";
        Assert.Equal(qualified,profile.Qualified);Assert.False(profile.IncludeNativeAot);
        Assert.Equal(4,profile.ExpectedMeasurementCount);Assert.Equal(new[]{"--filter","*Observed*"},profile.Arguments);
    }
    /// <summary>Opt-in changes the expected matrix without altering forwarded arguments.</summary>
    [Fact]
    public void Optional_native_profile_remains_available_without_becoming_a_default()
    {
        var profile=BaselineRunOptions.Parse(["--include-native-aot","--filter","*Observed*","--qualified-baseline","--artifacts","space path"]);
        Assert.True(profile.Qualified);Assert.True(profile.IncludeNativeAot);Assert.Equal(8,profile.ExpectedMeasurementCount);
        Assert.Equal(new[]{"--filter","*Observed*","--artifacts","space path"},profile.Arguments);
    }
    /// <summary>Null, duplicated or incomplete controls fail before BenchmarkDotNet can start work.</summary>
    [Fact]
    public void Invalid_controls_fail_closed()
    {
        Assert.Throws<ArgumentNullException>(()=>BaselineRunOptions.Parse(null!));
        foreach(var args in new[]{new string[]{null!},new[]{"--include-native-aot"},
            new[]{"--qualified-baseline","--qualified-baseline"},
            new[]{"--qualified-baseline","--include-native-aot","--include-native-aot"}})
            Assert.Throws<ArgumentException>(()=>BaselineRunOptions.Parse(args));
        Assert.Empty(BaselineRunOptions.Parse([]).Arguments);
    }
}
