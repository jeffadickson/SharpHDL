using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SharpHDL;

// Deterministic correctness-gated baseline. It is NOT a cross-backend comparison.
static class SimulationBenchmark
{
    sealed class Result
    {
        public string Fixture { get; set; }
        public int Seed { get; set; }
        public int Cycles { get; set; }
        public int WarmupCycles { get; set; }
        public int Repetitions { get; set; }
        public double MedianCyclesPerSecond { get; set; }
        public double[] RepetitionSeconds { get; set; }
        public string RtlSha256 { get; set; }
        public string Runtime { get; set; }
        public string OS { get; set; }
        public string Architecture { get; set; }
        public bool CorrectnessPassed { get; set; }
    }

    // A fixed LCG makes the input independent of runtime Random implementations.
    static uint Next(ref uint state) { state = unchecked(state * 1664525u + 1013904223u); return state; }

    static void Execute(Module m, Signal input, Signal enable, Signal count, int seed, int cycles)
    {
        var sim = m.Simulate();
        uint state = unchecked((uint)seed);
        uint expected = 0;
        sim.Tick(reset: true);
        for (int i = 0; i < cycles; i++)
        {
            uint value = Next(ref state);
            uint en = (value >> 31) & 1u;
            uint addend = value & 255u;
            sim.Set(input, addend);
            sim.Set(enable, en);
            sim.Tick();
            if (en != 0) expected = (expected + addend) & 65535u;
            BigInteger actual = sim.Read(count);
            if (actual != expected)
                throw new InvalidOperationException("Simulation divergence at cycle " + i +
                    ": expected=" + expected + " actual=" + actual);
        }
    }

    public static void Run()
    {
        const int seed = 104729;
        const int cycles = 100000;
        const int warmup = 10000;
        const int repetitions = 5;
        var m = new Module("SimulationBenchmarkCounter");
        var input = m.Input("data", 16);
        var enable = m.Input("enable", 1);
        var count = m.Register("count", 16, 0);
        m.NextWhen(count, enable, count + input);
        var output = m.Output("value", 16);
        m.Drive(output, count);
        string rtl = m.Verilog();
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rtl))).ToLowerInvariant();

        Execute(m, input, enable, count, seed, warmup);
        var seconds = new double[repetitions];
        for (int i = 0; i < repetitions; i++)
        {
            var timer = Stopwatch.StartNew();
            Execute(m, input, enable, count, seed, cycles);
            timer.Stop();
            seconds[i] = timer.Elapsed.TotalSeconds;
        }
        var sorted = (double[])seconds.Clone();
        Array.Sort(sorted);
        var report = new Result {
            Fixture = "16-bit enabled accumulator, reference-oracle checked every cycle",
            Seed = seed, Cycles = cycles, WarmupCycles = warmup,
            Repetitions = repetitions, MedianCyclesPerSecond = cycles / sorted[repetitions / 2],
            RepetitionSeconds = seconds, RtlSha256 = hash,
            Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            CorrectnessPassed = true
        };
        Directory.CreateDirectory("generated");
        File.WriteAllText("generated/simulation-benchmark.json",
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS simulator benchmark: " +
            report.MedianCyclesPerSecond.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) +
            " verified cycles/s; generated/simulation-benchmark.json");
    }
}
