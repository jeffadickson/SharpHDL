using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using NSubstitute;
using SharpHDL.HLS;
using SharpHDL.SymbolFixtures;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Matched, missing, malformed, embedded and hidden-region source provenance.</summary>
public sealed class ScheduledSourceMapTests
{
    /// <summary>Natural source path and checksum fixture.</summary>
    public static uint Natural(uint value)=>unchecked(value+1);

    /// <summary>Visible and hidden CIL regions with explicit source-line directives.</summary>
    public static uint Marked(uint value)
    {
#line 400 "SharpHDL_ScheduledSourceFixture.cs"
        uint result=value+1;
#line hidden
        result^=3;
#line 450 "SharpHDL_ScheduledSourceFixture.cs"
        return result;
#line default
    }

    /// <summary>The map is matched to the loaded PE and its checksum identifies the source bytes.</summary>
    [Fact]
    public void Sidecar_symbols_report_actual_coordinates_and_checksum()
    {
        var method=typeof(ScheduledSourceMapTests).GetMethod(nameof(Natural))!;
        var map=PortablePdbSourceMap.Read(method);
        Assert.Equal(PortablePdbMapStatus.Available,map.Status);
        var point=Assert.Single(map.Points,p=>p.Source!=null);var source=point.Source!;
        Assert.EndsWith(nameof(ScheduledSourceMapTests)+".cs",source.File);
        Assert.True(File.Exists(source.File));Assert.True(source.StartLine>0);
        Assert.Equal(new Guid("8829d00f-11b8-4213-878b-770e8597ac16"),source.DocumentHashAlgorithm);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source.File))),source.DocumentHash);
        Assert.Equal(source,map.FindSource(point.IlOffset));
        Assert.Null(map.FindSource(map.BodySize));Assert.Null(map.FindSource(int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(()=>map.FindSource(-1));
    }

    /// <summary>A hidden sequence point terminates the preceding visible source span.</summary>
    [Fact]
    public void Hidden_regions_and_encoded_states_are_not_assigned_guessed_lines()
    {
        var method=typeof(ScheduledSourceMapTests).GetMethod(nameof(Marked))!;
        var compiled=CilCompiler.BuildScheduledSequential(method,"SourceStates");
        Assert.Equal(PortablePdbMapStatus.Available,compiled.Symbols.Status);
        Assert.Contains(compiled.Symbols.Points,p=>p.Source?.StartLine==400);
        Assert.Contains(compiled.Symbols.Points,p=>p.Source?.StartLine==450);
        var hidden=Assert.Single(compiled.Symbols.Points,p=>p.IsHidden);
        Assert.Null(compiled.Symbols.FindSource(hidden.IlOffset));
        Assert.Equal(0,compiled.StateEncodings["idle"]);Assert.Null(compiled.StateSources["idle"]);
        Assert.Equal(compiled.StateEncodings.Count,compiled.StateSources.Count);
        Assert.Equal(compiled.StateOffsets.Count+1,compiled.StateEncodings.Count);
        foreach(var pair in compiled.StateOffsets)
        {
            Assert.Equal(compiled.Symbols.FindSource(pair.Value),compiled.StateSources[pair.Key]);
            Assert.True(compiled.StateEncodings[pair.Key]>0);
        }
        Assert.Contains(compiled.StateOffsets,p=>p.Value==hidden.IlOffset && compiled.StateSources[p.Key]==null);
    }

    /// <summary>Embedded symbols use the BCL fallback even when no sidecar is supplied.</summary>
    [Fact]
    public void Embedded_pdb_is_read_without_a_sidecar()
    {
        var method=typeof(EmbeddedKernel).GetMethod(nameof(EmbeddedKernel.AddSeven))!;
        var map=PortablePdbSourceMap.Read(method,_=>null);
        Assert.Equal(PortablePdbMapStatus.Available,map.Status);
        Assert.Contains(map.Points,p=>p.Source?.File.EndsWith("EmbeddedKernel.cs")==true);
    }

    /// <summary>Missing and different-module symbols cannot fabricate locations.</summary>
    [Fact]
    public void Missing_and_mismatched_symbols_have_explicit_status()
    {
        var method=typeof(ScheduledSourceMapTests).GetMethod(nameof(Natural))!;
        var missing=PortablePdbSourceMap.Read(method,_=>null);
        Assert.Equal(PortablePdbMapStatus.UnavailableSymbols,missing.Status);
        Assert.Empty(missing.Points);Assert.Null(missing.FindSource(0));
        var other=Path.ChangeExtension(typeof(ScheduledValue).Module.FullyQualifiedName,".pdb");
        Assert.True(File.Exists(other));
        var mismatch=PortablePdbSourceMap.Read(method,_=>File.OpenRead(other));
        Assert.Equal(PortablePdbMapStatus.UnavailableSymbols,mismatch.Status);Assert.Empty(mismatch.Points);
    }

    /// <summary>Malformed symbols, unexpected IO and invalid stream contracts are visible failures.</summary>
    [Fact]
    public void Invalid_symbol_sources_are_not_silently_swallowed()
    {
        var method=typeof(ScheduledSourceMapTests).GetMethod(nameof(Natural))!;
        Assert.Throws<BadImageFormatException>(()=>PortablePdbSourceMap.Read(method,_=>new MemoryStream(new byte[]{1,2,3,4})));
        Assert.Throws<IOException>(()=>PortablePdbSourceMap.Read(method,_=>throw new IOException("symbol read failed")));
        var stream=Substitute.For<System.IO.Stream>();stream.CanRead.Returns(true);stream.CanSeek.Returns(false);
        Assert.Throws<InvalidOperationException>(()=>PortablePdbSourceMap.Read(method,_=>stream));
        Assert.Throws<ArgumentNullException>(()=>PortablePdbSourceMap.Read(null!));
    }

    /// <summary>In-memory methods still compile without inventing a file-backed symbol identity.</summary>
    [Fact]
    public void In_memory_module_compiles_with_unavailable_image_status()
    {
        var assembly=AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("SourceFixture"+Guid.NewGuid().ToString("N")),AssemblyBuilderAccess.RunAndCollect);
        var type=assembly.DefineDynamicModule("main").DefineType("Kernel",TypeAttributes.Public);
        var method=type.DefineMethod("Value",MethodAttributes.Public|MethodAttributes.Static,typeof(uint),Type.EmptyTypes);
        var il=method.GetILGenerator();il.Emit(OpCodes.Ldc_I4_7);il.Emit(OpCodes.Ret);
        var info=type.CreateType()!.GetMethod("Value")!;
        var compiled=CilCompiler.BuildScheduledSequential(info,"InMemory");
        Assert.Equal(PortablePdbMapStatus.UnavailableImage,compiled.Symbols.Status);
        Assert.All(compiled.StateSources.Values,source=>Assert.Null(source));
    }

    /// <summary>Hexadecimal names that overflow a nonnegative CIL offset remain unmapped.</summary>
    [Theory]
    [InlineData("IL_ffffffff")] [InlineData("IL_80000000")] [InlineData("IL_100000000")]
    public void Invalid_offset_names_do_not_become_negative_locations(string name)
        =>Assert.Null(PortablePdbSourceMap.ParseIlOffset(name));
}
