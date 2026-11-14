using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using SharpHDL.Tooling;
using System.Text.Json;
using System.Text.Json.Schema;

var builder = Host.CreateApplicationBuilder(args);
// Stdout belongs exclusively to the MCP transport.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
var tools = ToolCommandRegistry.Commands.Select(command =>
    McpServerTool.Create(
        async (ToolRunRequest request, CancellationToken token) =>
        {
            var result = await ToolCommandRegistry.InvokeAsync(command.Id, request, token);
            var json = JsonSerializer.SerializeToElement(result, JsonSerializerOptions.Web);
            return new CallToolResult
            {
                IsError = result.ExitCode != 0,
                StructuredContent = json,
                Content = [new TextContentBlock { Text = json.GetRawText() }]
            };
        },
        new McpServerToolCreateOptions
        {
            Name = command.McpName,
            Description = command.Description,
            UseStructuredContent = true,
            OutputSchema = JsonSerializer.SerializeToElement(
                JsonSerializerOptions.Web.GetJsonSchemaAsNode(typeof(ToolCommandResult))),
            ReadOnly = false,
            Destructive = true,
            Idempotent = false,
            OpenWorld = true
        })).ToArray();
builder.Services.AddMcpServer().WithStdioServerTransport().WithTools(tools);
await builder.Build().RunAsync();
