using SharpHDL;

// Benchmark protocol: bits [15:0] payload; bit 31 ends the packet;
// bits [30:16] ignored. Output has the running modulo-65536 sum in [15:0]
// and the same end flag in bit 31. Every input produces one output.
// This is an educational additive checksum, not Ethernet CRC or IP checksum.
public static class PacketBenchmark
{
    [HardwareProcess]
    public static void Checksum(HwStream<uint> input,HwStream<uint> output)
    {
        uint sum=0;
        while(true)
        {
            uint word=input.Read();
            uint total=unchecked(sum+(word&0xffff))&0xffff;
            output.Write(total|(word&0x80000000));
            sum=(word&0x80000000)!=0?0:total;
        }
    }
}
