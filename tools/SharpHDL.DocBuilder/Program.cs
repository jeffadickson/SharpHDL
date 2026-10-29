using System.Text;
using System.Text.Json;
using SharpHDL.Documentation;
using SharpHDL.Tooling;

if(args.Length!=4)throw new ArgumentException("Usage: SharpHDL.DocBuilder <repository> <new-output-directory> <sphinx-python> <strictdoc-python>");
string repo=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);
if(!Directory.Exists(repo)||!Directory.Exists(Path.GetDirectoryName(output)))throw new DirectoryNotFoundException();
if(output.StartsWith(repo.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))
    throw new ArgumentException("Documentation output must be outside the repository: raw project copies conflict with recursive project discovery.");
if(Path.Exists(output))throw new IOException("Documentation output must be new.");Directory.CreateDirectory(output);
void Ensure(string directory)
{
    if(Directory.Exists(directory))return;string parent=Path.GetDirectoryName(directory)??throw new DirectoryNotFoundException();
    if(!Directory.Exists(parent))Ensure(parent);Directory.CreateDirectory(directory);
}
string runs=Path.Combine(output,"runs");Ensure(runs);int sequence=0;
async Task<string> Run(string executable,string[] arguments)
{
    string selected=Path.IsPathFullyQualified(executable)?executable:HdlToolRunner.FindExecutable(executable)??throw new FileNotFoundException(executable);
    string directory=Path.Combine(runs,(++sequence).ToString("D3"));
    var result=await HdlToolRunner.RunAsync(selected,arguments,repo,directory,TimeSpan.FromMinutes(10),maximumOutputBytes:8_388_608);
    if(result.Status!=ToolRunStatus.Success)throw new InvalidOperationException($"Documentation step {sequence} failed: {result.Status}. Inspect {directory}.");
    return await File.ReadAllTextAsync(Path.Combine(directory,"stdout.bin"));
}
string[] tracked=(await Run("git",["ls-files","-z"])).Split('\0',StringSplitOptions.RemoveEmptyEntries);
string[] specs=tracked.Where(p=>p.EndsWith(".sdoc",StringComparison.Ordinal)).ToArray();
if(specs.Length==0)throw new InvalidDataException("No tracked StrictDoc sources.");
foreach(string spec in specs)DocumentationCatalog.RepositoryFile(repo,spec);
string requirements=Path.Combine(output,"requirements");
await Run(args[3],new[]{"-c","from strictdoc.cli.main import main; main()","export"}.Concat(specs).Concat(new[]{"--formats","html,json","--no-parallelization","--output-dir",requirements}).ToArray());
var catalog=DocumentationCatalog.ReadRequirements(await File.ReadAllTextAsync(Path.Combine(requirements,"json","index.json")));
var trace=DocumentationCatalog.ReadTrace(repo,await File.ReadAllTextAsync(DocumentationCatalog.RepositoryFile(repo,"docs/requirements-trace.json")),catalog);
string source=Path.Combine(output,"source"),raw=Path.Combine(output,"raw"),api=Path.Combine(source,"api");Ensure(api);Ensure(raw);Ensure(Path.Combine(source,"docs"));
// Package tracked source references in the same repository-relative layout. This
// keeps canonical Markdown content intact and excludes private/untracked files.
foreach(string relative in tracked.Where(p=>p.StartsWith("docs/",StringComparison.Ordinal)||p.StartsWith("src/",StringComparison.Ordinal)||
    p.StartsWith("examples/",StringComparison.Ordinal)||p.StartsWith("tests/",StringComparison.Ordinal)||p.StartsWith("tools/",StringComparison.Ordinal)||p=="LICENSE"||p=="README.md"))
{
    string original=DocumentationCatalog.RepositoryFile(repo,relative);
    if(new FileInfo(original).Length>20_971_520)throw new InvalidDataException("Raw documentation reference exceeds 20 MiB: "+relative);
    string target=Path.Combine(raw,relative);Ensure(Path.GetDirectoryName(target)!);File.Copy(original,target);
    if(new[]{".png",".svg",".jpg",".jpeg",".gif"}.Contains(Path.GetExtension(relative),StringComparer.OrdinalIgnoreCase))
    {string image=Path.Combine(source,relative);Ensure(Path.GetDirectoryName(image)!);File.Copy(original,image);}
}
string[] pages=["MODULAR_ARCHITECTURE.md","DESIGN.md","BUILD_AND_SETUP.md","INSTRUCTION_CONTROL_FLOW.md","WORK_PACKAGES_2026-10-27.md","WORK_PACKAGES_4_6.md","WORK_PACKAGES_7_10.md","ENGINEERING_BUILD.md","SIMULATOR_BASELINES_2026-10-27.md","CONTINUATION_JIT_NARROW_2026-10-27.md","CONTINUATION_STORAGE_CHECKED_2026-10-27.md","CONTINUATION_TYPED_ALIASES_2026-10-28.md"];
foreach(string page in pages)File.Copy(DocumentationCatalog.RepositoryFile(repo,"docs/"+page),Path.Combine(source,"docs",page));
string[] assemblies=["SharpHDL.Core","SharpHDL.Simulation","SharpHDL.HLS","SharpHDL.RTL","SharpHDL","SharpHDL.Tooling","SharpHDL.Verification"];
foreach(string assembly in assemblies)
{
    string project=DocumentationCatalog.RepositoryFile(repo,$"src/{assembly}/{assembly}.csproj");
    await Run("dotnet",["build",project,"-c","Release","-m:1","-nr:false","-p:UseSharedCompilation=false","-p:GenerateDocumentationFile=true","-p:NoWarn=1591","--artifacts-path",Path.Combine(output,"managed")]);
    string xml=Path.Combine(output,"managed","bin",assembly,"release",assembly+".xml");
    await File.WriteAllTextAsync(Path.Combine(api,assembly+".rst"),DocumentationCatalog.RenderApi(await File.ReadAllTextAsync(xml)));
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
string site=Path.Combine(output,"site");await Run(args[2],["-m","sphinx","-W","-n","--keep-going","-b","html",source,site]);
void CopyTree(string from,string to)
{
    if(!Directory.Exists(from))throw new DirectoryNotFoundException(from);Ensure(to);
    foreach(string file in Directory.GetFiles(from))File.Copy(file,Path.Combine(to,Path.GetFileName(file)));
    foreach(string directory in Directory.GetDirectories(from))CopyTree(directory,Path.Combine(to,Path.GetFileName(directory)));
}
CopyTree(requirements,Path.Combine(site,"requirements"));
await using(var file=File.Create(Path.Combine(output,"documentation-summary.json")))
using(var writer=new Utf8JsonWriter(file,new JsonWriterOptions{Indented=true}))
{
    writer.WriteStartObject();writer.WriteString("createdUtc",DateTime.UtcNow);writer.WriteNumber("requirements",catalog.Count);writer.WriteNumber("mapped",trace.Count);
    writer.WriteNumber("unmapped",catalog.Count-trace.Count);writer.WriteNumber("apiAssemblies",assemblies.Length);writer.WriteNumber("engineeringPages",pages.Length);
    writer.WriteString("statusPolicy","Manual bounded statuses preserved; link validation is not requirement closure.");writer.WriteEndObject();
}
Console.WriteLine($"PASS engineering/API build: {catalog.Count} requirements, {trace.Count} mapped; {assemblies.Length} API assemblies. Site: {site}");
