using DotNet.Testcontainers.Builders;
using SharpHDL;
using Xunit;

namespace SharpHDL.IntegrationTests;

// Docker is mandatory for this separately invoked project; failures must not silently skip.
public sealed class IcarusIntegrationTests
{
    [Fact]
    public async Task Generated_counter_matches_rtl_reference_trace()
    {
        var m = new Module("ContainerCounter");
        var en = m.Input("en", 1);
        var q = m.Register("q", 8, 0);
        m.NextWhen(q, en, q + Expr.Const(8, 1));
        var y = m.Output("y", 8);
        m.Drive(y, q);
        string rtl = m.Verilog();
        string bench = @"module tb;
reg clk=0,rst=1,en=0;
wire [7:0] y;
h_ContainerCounter dut(.clk(clk),.rst(rst),.s_en(en),.s_y(y));
integer i;
initial begin
 #1;clk=1;#1;if(y!==0)$fatal(1,""reset mismatch"");clk=0;rst=0;en=1;
 for(i=1;i<=40;i=i+1)begin
 #1;clk=1;#1;if(y!==i)$fatal(1,""counter mismatch cycle %0d"",i);clk=0;
 end
 $display(""PASS: 40 reference cycles"");$finish;
end
endmodule";
        // Isolated Ubuntu fixture installs an actual Icarus binary; replace with a
        // digest-pinned prebuilt CI image for stable, fast production testing.
        await using var container = new ContainerBuilder()
            .WithImage("ubuntu:24.04")
            .WithEntrypoint("sleep")
            .WithCommand("infinity")
            .Build();
        await container.StartAsync();
        var install = await container.ExecAsync(new[] { "sh", "-lc",
            "apt-get update -qq && DEBIAN_FRONTEND=noninteractive apt-get install -qq -y iverilog" });
        Assert.Equal(0, install.ExitCode);
        static string ShellQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";
        async Task WriteFile(string path, string content)
        {
            string b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(content));
            var cmd = await container.ExecAsync(new[] { "sh", "-lc",
                "printf %s " + ShellQuote(b64) + " | base64 -d > " + path });
            Assert.Equal(0, cmd.ExitCode);
        }
        await WriteFile("/tmp/counter.v", rtl);
        await WriteFile("/tmp/tb.v", bench);
        var run = await container.ExecAsync(new[] { "sh", "-lc",
            "iverilog -g2012 -s tb -o /tmp/counter.out /tmp/counter.v /tmp/tb.v && vvp /tmp/counter.out" });
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("PASS: 40 reference cycles", run.Stdout);
    }
}
