using SharpHDL.Qualification;
using SharpHDL.Tooling;
using System.Diagnostics;

// Only the console adapter attaches stdout progress. Referencing this assembly
// from MCP does not execute the entry point or install a console listener.
using var progress = new ConsoleTraceListener();
Trace.Listeners.Add(progress);

return await BuildCampaign.RunConsoleResultAsync(token => EquivalenceCommand.Command.InvokeAsync(
    args.Length == 1 || (args.Length == 2 && args[1] == "--solver-only")
        ? new EquivalenceRequest(args[0], args.Length == 2) : null, token));
