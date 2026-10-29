#:property PublishAot=false
// Reproduce with: dotnet run tools/SummarizeRefinedRamCoverage.cs -- <Cobertura XML>
// The caller retains the raw input; its digest ties this summary to exact evidence.
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

if (args.Length != 1 || !File.Exists(args[0]))
    throw new ArgumentException("Expected an existing Cobertura XML file.");
var path = Path.GetFullPath(args[0]);
var root = XDocument.Load(path).Root ?? throw new InvalidDataException("Missing coverage root.");
var files = root.Descendants("class")
    .Where(c => Regex.IsMatch((string?)c.Attribute("filename") ?? "",
        @"ScheduledCil(RefinedValidation|ReferenceFacts|RamBindings|StackValidation|Relations|SharedBindings)\.cs$|ScheduledSequentialCil\.cs$|MemoryTransitionEquivalence\.cs$"))
    .GroupBy(c => (string)c.Attribute("filename")!);
var results = files.Select(file =>
{
    // Lines are shared by compiler-generated closures and their declaring type.
    // Count each source line once. Branch counters remain class-entry counters,
    // matching Coverlet's distinct generated methods rather than averaging rates.
    var entries = file.SelectMany(c => c.Element("lines")?.Elements("line") ?? [] ).ToArray();
    var lines = entries.GroupBy(l => (int)l.Attribute("number")!).ToArray();
    int coveredBranches = 0, branches = 0;
    foreach (var entry in entries)
    {
        var match = Regex.Match((string?)entry.Attribute("condition-coverage") ?? "", @"\((\d+)/(\d+)\)");
        if (!match.Success) continue;
        coveredBranches += int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        branches += int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
    }
    return new
    {
        file = Normalize(file.Key), lines = lines.Length,
        coveredLines = lines.Count(g => g.Any(l => (int)l.Attribute("hits")! > 0)),
        branches, coveredBranches
    };
});
Console.WriteLine(JsonSerializer.Serialize(new
{
    schema = 1,
    inputSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
    overall = new { lineRate = (string?)root.Attribute("line-rate"), branchRate = (string?)root.Attribute("branch-rate") },
    scope = "Unique source lines; summed Cobertura class-entry branch counters, including generated methods. Overall is the collected production assembly set, not the whole repository.",
    files = results
}, new JsonSerializerOptions { WriteIndented = true, TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() }));

// Normalize only the repository prefix, keeping source-relative identities comparable across hosts.
static string Normalize(string value)
{
    string path=value.Replace('\\','/');int offset=path.IndexOf("/src/",StringComparison.Ordinal);
    return offset<0?path:path[(offset+1)..];
}
