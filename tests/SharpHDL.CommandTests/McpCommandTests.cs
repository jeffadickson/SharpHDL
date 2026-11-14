using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SharpHDL.Tooling;
using Xunit;

namespace SharpHDL.CommandTests;

public sealed class McpCommandTests
{
    private static string Dotnet => HdlToolRunner.FindExecutable("dotnet")!;
    private static string AssemblyPath(string name) => Path.Combine(AppContext.BaseDirectory, name + ".dll");
    private static string RuntimePath(string name) => Path.Combine(AppContext.BaseDirectory, name + ".runtimeconfig.json");
    private static string Root => Environment.GetEnvironmentVariable("CODEX_ARTIFACT_ROOT") ?? Path.GetTempPath();
    private static string NewEvidence() => Path.Combine(Root, "sharphdl-mcp-" + Guid.NewGuid().ToString("N"));

    private static Task<McpClient> ConnectAsync() =>
        McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "SharpHDL integration",
            Command = Dotnet,
            Arguments = ["exec", "--runtimeconfig", RuntimePath("SharpHDL.Mcp"), AssemblyPath("SharpHDL.Mcp")]
        }));

    private static Dictionary<string, object?> Input(string mode, string evidence, params string[] values) => new()
    {
        ["request"] = new ToolRunRequest(Dotnet,
            ["exec", "--runtimeconfig", RuntimePath("SharpHDL.ToolFixture"), AssemblyPath("SharpHDL.ToolFixture"), mode, ..values],
            AppContext.BaseDirectory, evidence, 15000)
    };

    private static JsonElement Outcome(CallToolResult result)
    {
        Assert.NotNull(result.StructuredContent);
        return JsonSerializer.SerializeToElement(result.StructuredContent);
    }

    [Fact]
    public async Task Discovery_matches_registry_and_invocation_preserves_process_failures()
    {
        await using var client = await ConnectAsync();
        var tools = await client.ListToolsAsync();
        Assert.Equal(ToolCommandRegistry.Commands.Select(item => item.McpName), tools.Select(item => item.Name));
        Assert.Contains("request", tools.Single().JsonSchema.GetRawText());
        string cliEvidence = NewEvidence();
        var cli = await HdlToolRunner.RunAsync(Dotnet,
            ["exec", "--runtimeconfig", RuntimePath("SharpHDL.ToolCli"), AssemblyPath("SharpHDL.ToolCli"), "list"],
            AppContext.BaseDirectory, cliEvidence, TimeSpan.FromSeconds(15));
        Assert.Equal(ToolRunStatus.Success, cli.Status);
        var discovered = JsonSerializer.Deserialize<ToolCommandDescriptor[]>(
            await File.ReadAllTextAsync(Path.Combine(cliEvidence, "stdout.bin")));
        Assert.Equal(ToolCommandRegistry.Commands, discovered);
        foreach (var (mode, status, exit) in new[] { ("echo", "Success", 0), ("fail", "NonzeroExit", 23) })
        {
            string output = NewEvidence();
            var response = await client.CallToolAsync("sharphdl_tool_run", Input(mode, output, "literal ; & | λ"));
            var result = Outcome(response);
            Assert.Equal(exit != 0, response.IsError);
            Assert.Equal(status, result.GetProperty("status").GetString());
            Assert.Equal(exit, result.GetProperty("exitCode").GetInt32());
            Assert.True(File.Exists(Path.Combine(output, "manifest.json")));
            string cliOutput = NewEvidence();
            var request = (ToolRunRequest)Input(mode, cliOutput, "literal ; & | λ")["request"]!;
            string cliCapture = NewEvidence();
            var cliResult = await HdlToolRunner.RunAsync(Dotnet,
                ["exec", "--runtimeconfig", RuntimePath("SharpHDL.ToolCli"), AssemblyPath("SharpHDL.ToolCli"),
                    "run", request.EvidenceDirectory, request.WorkingDirectory, "15000",
                    request.Executable, ..request.Arguments],
                AppContext.BaseDirectory, cliCapture, TimeSpan.FromSeconds(20));
            Assert.Equal(exit, cliResult.ExitCode);
            Assert.StartsWith(status + ": ", await File.ReadAllTextAsync(Path.Combine(cliCapture,
                exit == 0 ? "stdout.bin" : "stderr.bin")));
            if (mode == "echo")
                Assert.Equal(new[] { "literal ; & | λ" }, JsonSerializer.Deserialize<string[]>(
                    await File.ReadAllTextAsync(Path.Combine(output, "stdout.bin"))));
        }
    }

    [Fact]
    public async Task Invalid_input_returns_typed_failure_and_session_remains_usable()
    {
        await using var client = await ConnectAsync();
        string output = NewEvidence();
        var input = Input("echo", output);
        input["request"] = ((ToolRunRequest)input["request"]!) with { TimeoutMilliseconds = 0 };
        var response = await client.CallToolAsync("sharphdl_tool_run", input);
        Assert.True(response.IsError);
        var result = Outcome(response);
        Assert.Equal("InvalidInput", result.GetProperty("status").GetString());
        Assert.False(Directory.Exists(output));
        Assert.Single(await client.ListToolsAsync());
    }

    [Fact]
    public async Task Malformed_protocol_argument_cannot_start_execution()
    {
        await using var client = await ConnectAsync();
        var response = await client.CallToolAsync("sharphdl_tool_run",
            new Dictionary<string, object?> { ["request"] = "not an object" });
        Assert.True(response.IsError);
        Assert.Single(await client.ListToolsAsync());
    }

    [Fact]
    public async Task Protocol_cancellation_stops_the_tool_and_retains_cancelled_manifest()
    {
        await using var client = await ConnectAsync();
        using var cancellation = new CancellationTokenSource();
        string output = NewEvidence();
        string ready = NewEvidence() + ".ready";
        Task<CallToolResult> invocation = client.CallToolAsync("sharphdl_tool_run", Input("sleep", output, ready),
            cancellationToken: cancellation.Token).AsTask();
        // External process readiness is authoritative. Bound reconciliation to
        // this test rather than relying on a fixed startup delay.
        try
        {
            for (int attempt = 0; !File.Exists(ready) && attempt < 200; attempt++)
                await Task.Delay(50);
            Assert.True(File.Exists(ready), "Tool did not signal readiness.");
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await invocation);
            string manifest = Path.Combine(output, "manifest.json");
            for (int attempt = 0; !File.Exists(manifest) && attempt < 100; attempt++)
                await Task.Delay(50);
            using var retained = JsonDocument.Parse(await File.ReadAllTextAsync(manifest));
            Assert.Equal("Cancelled", retained.RootElement.GetProperty("Status").GetString());
            Assert.Single(await client.ListToolsAsync());
        }
        finally
        {
            cancellation.Cancel();
            try { await invocation; } catch (OperationCanceledException) { }
        }
    }
}
