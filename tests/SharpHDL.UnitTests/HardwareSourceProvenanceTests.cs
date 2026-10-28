using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using SharpHDL;
using Xunit;

namespace SharpHDL.UnitTests;

public sealed class HardwareSourceProvenanceTests
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("xUnit", "xUnit1013", Justification = "Public synthesis fixture accessed by reflection; this is not a test entry point.")]
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
        // Resolve from the compiled source location so isolated Linux build outputs work too.
        var root=SourceDirectory();
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
    [System.Diagnostics.CodeAnalysis.SuppressMessage("xUnit", "xUnit1013", Justification = "Public synthesis fixture accessed by reflection; this is not a test entry point.")]
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

    [HardwareProcess]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("xUnit", "xUnit1013", Justification = "Public synthesis fixture accessed by reflection; this is not a test entry point.")]
    public static void CheckedMultiply(HwStream<uint> input,HwStream<uint> output)
    {
        while(true)
        {
            uint x=input.Read();
            output.Write(checked(x*3u));
        }
    }

    [Fact]
    public void Checked_multiplication_reports_unsupported_overflow_semantics()
    {
        var m=new Module("CheckedMultiply");
        var input=HwStream<uint>.Input(m,"in");
        var output=HwStream<uint>.Output(m,"out");
        var method=typeof(HardwareSourceProvenanceTests).GetMethod(nameof(CheckedMultiply))!;
        var error=Assert.Throws<NotSupportedException>(()=>
            CilCompiler.AttachPipeline(m,"checked_mul",method,input,output));
        Assert.Contains("Checked multiplication",error.Message);
    }

    [HardwareProcess]
    [HlsMultiplier(MultiplierImplementation.ShiftAdd)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("xUnit", "xUnit1013", Justification = "Public synthesis fixture accessed by reflection; this is not a test entry point.")]
    public static void MultiplyShiftAdd(HwStream<uint> input,HwStream<uint> output)
    {
        while(true)
        {
            uint x=input.Read();
            output.Write(x*(x+3u));
        }
    }

    [HardwareProcess]
    [HlsMultiplier(MultiplierImplementation.DspPreferred)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("xUnit", "xUnit1013", Justification = "Public synthesis fixture accessed by reflection; this is not a test entry point.")]
    public static void MultiplyDspPreferred(HwStream<uint> input,HwStream<uint> output)
    {
        while(true)
        {
            uint x=input.Read();
            output.Write(x*(x+3u));
        }
    }

    [Fact]
    public void Hls_multiplier_pragma_selects_real_structural_shift_add_or_dsp_hint()
    {
        Module Build(string name,string methodName,out PipelineHandle pipeline)
        {
            var m=new Module(name);
            var input=HwStream<uint>.Input(m,"in");
            var output=HwStream<uint>.Output(m,"out");
            pipeline=CilCompiler.AttachPipeline(m,"mul",
                typeof(HardwareSourceProvenanceTests).GetMethod(methodName)!,input,output);
            return m;
        }
        var fabric=Build("MulShiftAdd",nameof(MultiplyShiftAdd),out var fabricSchedule);
        var dsp=Build("MulDsp",nameof(MultiplyDspPreferred),out var dspSchedule);
        var fabricRtl=fabric.Verilog();
        var dspRtl=dsp.Verilog();
        Assert.Contains("Multiplier implementation: ShiftAdd",fabricSchedule.Report);
        Assert.Contains("Multiplier implementation: DspPreferred",dspSchedule.Report);
        Assert.DoesNotContain(" * ",fabricRtl);
        Assert.Contains(" ? ",fabricRtl);
        Assert.Contains(" + ",fabricRtl);
        Assert.Contains(" * ",dspRtl);
        Assert.Contains("use_dsp",dspRtl);
    }

    [Theory]
    [InlineData(nameof(MultiplyShiftAdd))]
    [InlineData(nameof(MultiplyDspPreferred))]
    public void Multiplier_architectures_match_modulo_32_bit_software_oracle(string methodName)
    {
        var m=new Module("MulOracle");
        var input=HwStream<uint>.Input(m,"in");
        var output=HwStream<uint>.Output(m,"out");
        var method=typeof(HardwareSourceProvenanceTests).GetMethod(methodName)!;
        CilCompiler.AttachPipeline(m,"mul",method,input,output);
        var sim=m.Simulate();
        sim.Tick(true);
        sim.Set(output.Ready,1);
        foreach(uint x in new uint[]{0,1,7,65535,0x80000000,uint.MaxValue})
        {
            sim.Set(input.Data,x);
            sim.Set(input.Valid,1);
            int guard=0;
            while(sim.Read(input.Ready).IsZero && guard++<200)sim.Tick();
            Assert.True(guard<200,"Multiplier input did not become ready");
            sim.Tick();
            sim.Set(input.Valid,0);
            guard=0;
            while(sim.Read(output.Valid).IsZero && guard++<200)sim.Tick();
            Assert.True(guard<200,"Multiplier output did not become valid");
            var expected=((System.Numerics.BigInteger)x *
                (System.Numerics.BigInteger)(unchecked(x+3u))) & Expr.Mask(32);
            Assert.Equal(expected,sim.Read(output.Data));
            sim.Tick();
        }
    }

    [HardwareProcess]
    [HlsMultiplier(MultiplierImplementation.BoothRadix4)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("xUnit", "xUnit1013", Justification = "Public synthesis fixture accessed by reflection; this is not a test entry point.")]
    public static void MultiplyBooth(HwStream<uint> input,HwStream<uint> output)
    {
        while(true)
        {
            uint x=input.Read();
            output.Write(x*(x+3u));
        }
    }

    [HardwareProcess]
    [HlsMultiplier(MultiplierImplementation.WallaceTree)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("xUnit", "xUnit1013", Justification = "Public synthesis fixture accessed by reflection; this is not a test entry point.")]
    public static void MultiplyWallace(HwStream<uint> input,HwStream<uint> output)
    {
        while(true)
        {
            uint x=input.Read();
            output.Write(x*(x+3u));
        }
    }

    [Theory]
    [InlineData(nameof(MultiplyBooth))]
    [InlineData(nameof(MultiplyWallace))]
    public void New_multiplier_architectures_match_modulo_32_bit_software_oracle(string methodName)
    {
        var m=new Module("MulNewOracle");
        var input=HwStream<uint>.Input(m,"in");
        var output=HwStream<uint>.Output(m,"out");
        var method=typeof(HardwareSourceProvenanceTests).GetMethod(methodName)!;
        var pipeline=CilCompiler.AttachPipeline(m,"mul",method,input,output,
            new PipelineOptions{MaxStages=256,LogicLevelsPerStage=4});
        var rtl=m.Verilog();
        Assert.DoesNotContain(" * ",rtl);
        Assert.True(pipeline.Latency>0);
        var sim=m.Simulate();
        sim.Tick(true);
        sim.Set(output.Ready,1);
        foreach(uint x in new uint[]{0,1,3,7,65535,0x80000000,uint.MaxValue})
        {
            sim.Set(input.Data,x);
            sim.Set(input.Valid,1);
            int guard=0;
            while(sim.Read(input.Ready).IsZero && guard++<300)sim.Tick();
            Assert.True(guard<300);
            sim.Tick();
            sim.Set(input.Valid,0);
            guard=0;
            while(sim.Read(output.Valid).IsZero && guard++<300)sim.Tick();
            Assert.True(guard<300);
            var expected=((System.Numerics.BigInteger)x *
                (System.Numerics.BigInteger)(unchecked(x+3u))) & Expr.Mask(32);
            Assert.Equal(expected,sim.Read(output.Data));
            sim.Tick();
        }
    }

    private static string SourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
        => Path.GetDirectoryName(sourceFile) ?? throw new InvalidOperationException("Test source directory unavailable.");
}
