using System;
using System.IO;
using SharpHDL;

// Generate an actual scheduled CIL pipeline, synthesizable RTL and an exact
// Portable-PDB -> scheduled RTL map. No hand-authored source mapping JSON.
var root=Path.GetFullPath(args.Length>0?args[0]:Directory.GetCurrentDirectory());
var module=ScheduledPipeline.Build(out var schedule);
var target=Path.Combine(root,"generated/vscode/ScheduledPolynomial1.v");
var json=HardwareSourceProvenance.Write(module,target,root);
Console.WriteLine("Generated "+target);
Console.WriteLine("Generated "+target+".source-map.json");
Console.WriteLine("Mapped "+System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("mappings").GetArrayLength()+" source/RTL operations.");
