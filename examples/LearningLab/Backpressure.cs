using SharpHDL;
using HStream = SharpHDL.Stream;

// Two independent lanes carry the same ordered packet to equally paused receivers.
// Each producer must obey its own Ready; do not fork Valid/Data without a broadcaster.
public static class Backpressure
{
    public static Module Build()
    {
        var m = new Module("Backpressure");
        Lane(m, "direct", false);
        Lane(m, "buffered", true);
        return m;
    }
    static void Lane(Module m, string name, bool skid)
    {
        var input = HStream.Input(m, name + "_in", 8, 2);
        var output = HStream.Output(m, name + "_out", 8, 2);
        input.Data.Describe("Unsigned token ID [7:0]; the lesson sends 10, 11, 12 in order.");
        input.User.Describe("Token position [1:0]: 0, 1, 2. KEEP is one; LAST marks token 12.");
        var path = skid ? StreamBlocks.SkidBuffer(m, input, "skid") : input;
        HStream.Connect(m, path, output);
        m.Drive(m.Output(name + "_accepted", 1), input.Fire.Named(name + "_input_handshake"));
        m.Drive(m.Output(name + "_delivered", 1), output.Fire.Named(name + "_output_handshake"));
        // These are handshake conditions, counted only at a rising edge outside reset.
    }
}
