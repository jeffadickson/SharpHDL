using System.Numerics;
using System.Text.Json;
using SharpHDL.Core;
using SharpHDL.RTL;
using SharpHDL.Tooling;
using SharpHDL.Verification;

return await BuildCampaign.RunConsoleAsync("sharphdl.equivalence.qualify", campaign => QualifyAsync(args, campaign));

static async Task<CampaignResult> QualifyAsync(string[] args, BuildCampaign campaign)
{
    bool solverOnly=args.Length==2&&args[1]=="--solver-only";
    if(args.Length!=1&&!solverOnly)return campaign.Result(CampaignStatus.InvalidInput,"Usage: SharpHDL.EquivalenceCli <new-evidence-directory> [--solver-only]");
    string root=Path.GetFullPath(args[0]);
    string? yosys=solverOnly?null:BuildCampaign.ResolveExecutable("yosys");
    if(!solverOnly&&yosys is null)
        return campaign.Result(CampaignStatus.MissingDependency,"Yosys is required for independent RTL qualification. Use --solver-only for the Z3 campaign.");
    if(campaign.Begin(root) is { } rejected)return rejected;
    for(int i=0;i<BitVectorCases.Names.Count;i++)foreach(bool corrupted in new[]{false,true})
    {
        var sample=BitVectorCases.Build(i,corrupted);string name=sample.Name+(corrupted?"_different":"_equivalent");
        string directory=Path.Combine(root,name);
        campaign.EnterStage("solve."+name,directory);
        Directory.CreateDirectory(directory);
        var result=BitVectorEquivalence.Check(sample.Left,sample.LeftOutput,sample.Right,sample.RightOutput,cancellationToken:campaign.Token);
        // Keep the solver's terminal diagnostic even if its check was cancelled.
        await File.WriteAllTextAsync(Path.Combine(directory,"solver.smt2"),result.SmtLib);
        await File.WriteAllTextAsync(Path.Combine(directory,"solver.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        if(result.Status!=(corrupted?EquivalenceStatus.Different:EquivalenceStatus.Equivalent))
            return campaign.Result(result.Status switch
            {
                EquivalenceStatus.Cancelled=>CampaignStatus.Cancelled,
                EquivalenceStatus.Unavailable=>CampaignStatus.MissingDependency,
                _=>CampaignStatus.Failed
            },result.Reason,result.Status.ToString());
        if(solverOnly){Console.WriteLine($"PASS {name}: Z3; Yosys explicitly omitted.");continue;}
        string Emit(HardwareGraph graph,int output,string module)=>CoreVerilogEmitter.Emit(graph,
            graph.Nodes.Where(n=>n.Operation==HardwareOperation.Input).ToDictionary(n=>n.Name!,n=>n.Id),new Dictionary<string,int>{{"result",output}},
            new Dictionary<string,int>(),new Dictionary<string,BigInteger>(),new Dictionary<string,HardwareMemoryPort>(),module);
        int width=sample.Left.Nodes.Single(n=>n.Id==sample.LeftOutput).Type.Width;
        string verilog=Emit(sample.Left,sample.LeftOutput,"left")+Emit(sample.Right,sample.RightOutput,"right")+
            $"\nmodule miter(input [1:0] x,y,output mismatch); wire [{width-1}:0] l,r;\n"+
            "h_left L(.clk(1'b0),.rst(1'b0),.s_x(x),.s_y(y),.s_result(l));\n"+
            "h_right R(.clk(1'b0),.rst(1'b0),.s_x(x),.s_y(y),.s_result(r)); assign mismatch=(l!=r); endmodule\n";
        await File.WriteAllTextAsync(Path.Combine(directory,"design.v"),verilog,campaign.Token);
        string yosysEvidence=Path.Combine(directory,"yosys");
        campaign.EnterStage("rtl-check."+name,yosysEvidence);
        var run=await HdlToolRunner.RunAsync(yosys!,["-Q","-T","-p","read_verilog design.v; hierarchy -check -top miter; proc; flatten; opt; sat -prove mismatch 0 -show-inputs -show-outputs -dump_json counterexample.json"],
            directory,yosysEvidence,TimeSpan.FromSeconds(30),campaign.Token);
        if(run.Status!=ToolRunStatus.Success)return campaign.ToolFailure(run);
        string log=await File.ReadAllTextAsync(Path.Combine(yosysEvidence,"stdout.bin"),campaign.Token);
        if(!log.Contains(corrupted?"model found: FAIL":"no model found: SUCCESS",StringComparison.Ordinal))
            return campaign.Result(CampaignStatus.Failed,"Yosys did not establish the expected outcome. Inspect the retained logs.","UnexpectedProofOutcome");
        if(corrupted)
        {
            // Every corrupt reference differs at exactly x=3,y=1; verify the independent
            // solver's concrete model, not merely an exit code or a banner substring.
            campaign.EnterStage("check-witness."+name,directory);
            string path=Path.Combine(directory,"counterexample.json");
            if(!File.Exists(path))return campaign.Result(CampaignStatus.Failed,"Missing Yosys counterexample.","MissingWitness");
            using var json=JsonDocument.Parse(await File.ReadAllTextAsync(path,campaign.Token));
            if(!MatchesWitness(json.RootElement,"x",3)||!MatchesWitness(json.RootElement,"y",1))
                return campaign.Result(CampaignStatus.Failed,"Yosys did not retain the expected binary witness.","InvalidWitness");
            if(!result.Counterexample.TryGetValue("x",out var x)||x!="3"||
               !result.Counterexample.TryGetValue("y",out var y)||y!="1")
                return campaign.Result(CampaignStatus.Failed,"Z3 returned an unexpected corruption witness.","InvalidWitness");
        }
        Console.WriteLine($"PASS {name}: Z3 and Yosys agree.");
    }
    return campaign.Result(CampaignStatus.Success,
        $"{BitVectorCases.Names.Count*2} {(solverOnly?"Z3-only":"independent solver/RTL")} checks completed.");
}

// Check the consumed WaveJSON shape before indexing it. Malformed evidence is
// an expected qualification failure and carries the case's evidence directory.
static bool MatchesWitness(JsonElement root,string name,int expected)
{
    if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("signal",out var signals)||
       signals.ValueKind!=JsonValueKind.Array)return false;
    bool matched=false;
    foreach(var signal in signals.EnumerateArray())
    {
        if(signal.ValueKind!=JsonValueKind.Object||!signal.TryGetProperty("name",out var label)||
           label.ValueKind!=JsonValueKind.String||label.GetString()!.TrimStart('\\')!=name)continue;
        if(matched||!signal.TryGetProperty("data",out var values)||values.ValueKind!=JsonValueKind.Array||
           values.GetArrayLength()==0||values[0].ValueKind!=JsonValueKind.String)return false;
        string bits=values[0].GetString()!;
        if(bits.Length is <1 or >2||bits.Any(c=>c is not ('0' or '1'))||Convert.ToInt32(bits,2)!=expected)return false;
        matched=true;
    }
    return matched;
}
