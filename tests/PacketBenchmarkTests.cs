using System;
using System.IO;
using System.Linq;
using SharpHDL;

static class PacketBenchmarkTests
{
    public static void Run()
    {
        Directory.CreateDirectory("generated/packet-benchmark");
        foreach(bool early in new[]{false,true})
        {
        string stem=early?"sharp":"baseline";
        var m=new Module("PacketChecksum");
        var input=HwStream<uint>.Input(m,"in");
        var output=HwStream<uint>.Output(m,"out");
        input.Data.Describe("Packet word: [15:0] unsigned payload; [30:16] ignored; [31] last word. Transfer on valid AND ready.");
        output.Data.Describe("Per-word running sum: [15:0] modulo-65536 sum including this word; [30:16] zero; [31] last word. Last word resets the next packet sum. Holds while stalled.");
        var h=CilCompiler.AttachFeedbackPipeline(m,"checksum",typeof(PacketBenchmark).GetMethod("Checksum"),input,output,
            new FeedbackPipelineOptions{LogicLevelsPerStage=32,ForwardOnCommit=true,AdvanceStateOnAccept=early});
        if(h.Latency!=1||h.InitiationInterval!=1)throw new Exception("Packet benchmark requires latency=1 and II=1.");
        File.WriteAllText("generated/packet-benchmark/"+stem+".v",m.Verilog());
        File.WriteAllText("generated/packet-benchmark/"+stem+".schedule.txt",h.Report);
        File.WriteAllText("generated/packet-benchmark/source.cil.txt",string.Join("\n",CilCompiler.Disassemble(typeof(PacketBenchmark).GetMethod("Checksum")).Split('\n').Select(line=>line.TrimEnd())));
        }
        Console.WriteLine("PASS packet checksum elaboration: latency=1, II=1");
    }
}
