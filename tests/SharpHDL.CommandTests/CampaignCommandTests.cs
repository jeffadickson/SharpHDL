using SharpHDL.Documentation;
using SharpHDL.Qualification;
using SharpHDL.Tooling;
using Xunit;

namespace SharpHDL.CommandTests;

public sealed class CampaignCommandTests
{
    [Fact]
    public async Task Typed_campaigns_validate_before_creating_output()
    {
        var docs = await DocumentationCommand.Command.InvokeAsync(null!, CancellationToken.None);
        Assert.Equal(CampaignStatus.InvalidInput, docs.Status);
        var qualification = await EquivalenceCommand.Command.InvokeAsync(null!, CancellationToken.None);
        Assert.Equal(CampaignStatus.InvalidInput, qualification.Status);
        Assert.Equal("sharphdl.documentation.build", docs.Operation);
        Assert.Equal("sharphdl.equivalence.qualify", qualification.Operation);
    }

    [Fact]
    public async Task Pre_cancelled_campaigns_cannot_publish_completion()
    {
        var token = new CancellationToken(true);
        var docs = await DocumentationCommand.Command.InvokeAsync(null!, token);
        var qualification = await EquivalenceCommand.Command.InvokeAsync(null!, token);
        Assert.Equal(CampaignStatus.Cancelled, docs.Status);
        Assert.Equal(CampaignStatus.Cancelled, qualification.Status);
        Assert.Null(docs.EvidenceDirectory);
        Assert.Null(qualification.EvidenceDirectory);
    }

    [Fact]
    public async Task Documentation_inputs_are_all_required_before_evidence_is_claimed()
    {
        string output = Path.Combine(Environment.GetEnvironmentVariable("CODEX_ARTIFACT_ROOT") ??
            Path.GetTempPath(), "sharphdl-invalid-doc-" + Guid.NewGuid().ToString("N"));
        var valid = new DocumentationRequest(AppContext.BaseDirectory, output, "python", "python");
        foreach (var request in new[]
        {
            valid with { Repository = null! }, valid with { Repository = "" },
            valid with { OutputDirectory = null! }, valid with { OutputDirectory = " " },
            valid with { SphinxPython = null! }, valid with { SphinxPython = "" },
            valid with { StrictDocPython = null! }, valid with { StrictDocPython = "" },
            valid with { Repository = "\0" }
        })
        {
            var result = await DocumentationCommand.Command.InvokeAsync(request, CancellationToken.None);
            Assert.Equal(CampaignStatus.InvalidInput, result.Status);
            Assert.False(string.IsNullOrWhiteSpace(result.Diagnostic));
            Assert.Null(result.EvidenceDirectory);
        }
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public async Task Valid_documentation_input_reaches_dependency_validation()
    {
        string output = Path.Combine(Environment.GetEnvironmentVariable("CODEX_ARTIFACT_ROOT") ??
            Path.GetTempPath(), "sharphdl-doc-dependency-" + Guid.NewGuid().ToString("N"));
        string missing = "SharpHDL_absent_python_" + Guid.NewGuid().ToString("N");
        var request = new DocumentationRequest(AppContext.BaseDirectory, output, missing, missing);
        var result = await DocumentationCommand.Command.InvokeAsync(request, CancellationToken.None);
        Assert.Equal(CampaignStatus.MissingDependency, result.Status);
        Assert.Contains(missing, result.Diagnostic);
        Assert.Null(result.EvidenceDirectory);
        Assert.False(Directory.Exists(output));
    }
}
