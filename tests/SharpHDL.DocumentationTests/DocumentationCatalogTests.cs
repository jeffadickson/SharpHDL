using System.Text.Json;
using System.Xml;
using SharpHDL.Documentation;
using SharpHDL.Tooling;
using Xunit;

namespace SharpHDL.DocumentationTests;

/// <summary>Negative parser/path tests and explicit preservation of incomplete traceability.</summary>
public sealed class DocumentationCatalogTests
{
    private const string Requirements="""{"DOCUMENTS":[{"_NODE_TYPE":"DOCUMENT","GRAMMAR":{"NODE_TYPE":"REQUIREMENT","UID":"not-a-requirement"},"CONTENT":[{"_NODE_TYPE":"REQUIREMENT","UID":"REQ-001","TITLE":"First"},{"_NODE_TYPE":"REQUIREMENT","UID":"REQ-002","TITLE":"Second"}]}]}""";
    /// <summary>Only authoritative requirement nodes count, not grammar or section metadata.</summary>
    [Fact]
    public void Authoritative_requirements_and_unmapped_status_are_preserved()
    {
        var entries=DocumentationCatalog.ReadRequirements(Requirements);Assert.Equal(new[]{"REQ-001","REQ-002"},entries.Select(e=>e.Uid));
        Assert.Equal(new[]{"First","Second"},entries.Select(e=>e.Title));
        Assert.Equal("R",Assert.Single(DocumentationCatalog.ReadRequirements("""[{"_NODE_TYPE":0},{"_NODE_TYPE":"REQUIREMENT","UID":"R"}]""")).Title);
        string page=DocumentationCatalog.RenderTrace(entries,[]);Assert.Contains("unmapped",page);Assert.DoesNotContain("verified_bounded",page);
        Assert.Throws<InvalidDataException>(()=>DocumentationCatalog.ReadRequirements("{}"));
        string Budget(int count)=>JsonSerializer.Serialize(Enumerable.Range(0,count).Select(i=>new Dictionary<string,string>{{"_NODE_TYPE","REQUIREMENT"},{"UID","B-"+i}}));
        Assert.Equal(10000,DocumentationCatalog.ReadRequirements(Budget(10000)).Count);
        Assert.Throws<InvalidDataException>(()=>DocumentationCatalog.ReadRequirements(Budget(10001)));
        Assert.Throws<InvalidDataException>(()=>DocumentationCatalog.ReadRequirements("""[{"_NODE_TYPE":"REQUIREMENT","UID":"D"},{"_NODE_TYPE":"REQUIREMENT","UID":"D"}]"""));
        Assert.ThrowsAny<JsonException>(()=>DocumentationCatalog.ReadRequirements("[invalid"));
        foreach(string invalid in new[]{"""{"_NODE_TYPE":"REQUIREMENT"}""","""{"_NODE_TYPE":"REQUIREMENT","UID":3}""","""{"_NODE_TYPE":"REQUIREMENT","UID":"X","TITLE":false}"""})
            Assert.Throws<InvalidDataException>(()=>DocumentationCatalog.ReadRequirements(invalid));
    }
    /// <summary>DTD/entity expansion is rejected; compiler IDs and parameter references remain visible.</summary>
    [Fact]
    public void Xml_documentation_is_rendered_without_external_resolution()
    {
        string xml="""<doc><assembly><name>Example</name></assembly><members><member name="M:Example.Run``1(System.Int32)"><summary>Use &lt;value&gt; and <paramref name="count"/>; see <see cref="T:Example"/>.</summary><param name="count">A *literal* count.</param><returns>Result.</returns></member></members></doc>""";
        string page=DocumentationCatalog.RenderApi(xml);Assert.Contains("M:Example.Run``1",page);Assert.Contains("<value>",page);Assert.Contains("count",page);Assert.Contains("T:Example",page);
        Assert.Contains("\\*literal\\*",page);Assert.Contains("Returns:",page);Assert.Contains("not complete API coverage",page);
        Assert.Throws<XmlException>(()=>DocumentationCatalog.RenderApi("<!DOCTYPE doc [<!ENTITY x SYSTEM 'file:///unavailable'>]><doc>&x;</doc>"));
        Assert.Throws<InvalidDataException>(()=>DocumentationCatalog.RenderApi("<doc/>"));
    }
    /// <summary>Unknown UIDs/statuses, absent evidence and path escape cannot produce a verified ledger row.</summary>
    [Fact]
    public async Task Trace_references_and_statuses_fail_closed()
    {
        string parent=Path.GetTempPath();if(!Directory.Exists(parent))throw new DirectoryNotFoundException();
        string root=Path.Combine(parent,"sharphdl-docs-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root,"source.cs"),"public void Real_test() {}");
        string Ledger(string uid,string status,string file="source.cs",string method="Real_test",bool evidence=true)=>
            JsonSerializer.Serialize(new{schema=1,entries=new[]{new{uid,status,implementation=new[]{file},tests=new[]{new{file,method}},evidence=evidence?new[]{"source.cs"}:Array.Empty<string>(),limitations="Bounded fixture, not product closure."}}});
        string outside=Path.Combine(parent,"sharphdl-doc-outside-"+Guid.NewGuid().ToString("N")+".cs");
        File.WriteAllText(outside,"outside");
        Assert.Throws<InvalidDataException>(()=>DocumentationCatalog.RepositoryFile(root,"../"+Path.GetFileName(outside)));
        if(!OperatingSystem.IsWindows())
        {
            string link=Path.Combine(root,"link.cs");File.CreateSymbolicLink(link,outside);
            Assert.Throws<InvalidDataException>(()=>DocumentationCatalog.RepositoryFile(root,"link.cs"));
        }
        string caseName=Path.GetFileName(root).ToUpperInvariant();
        if(OperatingSystem.IsWindows())
        {
            // Windows path admission must preserve the filesystem's case-insensitive semantics.
            Assert.True(File.Exists(DocumentationCatalog.RepositoryFile(root,"../"+caseName+"/source.cs")));
            string outsideDirectory=Path.Combine(parent,"sharphdl-doc-target-"+Guid.NewGuid().ToString("N"));
            if(!Directory.Exists(outsideDirectory))Directory.CreateDirectory(outsideDirectory);
            File.WriteAllText(Path.Combine(outsideDirectory,"source.cs"),"outside junction target");
            string junction=Path.Combine(root,"junction");
            string Quote(string value)=>"'"+value.Replace("'","''",StringComparison.Ordinal)+"'";
            string executable=HdlToolRunner.FindExecutable("pwsh")??throw new FileNotFoundException("PowerShell is required for the Windows junction fixture.");
            var created=await HdlToolRunner.RunAsync(executable,["-NoProfile","-NonInteractive","-Command",
                "New-Item -ItemType Junction -Path "+Quote(junction)+" -Value "+Quote(outsideDirectory)+" -ErrorAction Stop | Out-Null"],
                root,Path.Combine(root,"junction-evidence"),TimeSpan.FromSeconds(15));
            Assert.Equal(ToolRunStatus.Success,created.Status);
            Assert.Throws<InvalidDataException>(()=>DocumentationCatalog.RepositoryFile(root,"junction/source.cs"));
        }
        else
        {
            // A case-distinct existing sibling must stay outside a Linux repository root.
            string sibling=Path.Combine(parent,caseName);
            if(!Directory.Exists(sibling))Directory.CreateDirectory(sibling);
            File.WriteAllText(Path.Combine(sibling,"source.cs"),"case-distinct outside target");
            Assert.Throws<InvalidDataException>(()=>DocumentationCatalog.RepositoryFile(root,"../"+caseName+"/source.cs"));
        }
        var requirements=DocumentationCatalog.ReadRequirements(Requirements);
        var trace=DocumentationCatalog.ReadTrace(root,Ledger("REQ-001","partial"),requirements);
        var bounded=DocumentationCatalog.ReadTrace(root,Ledger("REQ-001","verified_bounded"),requirements);Assert.Equal("verified_bounded",Assert.Single(bounded).Status);
        string page=DocumentationCatalog.RenderTrace(requirements,trace);Assert.Contains("partial",page);Assert.Contains("unmapped",page);Assert.Contains("<source.cs>`_",page);
        Assert.Throws<InvalidDataException>(()=>DocumentationCatalog.ReadTrace(root,Ledger("unknown","partial"),requirements));
        Assert.Throws<InvalidDataException>(()=>DocumentationCatalog.ReadTrace(root,Ledger("REQ-001","complete"),requirements));
        Assert.Throws<InvalidDataException>(()=>DocumentationCatalog.ReadTrace(root,Ledger("REQ-001","verified_bounded",evidence:false),requirements));
        Assert.Throws<InvalidDataException>(()=>DocumentationCatalog.ReadTrace(root,Ledger("REQ-001","partial",method:"Absent_test"),requirements));
        Assert.Throws<InvalidDataException>(()=>DocumentationCatalog.RepositoryFile(root,"../outside"));
        Assert.Throws<InvalidDataException>(()=>DocumentationCatalog.RepositoryFile(root,Path.GetFullPath(Path.Combine(root,"source.cs"))));
        Assert.Throws<InvalidDataException>(()=>DocumentationCatalog.RepositoryFile(root,"missing.cs"));
    }
    /// <summary>Generated identifiers cannot bypass authoritative UID admission through escaping or punctuation.</summary>
    [FsCheck.Xunit.Property(MaxTest=100,Replay="1145655947,296144285")]
    public void Arbitrary_trace_uids_must_exist_in_the_authoritative_export(string uid)
    {
        string json=JsonSerializer.Serialize(new[]{new Dictionary<string,string?>{{"_NODE_TYPE","REQUIREMENT"},{"UID",uid},{"TITLE","Generated"}}});
        if(string.IsNullOrWhiteSpace(uid))Assert.Throws<InvalidDataException>(()=>DocumentationCatalog.ReadRequirements(json));
        else Assert.Equal(uid,Assert.Single(DocumentationCatalog.ReadRequirements(json)).Uid);
    }
}
