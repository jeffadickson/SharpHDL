using SharpHDL.Examples;
using SharpHDL.Verification;

if (args.Length != 1) throw new ArgumentException("Usage: SharpHDL.Z80Demo <output-directory>");
string output = Path.GetFullPath(args[0]);
if (!Directory.Exists(output))
{
    string parent = Path.GetDirectoryName(output)!;
    if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
    Directory.CreateDirectory(output);
}
var design = Z80TestCore.Build();
var replay = new CoreReplay(design);
int vectors = Z80Campaign.Run(Path.Combine(AppContext.BaseDirectory, "Z80Vectors.json"), replay.Drive,
    name => (int)replay.Read(name), replay.Clock);
int programEdges = Z80Campaign.RunProgram(replay.Drive, name => (int)replay.Read(name), replay.Clock);
replay.Save(output);
Console.WriteLine($"Z80: {vectors} independent vectors; program {programEdges} edges; {design.Graph.Nodes.Count} Core nodes.");
