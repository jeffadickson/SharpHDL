#!/usr/bin/env dotnet
#:property TargetFramework=net10.0
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

// Pinned independent oracle import, intentionally separate from hardware logic.
// Run from an existing repository root: dotnet run tools/ImportZ80Vectors.cs.
const string revision = "ebe1875d48f374bcfd4b505d8eb8ee751568b5f7";
const string root = "https://raw.githubusercontent.com/SingleStepTests/z80/" + revision + "/";
string destination = Path.GetFullPath("examples/Z80");
if (!Directory.Exists(destination)) throw new DirectoryNotFoundException(destination);
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
string[] baseCodes = "00 01 02 03 04 05 06 07 08 09 0a 0b 0f 10 17 18 1f 20 22 27 28 2a 2f 30 31 32 37 38 3a 3f 34 35 36 40 46 70 76 7e 80 86 88 8e 90 96 98 9e a0 a6 a8 ae b0 b6 b8 be c0 c1 c2 c3 c4 c5 c6 c7 c8 c9 cc cd ce d6 d9 de e6 e9 eb ee f1 f5 f6 f9 fe".Split(' ');
string[] cbCodes = "00 06 08 0e 10 16 18 1e 20 26 28 2e 38 3e 40 46 78 7e 80 86 b8 be c0 c6 f8 fe".Split(' ');
var paths = baseCodes.Select(code => "v1/" + code + ".json")
    .Concat(cbCodes.Select(code => "v1/cb " + code + ".json")).ToArray();
var collected = new JsonArray(); var manifest = new JsonArray();
// Bounded parallel downloads, deterministic output order regardless of completion.
using var budget = new SemaphoreSlim(4);
var tasks = paths.Select(async path =>
{
    await budget.WaitAsync();
    try
    {
        byte[] bytes = await http.GetByteArrayAsync(root + path.Replace(" ", "%20"));
        var all = JsonNode.Parse(bytes)!.AsArray();
        return (path, bytes, selected: all.Take(8).Select(node => node!.DeepClone()).ToArray());
    }
    finally { budget.Release(); }
}).ToArray();
foreach (var result in await Task.WhenAll(tasks))
{
    foreach (var item in result.selected) collected.Add(item);
    manifest.Add((JsonNode)new JsonObject { ["path"] = result.path, ["sha256"] = Convert.ToHexString(SHA256.HashData(result.bytes)), ["selected"] = result.selected.Length });
}
var options = new JsonSerializerOptions { WriteIndented = true };
await File.WriteAllTextAsync(Path.Combine(destination, "vectors.json"), collected.ToJsonString(options));
await File.WriteAllTextAsync(Path.Combine(destination, "vectors-provenance.json"), new JsonObject
{
    ["repository"] = "https://github.com/SingleStepTests/z80", ["revision"] = revision,
    ["selection"] = "First eight records per listed file; upstream state/RAM oracle, not T-state replay.", ["files"] = manifest
}.ToJsonString(options));
await File.WriteAllTextAsync(Path.Combine(destination, "SingleStepTests.LICENSE"), await http.GetStringAsync(root + "LICENSE"));
Console.WriteLine($"Imported {collected.Count} vectors from {paths.Length} pinned files.");
