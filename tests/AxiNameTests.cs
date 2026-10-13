using System;
using System.IO;
using SharpHDL;
static class AxiNameTests
{
    public static void Run()
    {
        foreach(var name in new[]{"aclk","aresetn"}) {
            var bad=new Module("Reserved");bad.Input(name,1);
            try { Axi4Stream.Wrapper(bad);throw new Exception("Accepted reserved AXI name "+name); }
            catch(ArgumentException) { }
        }
        var m=new Module("AxiNames");
        foreach(var name in new[]{"eventually","nexttime","until","until_with","core_1","core_out_tvalid_1"})m.Input(name,1);
        var keyword=m.Input("module",1);var core=m.Input("core",1);
        var collision=m.Input("core_out_tvalid",1);
        m.Drive(m.Output("out_tvalid",1),keyword & core & collision);
        m.Drive(m.Output("always",1),keyword);
        Directory.CreateDirectory("build/axi-names");
        File.WriteAllText("build/axi-names/design.v",m.Verilog()+Axi4Stream.Wrapper(m));
        File.WriteAllText("build/axi-names/tb.v",@"module tb;
reg aclk=0,aresetn=0,a=1,b=1,c=1;wire valid,y;
h_AxiNames_axi dut(.aclk(aclk),.aresetn(aresetn),.\module (a),.core(b),.core_out_tvalid(c),.out_tvalid(valid),.\always (y));
initial begin #1;if(valid!==0 || y!==1)$fatal(1,""reset masking"");
aresetn=1;#1;if(valid!==1)$fatal(1,""names"");c=0;#1;if(valid!==0)$fatal(1,""collision"");
$display(""PASS AXI identifiers and reset masking"");$finish;end endmodule");
        Console.WriteLine("PASS AXI reserved-name rejection and emitted regression fixture");
    }
}
