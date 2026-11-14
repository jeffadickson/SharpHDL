namespace SharpHDL.Tooling;

/// <summary>Boundary outcomes, including validation before process execution.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<ToolCommandStatus>))]
public enum ToolCommandStatus
{
    /// <summary>The process succeeded with complete retained output.</summary>
    Success,
    /// <summary>The request failed validation.</summary>
    InvalidInput,
    /// <summary>The process exited nonzero.</summary>
    NonzeroExit,
    /// <summary>The execution deadline expired.</summary>
    TimedOut,
    /// <summary>The caller cancelled execution.</summary>
    Cancelled,
    /// <summary>The selected executable could not be found.</summary>
    MissingExecutable,
    /// <summary>The operating system rejected process creation.</summary>
    FailedToStart,
    /// <summary>Captured output exceeded the runner budget.</summary>
    OutputLimitExceeded,
    /// <summary>Evidence could not be captured or published.</summary>
    EvidenceIoFailure
}

/// <summary>Input for shell-free tool execution. Timeout is in milliseconds.</summary>
public sealed record ToolRunRequest(string Executable, string[] Arguments, string WorkingDirectory,
    string EvidenceDirectory, int TimeoutMilliseconds);

/// <summary>A discoverable command identity. MCP uses a portable name for the same operation.</summary>
public sealed record ToolCommandDescriptor(string Id, string McpName, string Description);

/// <summary>Expected boundary outcome with the original process manifest when execution began.</summary>
public sealed record ToolCommandResult(string Operation, ToolCommandStatus Status, int ExitCode, string Diagnostic,
    string? EvidenceDirectory = null, ToolRunManifest? Manifest = null);

/// <summary>Shared tool command discovery, validation and dispatch for local adapters.</summary>
public static class ToolCommandRegistry
{
    /// <summary>Stable identity of the tool execution command.</summary>
    public const string RunId = "sharphdl.tool.run";

    /// <summary>The supported command inventory. These commands accept ToolRunRequest.</summary>
    public static IReadOnlyList<ToolCommandDescriptor> Commands { get; } = Array.AsReadOnly(
        new[] { new ToolCommandDescriptor(RunId, "sharphdl_tool_run",
            "Run a trusted local HDL tool with distinct arguments, a bounded timeout and retained evidence. Creates files and cannot be undone.") });

    /// <summary>Validate the whole request before resolving executables or claiming evidence.</summary>
    public static async Task<ToolCommandResult> InvokeAsync(string commandId, ToolRunRequest? request,
        CancellationToken cancellationToken = default)
    {
        ToolCommandResult Fail(ToolCommandStatus status, string diagnostic, int exit) =>
            new(commandId, status, exit, diagnostic);
        if (commandId != RunId || request is null)
            return Fail(ToolCommandStatus.InvalidInput, "Expected a registered command and a tool request.", 2);
        if (cancellationToken.IsCancellationRequested)
            return Fail(ToolCommandStatus.Cancelled, "Cancelled before execution.", 130);
        if (string.IsNullOrWhiteSpace(request.Executable) || request.Arguments is null ||
            request.WorkingDirectory is null || request.EvidenceDirectory is null ||
            request.TimeoutMilliseconds is < 1 or > 3_600_000 ||
            request.Arguments.Any(argument => argument is null || argument.Contains('\0')) ||
            request.Executable.Contains('\0'))
            return Fail(ToolCommandStatus.InvalidInput, "Use NUL-free arguments and a timeout in 1..3600000 milliseconds.", 2);

        string evidence;
        string working;
        string? executable;
        try
        {
            evidence = Path.GetFullPath(request.EvidenceDirectory);
            working = Path.GetFullPath(request.WorkingDirectory);
            if (!Directory.Exists(working) || !Directory.Exists(Path.GetDirectoryName(evidence)) || Path.Exists(evidence))
                return Fail(ToolCommandStatus.InvalidInput, "Use an existing working directory and a new evidence directory with an existing parent.", 2);
            executable = Path.IsPathFullyQualified(request.Executable)
                ? Path.GetFullPath(request.Executable) : HdlToolRunner.FindExecutable(request.Executable);
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return Fail(ToolCommandStatus.InvalidInput, error.Message, 2);
        }
        if (executable is null)
            return Fail(ToolCommandStatus.MissingExecutable, $"'{request.Executable}' was not found on PATH.", 127);

        try
        {
            var manifest = await HdlToolRunner.RunAsync(executable, request.Arguments, working, evidence,
                TimeSpan.FromMilliseconds(request.TimeoutMilliseconds), cancellationToken);
            // Outcome names deliberately match the runner contract. New runner
            // statuses must be admitted to the boundary explicitly.
            return new(commandId, Enum.Parse<ToolCommandStatus>(manifest.Status.ToString()), ExitCode(manifest.Status, manifest.ExitCode),
                manifest.Error ?? $"{manifest.Status}: {executable}; evidence {evidence}", evidence, manifest);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new(commandId, ToolCommandStatus.EvidenceIoFailure, 1, error.Message, evidence);
        }
    }

    /// <summary>Preserve the established CLI mapping for process and evidence outcomes.</summary>
    public static int ExitCode(ToolRunStatus status, int? processExit) => status switch
    {
        ToolRunStatus.Success => 0,
        ToolRunStatus.TimedOut => 124,
        ToolRunStatus.Cancelled => 130,
        ToolRunStatus.MissingExecutable => 127,
        ToolRunStatus.OutputLimitExceeded => 125,
        _ => processExit is > 0 and < 124 ? processExit.Value : 1
    };
}
