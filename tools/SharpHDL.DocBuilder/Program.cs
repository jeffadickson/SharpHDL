using SharpHDL.Documentation;
using SharpHDL.Tooling;

return await BuildCampaign.RunConsoleResultAsync(token => DocumentationCommand.Command.InvokeAsync(
    args.Length == 4 ? new DocumentationRequest(args[0], args[1], args[2], args[3]) : null, token));
