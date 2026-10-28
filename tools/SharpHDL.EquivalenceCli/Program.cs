using System.Numerics;
using System.Text.Json;
using System.Text;
using SharpHDL.Core;
using SharpHDL.RTL;
using SharpHDL.Tooling;
using SharpHDL.Verification;

bool solverOnly=args.Length==2&&args[1]=="--solver-only";
if(args.Length!=1&&!solverOnly)throw new ArgumentException("Usage: SharpHDL.EquivalenceCli <new-evidence-directory> [--solver-only]");
string root=Path.GetFullPath(args[0]);
if(!Directory.Exists(Path.GetDirectoryName(root)))throw new DirectoryNotFoundException();
if(Path.Exists(root))throw new IOException("Evidence already exists.");Directory.CreateDirectory(root);
string? yosys=solverOnly?null:HdlToolRunner.FindExecutable("yosys")??throw new FileNotFoundException("Yosys is required for this qualification; --solver-only explicitly omits that independent check.");
for(int i=0;i<BitVectorCases.Names.Count;i++)foreach(bool corrupted in new[]{false,true})
{
    var sample=BitVectorCases.Build(i,corrupted);string name=sample.Name+(corrupted?"_different":"_equivalent");
    string directory=Path.Combine(root,name);Directory.CreateDirectory(directory);
    var result=BitVectorEquivalence.Check(sample.Left,sample.LeftOutput,sample.Right,sample.RightOutput);
    File.WriteAllText(Path.Combine(directory,"solver.smt2"),result.SmtLib);
    File.WriteAllText(Path.Combine(directory,"solver.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
    if(result.Status!=(corrupted?EquivalenceStatus.Different:EquivalenceStatus.Equivalent))throw new InvalidOperationException(name+": "+result.Status+" "+result.Reason);
    if(solverOnly){Console.WriteLine($"PASS {name}: Z3; Yosys explicitly omitted.");continue;}
    string Emit(HardwareGraph graph,int output,string module)=>CoreVerilogEmitter.Emit(graph,
        graph.Nodes.Where(n=>n.Operation==HardwareOperation.Input).ToDictionary(n=>n.Name!,n=>n.Id),new Dictionary<string,int>{{"result",output}},
        new Dictionary<string,int>(),new Dictionary<string,BigInteger>(),new Dictionary<string,HardwareMemoryPort>(),module);
    int width=sample.Left.Nodes.Single(n=>n.Id==sample.LeftOutput).Type.Width;
    string verilog=Emit(sample.Left,sample.LeftOutput,"left")+Emit(sample.Right,sample.RightOutput,"right")+
        $"\nmodule miter(input [1:0] x,y,output mismatch); wire [{width-1}:0] l,r;\n"+
        "h_left L(.clk(1'b0),.rst(1'b0),.s_x(x),.s_y(y),.s_result(l));\n"+
        "h_right R(.clk(1'b0),.rst(1'b0),.s_x(x),.s_y(y),.s_result(r)); assign mismatch=(l!=r); endmodule\n";
    File.WriteAllText(Path.Combine(directory,"design.v"),verilog);
    var run=await HdlToolRunner.RunAsync(yosys!,["-Q","-T","-p","read_verilog design.v; hierarchy -check -top miter; proc; flatten; opt; sat -prove mismatch 0 -show-inputs -show-outputs -dump_json counterexample.json"],
        directory,Path.Combine(directory,"yosys"),TimeSpan.FromSeconds(30));
    string log=File.ReadAllText(Path.Combine(directory,"yosys","stdout.bin"));
    if(run.Status!=ToolRunStatus.Success||!log.Contains(corrupted?"model found: FAIL":"no model found: SUCCESS",StringComparison.Ordinal))
        throw new InvalidOperationException(name+": Yosys did not establish the expected outcome; inspect retained logs.");
    if(corrupted)
    {
        // Every corrupt reference differs at exactly x=3,y=1; verify the independent
        // solver's concrete model, not merely an exit code or a banner substring.
        if(!File.Exists(Path.Combine(directory,"counterexample.json")))throw new InvalidOperationException("Missing Yosys counterexample.");
        using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"counterexample.json")));
        var signals=json.RootElement.GetProperty("signal").EnumerateArray().ToDictionary(s=>s.GetProperty("name").GetString()!.TrimStart('\\'),s=>s);
        foreach(var pair in new[]{("x","3"),("y","1")})
        {
            string value=signals[pair.Item1].GetProperty("data")[0].GetString()!;
            if(value.Length is <1 or >2||value.Any(c=>c is not ('0' or '1'))||Convert.ToInt32(value,2)!=int.Parse(pair.Item2))
                throw new InvalidOperationException($"Unexpected Yosys binary valuation {pair.Item1}={value}.");
        }
        if(result.Counterexample["x"]!="3"||result.Counterexample["y"]!="1")throw new InvalidOperationException("Z3 returned an unexpected corruption witness.");
    }
    Console.WriteLine($"PASS {name}: Z3 and Yosys agree.");
}
Console.WriteLine($"PASS {BitVectorCases.Names.Count*2} {(solverOnly?"Z3-only":"independent solver/RTL")} checks; evidence {root}");
