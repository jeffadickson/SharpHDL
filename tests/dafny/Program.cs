using CampaignExit;
using SharpHDL.Tooling;

if (args.Length != 1 || !Directory.Exists(args[0]))
    throw new ArgumentException("Expected an existing proof evidence directory.");

// Exercise the production console boundary, including real publication for success.
var cases = new (CampaignStatus Actual, _IStatus Model)[]
{
    (CampaignStatus.Success, Status.create_Success()),
    (CampaignStatus.InvalidInput, Status.create_InvalidInput()),
    (CampaignStatus.MissingDependency, Status.create_MissingDependency()),
    (CampaignStatus.Failed, Status.create_Failed()),
    (CampaignStatus.Cancelled, Status.create_Cancelled()),
    (CampaignStatus.UnexpectedFailure, Status.create_UnexpectedFailure()),
    ((CampaignStatus)int.MaxValue, Status.create_Unknown())
};
if (!cases.Select(item => item.Actual).SkipLast(1).SequenceEqual(Enum.GetValues<CampaignStatus>()))
    throw new InvalidOperationException("Update the model and check for the current terminal outcomes.");

foreach (var (actual, model) in cases)
{
    int observed = await BuildCampaign.RunConsoleAsync("dafny-exit-check", campaign =>
        Task.FromResult((actual == CampaignStatus.Success
            ? campaign.Begin(Path.Combine(args[0], "success"))
            : null) ?? campaign.Result(actual, "Exit-code conformance check.")));
    int expected = (int)__default.ExitCode(model);
    if (observed != expected)
        throw new InvalidOperationException($"Status {actual}: expected {expected}, got {observed}.");
}
Console.WriteLine($"Generated C# agreed with the production console for {cases.Length} outcomes.");
