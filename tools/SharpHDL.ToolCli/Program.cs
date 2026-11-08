using SharpHDL.Tooling;
using System.Globalization;

// Validate the whole request before resolving tools or opening the evidence directory.
if (args.Length < 5 || args[0] != "run")
    return Fail("InvalidInput", "Usage: run <new-artifact-directory> <working-directory> <timeout-ms> <executable-or-name> [arguments...]", 2);
if (!int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out int timeout)
    || timeout is < 1 or > 3_600_000 || args.Any(arg => arg.Contains('\0')))
    return Fail("InvalidInput", "Use NUL-free arguments and a timeout in 1..3600000 milliseconds.", 2);

string evidence;
string working;
string? executable;
try
{
    evidence = Path.GetFullPath(args[1]);
    working = Path.GetFullPath(args[2]);
    if (!Directory.Exists(working) || !Directory.Exists(Path.GetDirectoryName(evidence)) || Path.Exists(evidence))
        return Fail("InvalidInput", "Use an existing working directory and a new evidence directory with an existing parent.", 2);
    executable = Path.IsPathFullyQualified(args[4]) ? Path.GetFullPath(args[4]) : HdlToolRunner.FindExecutable(args[4]);
}
catch (ArgumentException error)
{
    return Fail("InvalidInput", error.Message, 2);
}
catch (IOException error)
{
    return Fail("InvalidInput", error.Message, 2);
}
if (executable is null)
    return Fail("MissingExecutable", $"'{args[4]}' was not found on PATH.", 127);

using var cancelled = new CancellationTokenSource();
ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancelled.Cancel(); };
Console.CancelKeyPress += cancel;
try
{
    var result = await HdlToolRunner.RunAsync(executable, args[5..], working, evidence,
        TimeSpan.FromMilliseconds(timeout), cancelled.Token);
    int exitCode = result.Status switch
    {
        ToolRunStatus.Success => 0,
        ToolRunStatus.TimedOut => 124,
        ToolRunStatus.Cancelled => 130,
        ToolRunStatus.MissingExecutable => 127,
        ToolRunStatus.OutputLimitExceeded => 125,
        _ => result.ExitCode is > 0 and < 124 ? result.ExitCode.Value : 1
    };
    var output = exitCode == 0 ? Console.Out : Console.Error;
    output.WriteLine($"{result.Status}: {executable}; evidence {evidence}");
    if (result.Error is not null) output.WriteLine(result.Error);
    return exitCode;
}
catch (Exception error) when (error is IOException or UnauthorizedAccessException)
{
    return Fail("EvidenceIoFailure", $"{error.Message} Evidence directory: {evidence}", 1);
}
finally
{
    Console.CancelKeyPress -= cancel;
}

static int Fail(string code, string message, int exitCode)
{
    Console.Error.WriteLine($"{code}: {message}");
    return exitCode;
}
