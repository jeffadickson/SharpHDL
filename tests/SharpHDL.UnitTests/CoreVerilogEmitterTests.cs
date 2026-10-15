using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using SharpHDL;
using SharpHDL.Core;
using SharpHDL.HLS;
using SharpHDL.RTL;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Checks the native Core RTL backend without invoking the legacy writer.</summary>
public sealed class CoreVerilogEmitterTests
{
    [Fact]
    public void Native_backend_emits_real_registered_elastic_pipeline()
    {
        var module=new Module("CoreRtlTest");
        var input=module.Input("input",16);
        var output=module.Output("output",16);
        module.Drive(output,(input+Expr.Const(16,5))*input);
        var source=module.ExportCoreGraph();
        var compiled=HlsElasticCompiler.Compile(source.Graph,
            source.Inputs["input"],source.Outputs["output"],2,
            new HlsCostProfile{MultiplyCost=2,ArithmeticCost=1});
        var rtl=CoreVerilogEmitter.Emit(compiled.Graph,compiled.Inputs,
            compiled.Outputs,compiled.NextState,compiled.ResetValues,"CoreRtlTest");
        Assert.Contains("module h_CoreRtlTest(",rtl);
        Assert.Contains("always @(posedge clk)",rtl);
        Assert.Contains("if (rst)",rtl);
        Assert.Contains("s_hls_input_valid",rtl);
        Assert.Contains("s_hls_output_ready",rtl);
        Assert.Contains("s_hls_output_valid",rtl);
        Assert.Contains(" * ",rtl);
        Assert.Contains("s_hls_valid_",rtl);
        Assert.Equal(compiled.ResetValues.Keys.Count(name=>name.StartsWith("hls_valid_",StringComparison.Ordinal)),
            System.Text.RegularExpressions.Regex.Matches(rtl,@"<= 1'd0;").Count);
    }

    [Fact]
    public void Native_backend_rejects_missing_memory_metadata_instead_of_miscompiling()
    {
        var m=new Module("NativeMemoryReject");
        var addr=m.Input("addr",2);
        var write=m.Input("write",1);
        var data=m.Input("data",8);
        var ram=SynchronousRam.Create(m,"ram",8,4,addr,write,data,
            Expr.Const(1,1),new BigInteger[]{1,2,3,4});
        var result=m.Output("result",8);
        m.Drive(result,ram.ReadData);
        var export=m.ExportCoreGraph();
        Assert.Throws<ArgumentException>(()=>
            CoreVerilogEmitter.Emit(export.Graph,export.Inputs,export.Outputs,
                export.NextState,export.ResetValues,"NativeMemoryReject"));
    }

    [Fact]
    public void Native_backend_validates_identifiers()
    {
        var m=new Module("IdentifierSource");
        var input=m.Input("input",8);
        var output=m.Output("output",8);
        m.Drive(output,input);
        var export=m.ExportCoreGraph();
        Assert.Throws<ArgumentException>(()=>
            CoreVerilogEmitter.Emit(export.Graph,export.Inputs,export.Outputs,
                export.NextState,export.ResetValues,"not-valid!"));
    }
    [Fact]
    public void Native_backend_emits_initialized_ROM_and_read_before_write_RAM()
    {
        var m=new Module("NativeMemory");
        var addr=m.Input("addr",2);
        var write=m.Input("write",1);
        var data=m.Input("data",8);
        var ram=SynchronousRam.Create(m,"ram",8,4,addr,write,data,
            Expr.Const(1,1),new BigInteger[]{17,33,65,129});
        var rom=m.Output("rom",8);
        var result=m.Output("result",8);
        m.Drive(rom,Expr.Lookup(8,addr,new BigInteger[]{3,5,7,11}));
        m.Drive(result,ram.ReadData);
        var core=m.ExportCoreGraph();
        var rtl=CoreVerilogEmitter.Emit(core.Graph,core.Inputs,core.Outputs,
            core.NextState,core.ResetValues,core.Memories,"NativeMemory");
        Assert.Contains("function [7:0] rom_n_",rtl);
        Assert.Contains("default: rom_n_",rtl);
        Assert.Contains("reg [7:0] mem_ram [0:3]",rtl);
        Assert.Contains("mem_ram[0] = 8'd17",rtl);
        Assert.Contains("if (!rst &&",rtl);
        Assert.Contains("mem_ram[n_",rtl);
        Assert.Contains("always @(posedge clk)",rtl);
    }

}
