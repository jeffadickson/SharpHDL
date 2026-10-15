using SharpHDL.RTL;

// Rebuild the same HelloWorld CPU used by the existing reference test.
// Emit RTL exclusively from Core IR, not Module.Verilog().
var root=Path.GetFullPath(args.Length>0?args[0]:Directory.GetCurrentDirectory());
var words=File.ReadAllLines(Path.Combine(root,"generated/tinyrisc/hello_world.hex"))
    .Where(line=>!string.IsNullOrWhiteSpace(line))
    .Select(line=>Convert.ToUInt16(line.Trim(),16)).ToArray();
var design=TinyRisc.Build(words,"HelloWorld");
var core=design.ExportCoreGraph();
var native=CoreVerilogEmitter.Emit(core.Graph,core.Inputs,core.Outputs,
    core.NextState,core.ResetValues,core.Memories,"HelloWorld");
var folder=Path.Combine(root,"generated/tinyrisc/native");
Directory.CreateDirectory(folder);
var file=Path.Combine(folder,"HelloWorld.v");
File.WriteAllText(file,native);
Console.WriteLine("Generated native Core RTL TinyRisc HelloWorld: "+
    core.Graph.Nodes.Count+" nodes, "+core.Memories.Count+" RAM(s), "+
    words.Length+" ROM words -> "+file);
