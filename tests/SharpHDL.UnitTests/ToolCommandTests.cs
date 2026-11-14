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
        return result.Status == ToolCommandStatus.InvalidInput && result.ExitCode == 2 && !Directory.Exists(output);
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
        Assert.Equal(status, result.Status.ToString());
        Assert.Equal(exit, result.ExitCode);
        Assert.Equal(result.Manifest!.Status.ToString(), result.Status.ToString());
        Assert.True(File.Exists(Path.Combine(output, "manifest.json")));
    }

    [Fact]
    public async Task Registry_rejects_unknown_and_malformed_requests_without_outputs()
    {
        string output = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var request = new ToolRunRequest("missing", [], AppContext.BaseDirectory, output, 0);
        Assert.Equal(ToolCommandStatus.InvalidInput, (await ToolCommandRegistry.InvokeAsync("unknown", request)).Status);
        Assert.Equal(ToolCommandStatus.InvalidInput, (await ToolCommandRegistry.InvokeAsync(ToolCommandRegistry.RunId, request)).Status);
        Assert.Equal(ToolCommandStatus.InvalidInput, (await ToolCommandRegistry.InvokeAsync(ToolCommandRegistry.RunId, null)).Status);
        Assert.False(Directory.Exists(output));
        Assert.Single(ToolCommandRegistry.Commands);
        Assert.All(ToolCommandRegistry.Commands, descriptor =>
        {
            Assert.False(string.IsNullOrWhiteSpace(descriptor.McpName));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Description));
        });
    }

    [Fact]
    public async Task Pre_cancelled_command_does_not_claim_evidence()
    {
        string output = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var request = new ToolRunRequest("dotnet", [], AppContext.BaseDirectory, output, 1000);
        var result = await ToolCommandRegistry.InvokeAsync(ToolCommandRegistry.RunId, request, new CancellationToken(true));
        Assert.Equal(ToolCommandStatus.Cancelled, result.Status);
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
        Assert.Equal(ToolCommandStatus.InvalidInput, (await ToolCommandRegistry.InvokeAsync(ToolCommandRegistry.RunId, existing)).Status);
        Assert.Equal(ToolCommandStatus.InvalidInput, (await ToolCommandRegistry.InvokeAsync(ToolCommandRegistry.RunId,
            request with { EvidenceDirectory = "\0" })).Status);
        Assert.Equal(ToolCommandStatus.InvalidInput, (await ToolCommandRegistry.InvokeAsync(ToolCommandRegistry.RunId,
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

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(123, 123)]
    [InlineData(124, 1)]
    [InlineData(null, 1)]
    public void Reserved_process_exits_cannot_escape_mapping(int? processExit, int expected) =>
        Assert.Equal(expected, ToolCommandRegistry.ExitCode(ToolRunStatus.NonzeroExit, processExit));

    [Fact]
    public async Task Input_shape_and_timeout_edges_are_validated_before_dispatch()
    {
        string output = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var request = new ToolRunRequest("SharpHDL_absent_" + Guid.NewGuid().ToString("N"),
            [], AppContext.BaseDirectory, output, 1);
        foreach (int timeout in new[] { 1, 3_600_000 })
        {
            var missing = await ToolCommandRegistry.InvokeAsync(ToolCommandRegistry.RunId,
                request with { TimeoutMilliseconds = timeout });
            Assert.Equal(ToolCommandStatus.MissingExecutable, missing.Status);
            Assert.Contains(request.Executable, missing.Diagnostic);
        }
        foreach (var malformed in new[]
        {
            request with { Executable = "" }, request with { Arguments = null! },
            request with { WorkingDirectory = null! }, request with { EvidenceDirectory = null! },
            request with { WorkingDirectory = Path.Combine(output, "absent") },
            request with { TimeoutMilliseconds = 3_600_001 }
        })
        {
            var rejected = await ToolCommandRegistry.InvokeAsync(ToolCommandRegistry.RunId, malformed);
            Assert.Equal(ToolCommandStatus.InvalidInput, rejected.Status);
            Assert.False(string.IsNullOrWhiteSpace(rejected.Diagnostic));
        }
        Assert.False(Directory.Exists(output));
    }
}
