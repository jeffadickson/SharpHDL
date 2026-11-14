using SharpHDL.Tooling;
using Xunit;

namespace SharpHDL.UnitTests;

public sealed class ToolCommandTests
{
    [FsCheck.Xunit.Property(MaxTest = 40, Replay = "1145655947,296144285")]
    public async Task<bool> Invalid_timeouts_never_create_output(int value)
    {
        int timeout = value > 0 ? -value : value;
        string output = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var request = new ToolRunRequest("dotnet", [], AppContext.BaseDirectory, output, timeout);
        var result = await ToolCommandRegistry.InvokeAsync(ToolCommandRegistry.RunId, request);
        return result.Status == "InvalidInput" && result.ExitCode == 2 && !Directory.Exists(output);
    }

    [Theory]
    [InlineData("echo", "Success", 0)]
    [InlineData("fail", "NonzeroExit", 23)]
    public async Task Registry_preserves_real_process_outcome(string mode, string status, int exit)
    {
        string parent = Environment.GetEnvironmentVariable("CODEX_ARTIFACT_ROOT") ?? Path.GetTempPath();
        Assert.True(Directory.Exists(parent));
        string output = Path.Combine(parent, "sharphdl-command-test-" + Guid.NewGuid().ToString("N"));
        string fixture = typeof(ToolFixtureMarker).Assembly.Location;
        var request = new ToolRunRequest(HdlToolRunner.FindExecutable("dotnet")!,
            ["exec", "--runtimeconfig", Path.Combine(AppContext.BaseDirectory, "SharpHDL.ToolFixture.runtimeconfig.json"), fixture, mode],
            AppContext.BaseDirectory, output, 10000);
        var result = await ToolCommandRegistry.InvokeAsync(ToolCommandRegistry.RunId, request);
        Assert.Equal(status, result.Status);
        Assert.Equal(exit, result.ExitCode);
        Assert.Equal(result.Manifest!.Status.ToString(), result.Status);
        Assert.True(File.Exists(Path.Combine(output, "manifest.json")));
    }

    [Fact]
    public async Task Registry_rejects_unknown_and_malformed_requests_without_outputs()
    {
        string output = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var request = new ToolRunRequest("missing", [], AppContext.BaseDirectory, output, 0);
        Assert.Equal("InvalidInput", (await ToolCommandRegistry.InvokeAsync("unknown", request)).Status);
        Assert.Equal("InvalidInput", (await ToolCommandRegistry.InvokeAsync(ToolCommandRegistry.RunId, request)).Status);
        Assert.Equal("InvalidInput", (await ToolCommandRegistry.InvokeAsync(ToolCommandRegistry.RunId, null)).Status);
        Assert.False(Directory.Exists(output));
        Assert.Single(ToolCommandRegistry.Commands);
    }

    [Fact]
    public async Task Pre_cancelled_command_does_not_claim_evidence()
    {
        string output = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var request = new ToolRunRequest("dotnet", [], AppContext.BaseDirectory, output, 1000);
        var result = await ToolCommandRegistry.InvokeAsync(ToolCommandRegistry.RunId, request, new CancellationToken(true));
        Assert.Equal("Cancelled", result.Status);
        Assert.Equal(130, result.ExitCode);
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public async Task Missing_and_existing_destinations_have_explicit_outcomes()
    {
        string parent = Environment.GetEnvironmentVariable("CODEX_ARTIFACT_ROOT") ?? Path.GetTempPath();
        string output = Path.Combine(parent, "sharphdl-missing-" + Guid.NewGuid().ToString("N"));
        var request = new ToolRunRequest("SharpHDL_nonexistent_" + Guid.NewGuid().ToString("N"),
            [], AppContext.BaseDirectory, output, 1000);
        Assert.Equal(127, (await ToolCommandRegistry.InvokeAsync(ToolCommandRegistry.RunId, request)).ExitCode);
        Assert.False(Directory.Exists(output));
        var existing = request with { EvidenceDirectory = AppContext.BaseDirectory };
        Assert.Equal("InvalidInput", (await ToolCommandRegistry.InvokeAsync(ToolCommandRegistry.RunId, existing)).Status);
        Assert.Equal("InvalidInput", (await ToolCommandRegistry.InvokeAsync(ToolCommandRegistry.RunId,
            request with { EvidenceDirectory = "\0" })).Status);
        Assert.Equal("InvalidInput", (await ToolCommandRegistry.InvokeAsync(ToolCommandRegistry.RunId,
            request with { Arguments = [null!] })).Status);
    }

    [Theory]
    [InlineData(ToolRunStatus.Success, 0)]
    [InlineData(ToolRunStatus.NonzeroExit, 23)]
    [InlineData(ToolRunStatus.TimedOut, 124)]
    [InlineData(ToolRunStatus.Cancelled, 130)]
    [InlineData(ToolRunStatus.MissingExecutable, 127)]
    [InlineData(ToolRunStatus.OutputLimitExceeded, 125)]
    [InlineData(ToolRunStatus.EvidenceIoFailure, 23)]
    public void Exit_mapping_preserves_existing_cli_contract(ToolRunStatus status, int expected) =>
        Assert.Equal(expected, ToolCommandRegistry.ExitCode(status, 23));
}
