using System;
using System.Linq;
using SharpHDL;
using Xunit;

namespace SharpHDL.UnitTests;

public sealed class PortablePdbSourceMapTests
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("xUnit", "xUnit1013", Justification = "Public synthesis fixture accessed by reflection; this is not a test entry point.")]
    public static void Increment(HwStream<ushort> input, HwStream<ushort> output)
    {
        while (true) {
            ushort x = input.Read();
            output.Write((ushort)(x + 1));
        }
    }

    [Theory]
    [InlineData("Increment_IL_002a_add",42)]
    [InlineData("IL_0000",0)]
    [InlineData("not_an_offset",null)]
    public void Extracts_real_IL_offsets_only(string hint,int? expected)
        => Assert.Equal(expected,PortablePdbSourceMap.ParseIlOffset(hint));

    [Fact]
    public void CIL_pipeline_maps_to_sequence_points_when_portable_PDB_is_present()
    {
        var module=new Module("SourceMapping");
        var input=HwStream<ushort>.Input(module,"in");
        var output=HwStream<ushort>.Output(module,"out");
        var method=typeof(PortablePdbSourceMapTests).GetMethod(nameof(Increment))!;
        var handle=CilCompiler.AttachPipeline(module,"increment",method,input,output);
        Assert.NotEmpty(handle.DataflowGraph.Nodes);
        var pdb=System.IO.Path.ChangeExtension(method.Module.FullyQualifiedName,".pdb");
        if (!System.IO.File.Exists(pdb)) return; // PDB not required for synthesis.
        Assert.Contains(handle.DataflowGraph.Nodes,n=>n.SourceLine.HasValue && n.SourceLine.Value>0);
        Assert.All(handle.DataflowGraph.Nodes.Where(n=>n.SourceLine.HasValue),n=>
        {
            Assert.NotNull(n.SourceFile);
            Assert.True(n.IlOffset.HasValue);
        });
    }
}
