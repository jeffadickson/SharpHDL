using System.Text.Json;
using SharpHDL.Tooling;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Terminal result publication against an actual writable evidence directory.</summary>
public sealed class BuildCampaignTests
{
    [Theory]
    [InlineData(CampaignStatus.Success, false, "complete", "campaign-complete.json")]
    [InlineData(CampaignStatus.Failed, false, "build", "campaign-failure.json")]
    [InlineData(CampaignStatus.Cancelled, true, "publish-completion", "campaign-failure.json")]
    public async Task Terminal_record_matches_command_outcome(CampaignStatus status, bool cancelBeforePublication,
        string expectedStage, string record)
    {
        string directory = NewEvidencePath();
        using var cancellation = new CancellationTokenSource();
        var command = new BuildCampaign("test.build", cancellation.Token);
        var result = await command.RunAsync(campaign =>
        {
            Assert.Null(campaign.Begin(directory));
            campaign.EnterStage("build", directory);
            File.WriteAllText(Path.Combine(directory, "stage.log"), "Stage evidence.");
            if (cancelBeforePublication) cancellation.Cancel();
            return Task.FromResult(campaign.Result(cancelBeforePublication ? CampaignStatus.Success : status, "Stage outcome."));
        });
        Assert.Equal(status, result.Status);
        Assert.Equal(expectedStage, result.Stage);
        Assert.Equal(directory, result.EvidenceDirectory);
        Assert.Equal("Stage evidence.", await File.ReadAllTextAsync(Path.Combine(directory, "stage.log")));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, record)));
        Assert.Equal(status.ToString(), json.RootElement.GetProperty("Status").GetString());
        Assert.Equal(expectedStage, json.RootElement.GetProperty("Stage").GetString());
        Assert.False(File.Exists(Path.Combine(directory, status == CampaignStatus.Success ? "campaign-failure.json" : "campaign-complete.json")));
    }

    [Fact]
    public async Task Existing_evidence_is_left_untouched()
    {
        string directory = NewEvidencePath();
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "campaign-complete.json");
        await File.WriteAllTextAsync(file, "previous run");
        var command = new BuildCampaign("test.build", CancellationToken.None);
        var result = await command.RunAsync(campaign =>
            Task.FromResult(campaign.Begin(directory) ?? campaign.Result(CampaignStatus.Success, "Unexpected ownership.")));
        Assert.Equal(CampaignStatus.InvalidInput, result.Status);
        Assert.Equal("previous run", await File.ReadAllTextAsync(file));
        Assert.False(File.Exists(Path.Combine(directory, "campaign-failure.json")));
    }

    [Fact]
    public async Task Publication_conflict_reports_failure_and_preserves_original_file()
    {
        string directory = NewEvidencePath();
        string file = Path.Combine(directory, "campaign-complete.json");
        var command = new BuildCampaign("test.build", CancellationToken.None);
        var result = await command.RunAsync(campaign =>
        {
            Assert.Null(campaign.Begin(directory));
            File.WriteAllText(file, "external file");
            return Task.FromResult(campaign.Result(CampaignStatus.Success, "Stages finished."));
        });
        Assert.Equal(CampaignStatus.Failed, result.Status);
        Assert.Equal("publish-completion", result.Stage);
        Assert.Equal("external file", await File.ReadAllTextAsync(file));
        Assert.True(File.Exists(Path.Combine(directory, "campaign-failure.json")));
    }

    private static string NewEvidencePath()
    {
        string parent = Path.GetTempPath();
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
        return Path.Combine(parent, "sharphdl-campaign-" + Guid.NewGuid().ToString("N"));
    }
}
