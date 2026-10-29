#:property PublishAot=false
// Reproduce with: dotnet run tools/SummarizeMutationEvidence.cs -- <report.json> <file-name-regex>
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

if(args.Length!=2||!File.Exists(args[0]))throw new ArgumentException("Expected an existing Stryker JSON report and a file-name regex.");
byte[] bytes=File.ReadAllBytes(args[0]);using var document=JsonDocument.Parse(bytes);
var files=new List<object>();
foreach(var file in document.RootElement.GetProperty("files").EnumerateObject())
{
    string name=file.Name.Replace('\\','/').Split('/')[^1];if(!Regex.IsMatch(name,args[1]))continue;
    var mutants=file.Value.GetProperty("mutants").EnumerateArray().ToArray();
    var counts=mutants.GroupBy(m=>m.GetProperty("status").GetString()!).ToDictionary(g=>g.Key,g=>g.Count());
    string source=file.Value.GetProperty("source").GetString()!;
    var remaining=mutants.Where(m=>m.GetProperty("status").GetString() is "Survived" or "Timeout" or "NoCoverage")
        .Select(m=>new{id=m.GetProperty("id").ToString(),status=m.GetProperty("status").GetString(),
            location=m.GetProperty("location").Clone(),replacement=m.GetProperty("replacement").GetString()}).ToArray();
    files.Add(new{file=name,sourceSha256=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))),counts,remaining});
}
if(files.Count==0)throw new InvalidDataException("No source file matched the requested scope.");
Console.WriteLine(JsonSerializer.Serialize(new{schema=1,tool="Stryker.NET 5.0.0",rawPath=args[0],
    rawSha256=Convert.ToHexString(SHA256.HashData(bytes)),scope=args[1],
    policy="Separate executions; never combine scores. File-wide CompileError/Ignored counts are not executed tests. Timeout is distinct from an ordinary kill. Survivor dispositions require explicit review.",files},
    new JsonSerializerOptions{WriteIndented=true,TypeInfoResolver=new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()}));
