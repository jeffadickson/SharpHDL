using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Rejections retain their API type while reporting actual matched CIL/source provenance.</summary>
public sealed class CilDiagnosticTests
{
    private static class Kernels
    {
#line 700
        public static uint Divide(uint value) => value / 3;
#line 710
        public static void NoResult() { }
#line hidden
        public static uint Hidden(uint value) => value / 3;
#line default
    }

    /// <summary>A real unsupported opcode maps to its sequence point and retains method/IL identity.</summary>
    [Fact]
    public void Unsupported_opcode_has_structured_source_location()
    {
        var method=typeof(Kernels).GetMethod(nameof(Kernels.Divide))!;
        var error=Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(method,"Diagnostic"));
        var diagnostic=Assert.IsType<CilDiagnostic>(CilDiagnostic.FromException(error));
        Assert.Equal("SHDL-CIL-001",diagnostic.Code);Assert.Same(method,diagnostic.Method);
        Assert.Contains("div",diagnostic.Reason);Assert.True(diagnostic.IlOffset>0);
        Assert.Equal(PortablePdbMapStatus.Available,diagnostic.Symbols);
        Assert.NotNull(diagnostic.Source);Assert.Equal(700,diagnostic.Source.StartLine);
        Assert.EndsWith("CilDiagnosticTests.cs",diagnostic.Source.File);
        Assert.Contains("IL_"+diagnostic.IlOffset.ToString("x4"),error.Message);
        Assert.Contains(method.DeclaringType!.FullName+"."+method.Name,error.Message);
        Assert.Contains(":700:"+diagnostic.Source.StartColumn+")",error.Message);Assert.Null(diagnostic.SymbolFailure);
    }

    /// <summary>Hidden source is explicitly absent rather than borrowed from an earlier visible method.</summary>
    [Fact]
    public void Hidden_sequence_point_does_not_fabricate_source()
    {
        var error=Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(typeof(Kernels).GetMethod(nameof(Kernels.Hidden))!,"Hidden"));
        var diagnostic=CilDiagnostic.FromException(error)!;
        Assert.Equal(PortablePdbMapStatus.Available,diagnostic.Symbols);Assert.Null(diagnostic.Source);
        Assert.DoesNotContain(":700:",error.Message);
    }

    /// <summary>Signature rejection at offset zero still resolves a visible sequence point.</summary>
    [Fact]
    public void Zero_offset_rejection_preserves_source()
    {
        var error=Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(typeof(Kernels).GetMethod(nameof(Kernels.NoResult))!,"VoidDiagnostic"));
        var diagnostic=CilDiagnostic.FromException(error)!;
        Assert.Equal(0,diagnostic.IlOffset);Assert.Equal(710,diagnostic.Source!.StartLine);
    }

    /// <summary>Raw in-memory methods preserve useful diagnostics without requiring on-disk images.</summary>
    [Fact]
    public void In_memory_method_reports_missing_image_and_real_il_offset()
    {
        var type=AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Diagnostic"+Guid.NewGuid().ToString("N")),AssemblyBuilderAccess.RunAndCollect)
            .DefineDynamicModule("main").DefineType("Kernel",TypeAttributes.Public);
        var method=type.DefineMethod("Divide",MethodAttributes.Public|MethodAttributes.Static,typeof(uint),new[]{typeof(uint)});
        var il=method.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldc_I4_3);il.Emit(OpCodes.Div_Un);il.Emit(OpCodes.Ret);
        var error=Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(type.CreateType()!.GetMethod("Divide")!,"RawDiagnostic"));
        var diagnostic=CilDiagnostic.FromException(error)!;
        Assert.Equal(2,diagnostic.IlOffset);Assert.Equal(PortablePdbMapStatus.UnavailableImage,diagnostic.Symbols);
        Assert.Null(diagnostic.Source);Assert.Null(error.InnerException);
    }

    /// <summary>A malformed negative branch target must not turn diagnostic lookup into a new exception.</summary>
    [Fact]
    public void Malformed_negative_branch_preserves_the_primary_rejection()
    {
        var type=AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("BadBranch"+Guid.NewGuid().ToString("N")),AssemblyBuilderAccess.RunAndCollect)
            .DefineDynamicModule("main").DefineType("Kernel",TypeAttributes.Public);
        var method=type.DefineMethod("Broken",MethodAttributes.Public|MethodAttributes.Static,typeof(uint),Type.EmptyTypes);
        var il=method.GetILGenerator();il.Emit(OpCodes.Br_S,unchecked((sbyte)-128));il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ret);
        var info=type.CreateType()!.GetMethod("Broken")!;
        var error=Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(info,"BadBranch"));
        var diagnostic=CilDiagnostic.FromException(error)!;
        Assert.Contains("branch target",diagnostic.Reason);Assert.Null(diagnostic.Source);Assert.Null(diagnostic.SymbolFailure);
        // The legacy frontend reports the target itself; scheduled validation now
        // reports the originating branch. Both must preserve their primary error.
        var legacy=Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildSequential(info,"LegacyBadBranch"));
        Assert.True(CilDiagnostic.FromException(legacy)!.IlOffset<0);
        Assert.Null(CilDiagnostic.FromException(legacy)!.Source);
    }

    /// <summary>Global emitted methods have no declaring type but still need usable compiler errors.</summary>
    [Fact]
    public void Global_method_without_declaring_type_reports_its_own_name()
    {
        var module=AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("GlobalDiagnostic"+Guid.NewGuid().ToString("N")),AssemblyBuilderAccess.RunAndCollect).DefineDynamicModule("main");
        var method=module.DefineGlobalMethod("GlobalDivide",MethodAttributes.Public|MethodAttributes.Static,typeof(uint),new[]{typeof(uint)});
        var il=method.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldc_I4_3);il.Emit(OpCodes.Div_Un);il.Emit(OpCodes.Ret);
        module.CreateGlobalFunctions();var info=module.GetMethod("GlobalDivide")!;Assert.Null(info.DeclaringType);
        var error=Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(info,"GlobalDiagnostic"));
        Assert.StartsWith("GlobalDivide IL_0002:",error.Message);Assert.Same(info,CilDiagnostic.FromException(error)!.Method);
    }

    /// <summary>Corrupted optional PDB bytes retain the primary rejection and expose their own failure.</summary>
    [Fact]
    public void Malformed_symbols_do_not_replace_the_compiler_rejection()
    {
        string parent=Path.GetTempPath();Assert.True(Directory.Exists(parent));
        string root=Path.Combine(parent,"sharphdl-diagnostic-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string image=Path.Combine(root,"SharpHDL.UnitTests.dll");
        File.Copy(typeof(CilDiagnosticTests).Assembly.Location,image);
        File.WriteAllBytes(Path.ChangeExtension(image,".pdb"),new byte[]{1,2,3,4});
        var context=new AssemblyLoadContext("DiagnosticSymbols",isCollectible:true);
        try
        {
            var assembly=context.LoadFromAssemblyPath(image);
            var method=assembly.GetType(typeof(Kernels).FullName!)!.GetMethod(nameof(Kernels.Divide))!;
            var error=Assert.Throws<NotSupportedException>(()=>CilCompiler.BuildScheduledSequential(method,"BadSymbols"));
            var diagnostic=CilDiagnostic.FromException(error)!;
            Assert.Contains("opcode",diagnostic.Reason);Assert.Null(diagnostic.Source);Assert.Null(diagnostic.Symbols);
            Assert.IsType<BadImageFormatException>(diagnostic.SymbolFailure);Assert.Same(diagnostic.SymbolFailure,error.InnerException);
            Assert.Contains("symbol lookup failed",error.Message);
        }
        finally { context.Unload(); }
        // The isolated malformed fixture is retained for reproduction; no real
        // assembly or symbol file is modified, and no process-wide loader is reset.
    }

    /// <summary>Foreign exceptions are not misclassified as compiler failures.</summary>
    [Fact]
    public void Unrelated_exception_has_no_diagnostic()
    {
        Assert.Null(CilDiagnostic.FromException(new InvalidOperationException("other")));
        Assert.Throws<ArgumentNullException>(()=>CilDiagnostic.FromException(null!));
    }
}
