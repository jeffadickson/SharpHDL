using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;

namespace SharpHDL.Tooling;

/// <summary>Terminal outcomes exposed by an automated build or verification command.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CampaignStatus>))]
public enum CampaignStatus
{
    /// <summary>Every stage finished and completion was published.</summary>
    Success,
    /// <summary>The caller supplied an invalid command or destination.</summary>
    InvalidInput,
    /// <summary>A required executable could not be resolved.</summary>
    MissingDependency,
    /// <summary>A stage failed or its evidence could not be published.</summary>
    Failed,
    /// <summary>The caller cancelled before completion was published.</summary>
    Cancelled,
    /// <summary>An unexpected fault occurred. Diagnostic retains the exception details.</summary>
    UnexpectedFailure
}

/// <summary>A terminal outcome with stable command and stage IDs, evidence locations and a domain outcome code.</summary>
public sealed record CampaignResult(string Operation, string Stage, CampaignStatus Status,
    string Diagnostic, string? EvidenceDirectory, string? StageEvidenceDirectory, string? DetailCode);

/// <summary>Owns one command's cancellation and evidence. Stage outputs are diagnostic until the completion record exists.</summary>
public sealed class BuildCampaign
{
    private readonly string operation;
    private string stage = "validate";
    private string? root;
    private string? stageEvidence;

    internal BuildCampaign(string operation, CancellationToken token) =>
        (this.operation, Token) = (operation, token);

    /// <summary>The same caller token is passed to every cancellable operation in the campaign.</summary>
    public CancellationToken Token { get; }

    /// <summary>Resolve an explicit absolute path or a simple executable name on PATH.</summary>
    public static string? ResolveExecutable(string argument)
    {
        if (!Path.IsPathFullyQualified(argument)) return HdlToolRunner.FindExecutable(argument);
        return File.Exists(argument) ? Path.GetFullPath(argument) : null;
    }

    /// <summary>Claim a new evidence directory after command validation. Returns a failure when the destination is invalid.</summary>
    public CampaignResult? Begin(string directory)
    {
        Token.ThrowIfCancellationRequested();
        if (root is not null) throw new InvalidOperationException("The campaign already owns an evidence directory.");
        if (!Path.IsPathFullyQualified(directory) || !Directory.Exists(Path.GetDirectoryName(directory)) || Path.Exists(directory))
            return Result(CampaignStatus.InvalidInput, "Use a new evidence directory with an existing parent.");
        Directory.CreateDirectory(directory);
        // CreateDirectory itself permits concurrent callers. Claim the run before
        // writing any stage data so a racing command cannot publish our result.
        using (new FileStream(Path.Combine(directory, ".campaign-claim"), FileMode.CreateNew)) { }
        root = directory;
        return null;
    }

    /// <summary>Record a stable stage name and check for cancellation before starting work.</summary>
    public void EnterStage(string name, string? evidenceDirectory = null)
    {
        stage = name;
        stageEvidence = evidenceDirectory;
        Token.ThrowIfCancellationRequested();
    }

    /// <summary>Describe an operation outcome using the current stage and its evidence.</summary>
    public CampaignResult Result(CampaignStatus status, string diagnostic, string? detailCode = null) =>
        new(operation, stage, status, diagnostic, root, stageEvidence, detailCode);

    /// <summary>Preserve a subprocess failure's original status and diagnostic at the current stage.</summary>
    public CampaignResult ToolFailure(ToolRunManifest run) =>
        Result(run.Status switch
        {
            ToolRunStatus.Cancelled => CampaignStatus.Cancelled,
            ToolRunStatus.MissingExecutable => CampaignStatus.MissingDependency,
            _ => CampaignStatus.Failed
        }, run.Error ?? $"Tool {run.Executable} ended with {run.Status}.", run.Status.ToString());

    /// <summary>Run a command with Ctrl+C propagation, atomically publish its terminal record and return a stable exit code.</summary>
    public static Task<int> RunConsoleAsync(string operation, Func<BuildCampaign, Task<CampaignResult>> execute) =>
        RunConsoleResultAsync(token => ExecuteAsync(operation, execute, token));

    /// <summary>Execute a campaign without console interaction and retain its terminal outcome.</summary>
    public static Task<CampaignResult> ExecuteAsync(string operation,
        Func<BuildCampaign, Task<CampaignResult>> execute, CancellationToken token = default) =>
        new BuildCampaign(operation, token).RunAsync(campaign =>
        {
            token.ThrowIfCancellationRequested();
            return execute(campaign);
        });

    /// <summary>Adapt a typed command to Ctrl+C and the established JSON/exit-code console contract.</summary>
    public static async Task<int> RunConsoleResultAsync(Func<CancellationToken, Task<CampaignResult>> execute)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var result = await execute(cancellation.Token);
            var output = result.Status == CampaignStatus.Success ? Console.Out : Console.Error;
            await output.WriteLineAsync(JsonSerializer.Serialize(result, CampaignJsonContext.Default.CampaignResult));
            return result.Status switch
            {
                CampaignStatus.Success => 0,
                CampaignStatus.InvalidInput => 2,
                CampaignStatus.MissingDependency => 127,
                CampaignStatus.Cancelled => 130,
                _ => 1
            };
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }

    internal async Task<CampaignResult> RunAsync(Func<BuildCampaign, Task<CampaignResult>> execute)
    {
        CampaignResult result;
        try { result = await execute(this); }
        catch (OperationCanceledException) when (Token.IsCancellationRequested)
        {
            result = Result(CampaignStatus.Cancelled, "Cancelled by the caller.");
        }
        catch (ArgumentException error) when (stage == "validate")
        {
            result = Result(CampaignStatus.InvalidInput, error.Message);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or XmlException)
        {
            result = Result(CampaignStatus.Failed, error.Message, error.GetType().Name);
        }
        catch (Exception error)
        {
            result = Result(CampaignStatus.UnexpectedFailure, error.ToString(), error.GetType().Name);
        }
        if (root is null)
            return result.Status == CampaignStatus.Success
                ? Result(CampaignStatus.UnexpectedFailure, "The command reported success without claiming an evidence directory.")
                : result;

        if (result.Status == CampaignStatus.Success)
        {
            try
            {
                EnterStage("publish-completion");
                result = result with { Stage = "complete", StageEvidenceDirectory = null };
                await PublishAsync("campaign-complete.json", result, Token);
                return result;
            }
            catch (OperationCanceledException) when (Token.IsCancellationRequested)
            {
                result = Result(CampaignStatus.Cancelled, "Cancelled before completion was published.");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                result = Result(CampaignStatus.Failed, error.Message, error.GetType().Name);
            }
        }

        // Retain the failure record even when the caller has already cancelled.
        try { await PublishAsync("campaign-failure.json", result, CancellationToken.None); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            result = result with { Diagnostic = $"{result.Diagnostic} Could not publish failure evidence: {error.Message}" };
        }
        return result;
    }

    private async Task PublishAsync(string name, CampaignResult result, CancellationToken token)
    {
        string destination = Path.Combine(root!, name);
        string pending = destination + ".pending";
        await using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            await JsonSerializer.SerializeAsync(stream, result, CampaignJsonContext.Default.CampaignResult, token);
        // The rename is the commit point. An interrupted write remains a pending file.
        token.ThrowIfCancellationRequested();
        File.Move(pending, destination);
    }
}

[JsonSerializable(typeof(CampaignResult))]
internal partial class CampaignJsonContext : JsonSerializerContext;
