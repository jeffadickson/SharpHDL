using SharpHDL.Tooling;
using System.Globalization;
using System.Text.Json;

// CLI syntax remains an adapter. Registry validation is shared with automation.
if (args is ["list"])
{
    Console.WriteLine(JsonSerializer.Serialize(ToolCommandRegistry.Commands));
    return 0;
}
if (args.Length < 5 || args[0] != "run" ||
    !int.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int timeout))
{
    Console.Error.WriteLine("InvalidInput: Usage: run <new-artifact-directory> <working-directory> <timeout-ms> <executable-or-name> [arguments...]");
    return 2;
}

using var cancelled = new CancellationTokenSource();
ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancelled.Cancel(); };
Console.CancelKeyPress += cancel;
try
{
    var request = new ToolRunRequest(args[4], args[5..], args[2], args[1], timeout);
    var result = await ToolCommandRegistry.InvokeAsync(ToolCommandRegistry.RunId, request, cancelled.Token);
    var output = result.ExitCode == 0 ? Console.Out : Console.Error;
    output.WriteLine($"{result.Status}: {result.Diagnostic}");
    return result.ExitCode;
}
finally
{
    Console.CancelKeyPress -= cancel;
}
