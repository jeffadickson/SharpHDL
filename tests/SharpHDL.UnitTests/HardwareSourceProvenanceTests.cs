using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using SharpHDL;
using Xunit;

namespace SharpHDL.UnitTests;

public sealed class HardwareSourceProvenanceTests
{
    public static void DoubleIt(HwStream<ushort> input,HwStream<ushort> output)
    {
        while(true)
        {
            ushort x=input.Read();
            output.Write((ushort)(x+x));
        }
    }

    [Fact]
    public void Generated_pipeline_emits_only_real_source_and_RTL_locations()
    {
        var m=new Module("ProvenanceFixture");
        var input=HwStream<ushort>.Input(m,"in");
        var output=HwStream<ushort>.Output(m,"out");
        var method=typeof(HardwareSourceProvenanceTests).GetMethod(nameof(DoubleIt))!;
        var pipeline=CilCompiler.AttachPipeline(m,"doubler",method,input,output);
        var root=Directory.GetCurrentDirectory();
        while(!File.Exists(Path.Combine(root,"Makefile")) && Directory.GetParent(root)!=null)
            root=Directory.GetParent(root)!.FullName;
        Assert.True(File.Exists(Path.Combine(root,"Makefile")));
        var relative="generated/provenance-tests/"+Guid.NewGuid().ToString("N")+".v";
        var target=Path.Combine(root,relative);
        try
        {
            var json=HardwareSourceProvenance.Write(m,target,root);
            using var document=JsonDocument.Parse(json);
            var mappings=document.RootElement.GetProperty("mappings").EnumerateArray().ToArray();
            var rtlLines=File.ReadAllLines(target);
            Assert.True(File.Exists(target+".source-map.json"));
            // If a portable PDB is present and its source is in the checkout,
            // at least one named scheduled arithmetic operation must map.
            var hasSource=pipeline.DataflowGraph.Nodes.Any(n=>n.SourceLine.HasValue &&
                n.Name.Contains("_IL_") && n.Operation=="+");
            if(hasSource)Assert.NotEmpty(mappings);
            foreach(var mapping in mappings)
            {
                var src=mapping.GetProperty("source");
                var rtl=mapping.GetProperty("rtl");
                var line=rtl.GetProperty("line").GetInt32();
                Assert.InRange(line,1,rtlLines.Length);
                Assert.Contains("wire ",rtlLines[line-1]);
                Assert.EndsWith(".v",rtl.GetProperty("file").GetString());
                Assert.True(File.Exists(Path.Combine(root,src.GetProperty("file").GetString()!)));
            }
        }
        finally
        {
            if(File.Exists(target))File.Delete(target);
            if(File.Exists(target+".source-map.json"))File.Delete(target+".source-map.json");
        }
    }
    [HardwareProcess]
    public static void MultiplyTwoPaths(HwStream<uint> input,HwStream<uint> output)
    {
        while(true)
        {
            uint x=input.Read();
            uint a=x*7u;
            uint b=x*x;
            output.Write(a+b);
        }
    }

    [Fact]
    public void Hls_reports_multiplier_resource_and_timing_limitations()
    {
        var m=new Module("MultiplyResourceAdvisory");
        var input=HwStream<uint>.Input(m,"in");
        var output=HwStream<uint>.Output(m,"out");
        var method=typeof(HardwareSourceProvenanceTests).GetMethod(nameof(MultiplyTwoPaths))!;
        var pipeline=CilCompiler.AttachPipeline(m,"multiplier",method,input,output);
        Assert.Equal(2,pipeline.Operations.Count(op=>op.Operation=="*"));
        Assert.Contains("MULTIPLIER ADVISORY: 2",pipeline.Report);
        Assert.Contains("TIMING WARNING: wide multiplication",pipeline.Report);
        Assert.Contains("constant operand",pipeline.Report);
        Assert.Contains("target synthesis/STA",pipeline.Report);
        Assert.Contains("*",m.Verilog());
    }

}
