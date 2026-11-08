using System.Text;
using System.Text.Json;
using SharpHDL.Documentation;
using SharpHDL.Tooling;

return await BuildCampaign.RunConsoleAsync("sharphdl.documentation.build", campaign => BuildAsync(args, campaign));

static async Task<CampaignResult> BuildAsync(string[] args, BuildCampaign campaign)
{
    if(args.Length!=4)return campaign.Result(CampaignStatus.InvalidInput,"Usage: SharpHDL.DocBuilder <repository> <new-output-directory> <sphinx-python> <strictdoc-python>");
    string repo=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);
    if(!Directory.Exists(repo))return campaign.Result(CampaignStatus.InvalidInput,"The repository directory must exist.");
    if(output.StartsWith(repo.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))
        return campaign.Result(CampaignStatus.InvalidInput,"Documentation output must be outside the repository: raw project copies conflict with recursive project discovery.");
    string? git=BuildCampaign.ResolveExecutable("git"),dotnet=BuildCampaign.ResolveExecutable("dotnet"),
        sphinx=BuildCampaign.ResolveExecutable(args[2]),strictDoc=BuildCampaign.ResolveExecutable(args[3]);
    if(git is null||dotnet is null||sphinx is null||strictDoc is null)
        return campaign.Result(CampaignStatus.MissingDependency,
            $"Could not resolve { (git is null ? "git" : dotnet is null ? "dotnet" : sphinx is null ? args[2] : args[3]) }.");
    if(campaign.Begin(output) is { } rejected)return rejected;
    void Ensure(string directory)
    {
        if(Directory.Exists(directory))return;string parent=Path.GetDirectoryName(directory)??throw new DirectoryNotFoundException();
        if(!Directory.Exists(parent))Ensure(parent);Directory.CreateDirectory(directory);
    }
    string runs=Path.Combine(output,"runs");Ensure(runs);int sequence=0;
    async Task<(ToolRunManifest Manifest,string Directory)> Run(string stage,string executable,string[] arguments)
    {
        string directory=Path.Combine(runs,(++sequence).ToString("D3"));
        campaign.EnterStage(stage,directory);
        var result=await HdlToolRunner.RunAsync(executable,arguments,repo,directory,TimeSpan.FromMinutes(10),campaign.Token,maximumOutputBytes:8_388_608);
        return(result,directory);
    }
    var inventory=await Run("inventory",git,["ls-files","-z"]);
    if(inventory.Manifest.Status!=ToolRunStatus.Success)return campaign.ToolFailure(inventory.Manifest);
    string[] tracked=(await File.ReadAllTextAsync(Path.Combine(inventory.Directory,"stdout.bin"),campaign.Token)).Split('\0',StringSplitOptions.RemoveEmptyEntries);
    string[] specs=tracked.Where(p=>p.EndsWith(".sdoc",StringComparison.Ordinal)).ToArray();
    if(specs.Length==0)return campaign.Result(CampaignStatus.Failed,"No tracked StrictDoc sources.","MissingRequirements");
    foreach(string spec in specs)DocumentationCatalog.RepositoryFile(repo,spec);
    string requirements=Path.Combine(output,"requirements");
    var export=await Run("requirements-export",strictDoc,new[]{"-c","from strictdoc.cli.main import main; main()","export"}.Concat(specs).Concat(new[]{"--formats","html,json","--no-parallelization","--output-dir",requirements}).ToArray());
    if(export.Manifest.Status!=ToolRunStatus.Success)return campaign.ToolFailure(export.Manifest);
    campaign.EnterStage("requirements-trace",requirements);
    var catalog=DocumentationCatalog.ReadRequirements(await File.ReadAllTextAsync(Path.Combine(requirements,"json","index.json"),campaign.Token));
    var trace=DocumentationCatalog.ReadTrace(repo,await File.ReadAllTextAsync(DocumentationCatalog.RepositoryFile(repo,"docs/requirements-trace.json"),campaign.Token),catalog);
    string source=Path.Combine(output,"source"),raw=Path.Combine(output,"raw"),api=Path.Combine(source,"api");Ensure(api);Ensure(raw);Ensure(Path.Combine(source,"docs"));
    campaign.EnterStage("package-sources",raw);
    // Package tracked source references in the same repository-relative layout. This
    // keeps canonical Markdown content intact and excludes private/untracked files.
    foreach(string relative in tracked.Where(p=>p.StartsWith("docs/",StringComparison.Ordinal)||p.StartsWith("src/",StringComparison.Ordinal)||
        p.StartsWith("examples/",StringComparison.Ordinal)||p.StartsWith("tests/",StringComparison.Ordinal)||p.StartsWith("tools/",StringComparison.Ordinal)||p=="LICENSE"||p=="README.md"))
    {
        campaign.Token.ThrowIfCancellationRequested();
        string original=DocumentationCatalog.RepositoryFile(repo,relative);
        if(new FileInfo(original).Length>20_971_520)throw new InvalidDataException("Raw documentation reference exceeds 20 MiB: "+relative);
        string target=Path.Combine(raw,relative);Ensure(Path.GetDirectoryName(target)!);File.Copy(original,target);
        if(new[]{".png",".svg",".jpg",".jpeg",".gif"}.Contains(Path.GetExtension(relative),StringComparer.OrdinalIgnoreCase))
        {string image=Path.Combine(source,relative);Ensure(Path.GetDirectoryName(image)!);File.Copy(original,image);}
    }
    string[] pages=["MODULAR_ARCHITECTURE.md","DESIGN.md","BUILD_AND_SETUP.md","INSTRUCTION_CONTROL_FLOW.md","WORK_PACKAGES_2026-10-27.md","WORK_PACKAGES_4_6.md","WORK_PACKAGES_7_10.md","ENGINEERING_BUILD.md","SIMULATOR_BASELINES_2026-10-27.md","CONTINUATION_JIT_NARROW_2026-10-27.md","CONTINUATION_STORAGE_CHECKED_2026-10-27.md","CONTINUATION_TYPED_ALIASES_2026-10-28.md","CONTINUATION_REFINED_RAM_2026-10-28.md","FIVE_PACKAGE_COMPLETION_2026-10-28.md","RAILWAY_BOUNDARIES.md"];
    foreach(string page in pages)
    {
        campaign.Token.ThrowIfCancellationRequested();
        File.Copy(DocumentationCatalog.RepositoryFile(repo,"docs/"+page),Path.Combine(source,"docs",page));
    }
    string[] assemblies=["SharpHDL.Core","SharpHDL.Simulation","SharpHDL.HLS","SharpHDL.RTL","SharpHDL","SharpHDL.Tooling","SharpHDL.Verification"];
    foreach(string assembly in assemblies)
    {
        string project=DocumentationCatalog.RepositoryFile(repo,$"src/{assembly}/{assembly}.csproj");
        var compile=await Run("compile-api."+assembly,dotnet,["build",project,"-c","Release","-m:1","-nr:false","-p:UseSharedCompilation=false","-p:GenerateDocumentationFile=true","-p:NoWarn=1591","--artifacts-path",Path.Combine(output,"managed")]);
        if(compile.Manifest.Status!=ToolRunStatus.Success)return campaign.ToolFailure(compile.Manifest);
        string xml=Path.Combine(output,"managed","bin",assembly,"release",assembly+".xml");
        await File.WriteAllTextAsync(Path.Combine(api,assembly+".rst"),DocumentationCatalog.RenderApi(await File.ReadAllTextAsync(xml,campaign.Token)),campaign.Token);
    }
    await File.WriteAllTextAsync(Path.Combine(api,"index.rst"),"Compiler XML API reference\n==========================\n\nOnly compiler-emitted documented members are shown. Legacy missing comments remain a limitation.\n\n.. toctree::\n   :maxdepth: 1\n\n"+string.Join("\n",assemblies.Select(a=>"   "+a))+"\n");
    await File.WriteAllTextAsync(Path.Combine(source,"traceability.rst"),DocumentationCatalog.RenderTrace(catalog,trace));
    string index="SharpHDL engineering reference\n==============================\n\nThis curated reference builds existing engineering pages, compiler XML documentation and real StrictDoc requirements. Raw repository-relative source links are packaged alongside the rendered pages. Mermaid diagrams use the pinned browser CDN; offline diagram rendering is not qualified.\n\n"+
        "`StrictDoc requirements <requirements/html/index.html>`_\n\n.. toctree::\n   :maxdepth: 2\n\n"+string.Join("\n",pages.Select(p=>"   docs/"+Path.GetFileNameWithoutExtension(p)))+"\n   api/index\n   traceability\n";
    // Requirements are copied into the final site after Sphinx; keep its rendered
    // HTML and search assets unchanged rather than reconstructing its output.
    await File.WriteAllTextAsync(Path.Combine(source,"index.rst"),index);
    string quoted;using(var memory=new MemoryStream()){using(var writer=new Utf8JsonWriter(memory)){writer.WriteStringValue(raw);}quoted=Encoding.UTF8.GetString(memory.ToArray());}
    File.Copy(DocumentationCatalog.RepositoryFile(repo,"docs/engineering/conf.py"),Path.Combine(source,"conf.py"));
    await File.AppendAllTextAsync(Path.Combine(source,"conf.py"),"\nhtml_extra_path = ["+quoted+"]\n");
    string site=Path.Combine(output,"site");
    var render=await Run("render-site",sphinx,["-m","sphinx","-W","-n","--keep-going","-b","html",source,site]);
    if(render.Manifest.Status!=ToolRunStatus.Success)return campaign.ToolFailure(render.Manifest);
    void CopyTree(string from,string to)
    {
        campaign.Token.ThrowIfCancellationRequested();
        if(!Directory.Exists(from))throw new DirectoryNotFoundException(from);Ensure(to);
        foreach(string file in Directory.GetFiles(from))
        {
            campaign.Token.ThrowIfCancellationRequested();
            File.Copy(file,Path.Combine(to,Path.GetFileName(file)));
        }
        foreach(string directory in Directory.GetDirectories(from))CopyTree(directory,Path.Combine(to,Path.GetFileName(directory)));
    }
    campaign.EnterStage("package-site",site);
    CopyTree(requirements,Path.Combine(site,"requirements"));
    await using(var file=File.Create(Path.Combine(output,"documentation-summary.json")))
    using(var writer=new Utf8JsonWriter(file,new JsonWriterOptions{Indented=true}))
    {
        writer.WriteStartObject();writer.WriteString("createdUtc",DateTime.UtcNow);writer.WriteNumber("requirements",catalog.Count);writer.WriteNumber("mapped",trace.Count);
        writer.WriteNumber("unmapped",catalog.Count-trace.Count);writer.WriteNumber("apiAssemblies",assemblies.Length);writer.WriteNumber("engineeringPages",pages.Length);
        writer.WriteString("statusPolicy","Manual bounded statuses preserved; link validation is not requirement closure.");writer.WriteEndObject();
    }
    return campaign.Result(CampaignStatus.Success,
        $"Engineering/API build completed with {catalog.Count} requirements, {trace.Count} mapped and {assemblies.Length} API assemblies. Site: {site}");
}
