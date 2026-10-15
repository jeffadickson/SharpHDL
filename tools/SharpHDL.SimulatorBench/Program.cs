using System;
using System.Diagnostics;
using System.Numerics;
using SharpHDL;

/// <summary>Reproducible JIT vs graph interpreter microbenchmark.
/// Excludes compilation from steady-state throughput but reports it separately.
/// Never infer a whole-CPU speedup from this simple arithmetic circuit.</summary>
internal static class Program
{
    private static void Main(string[] args)
    {
        const int warmup = 10000;
        int cycles = args.Length > 0 ? int.Parse(args[0]) : 200000;
        if(cycles < 1 || cycles > 10000000) throw new ArgumentOutOfRangeException(nameof(cycles));
        var m = new Module("CompiledBenchmarkCounter");
        var enable = m.Input("enable", 1);
        var accum = m.Register("accum", 32, 0);
        m.NextWhen(accum, enable, accum + Expr.Const(32, 7));
        var reference = m.Simulate();
        var compileWatch = Stopwatch.StartNew();
        var compiled = new CompiledSequentialSimulator(m);
        compileWatch.Stop();
        reference.Tick(true); compiled.Tick(true);
        reference.Set(enable, 1); compiled.Set(enable, 1);
        for(int i=0;i<warmup;i++) { reference.Tick(); compiled.Tick(); }
        if(reference.Read(accum)!=compiled.Read(accum)) throw new Exception("Warmup divergence");
        var watch=Stopwatch.StartNew();
        for(int i=0;i<cycles;i++) reference.Tick();
        watch.Stop();
        double interpreterSeconds=watch.Elapsed.TotalSeconds;
        watch.Restart();
        for(int i=0;i<cycles;i++) compiled.Tick();
        watch.Stop();
        double jitSeconds=watch.Elapsed.TotalSeconds;
        if(reference.Read(accum)!=compiled.Read(accum))
            throw new Exception("Reference and compiled result mismatch");
        Console.WriteLine("SharpHDL benchmark: .NET "+Environment.Version);
        Console.WriteLine("Workload: 32-bit enabled accumulator, "+cycles+" timed clock edges");
        Console.WriteLine("JIT setup: "+compileWatch.Elapsed.TotalMilliseconds.ToString("F3")+" ms");
        Console.WriteLine("Interpreter: "+(cycles/interpreterSeconds).ToString("F0")+" edges/sec");
        Console.WriteLine("Compiled .NET: "+(cycles/jitSeconds).ToString("F0")+" edges/sec");
        Console.WriteLine("Ratio: "+(interpreterSeconds/jitSeconds).ToString("F3")+"x");
        Console.WriteLine("PASS matching final register state; timings are machine-dependent");
    }
}
