using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using SharpHDL.Documentation;
using SharpHDL.Qualification;
using SharpHDL.Tooling;
using System.Text.Json;
using System.Text.Json.Schema;

var builder = Host.CreateApplicationBuilder(args);
// Stdout belongs exclusively to the MCP transport. The command registrations
// hold the same delegates and metadata consumed by their console adapters.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
McpServerTool[] tools =
[
    CreateTool(ToolCommandRegistry.Command, result => result.ExitCode != 0),
    CreateTool(DocumentationCommand.Command, result => result.Status != CampaignStatus.Success),
    CreateTool(EquivalenceCommand.Command, result => result.Status != CampaignStatus.Success)
];
builder.Services.AddMcpServer().WithStdioServerTransport().WithTools(tools);
await builder.Build().RunAsync();

static McpServerTool CreateTool<TRequest, TResult>(RegisteredCommand<TRequest, TResult> command,
    Func<TResult, bool> isError) =>
    McpServerTool.Create(async (TRequest request, CancellationToken token) =>
    {
        var result = await command.InvokeAsync(request, token);
        var json = JsonSerializer.SerializeToElement(result, JsonSerializerOptions.Web);
        return new CallToolResult
        {
            IsError = isError(result),
            StructuredContent = json,
            Content = [new TextContentBlock { Text = json.GetRawText() }]
        };
    }, new McpServerToolCreateOptions
    {
        Name = command.Descriptor.McpName,
        Description = command.Descriptor.Description,
        UseStructuredContent = true,
        OutputSchema = JsonSerializer.SerializeToElement(
            JsonSerializerOptions.Web.GetJsonSchemaAsNode(typeof(TResult))),
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = true
    });
