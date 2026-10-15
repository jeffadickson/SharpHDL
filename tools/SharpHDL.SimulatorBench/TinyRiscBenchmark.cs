using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using SharpHDL;

/// <summary>
/// Measures identical TinyRisc clock schedules on the reference interpreter and
/// compiled simulator. Construction, warm-up, observation and serialization are
/// outside the timed loops. The companion C++ driver uses the same workload.
/// </summary>
internal static class TinyRiscBenchmark
{
    const int WarmupEdges = 100;
    const int ResetPeriod = 128;
    const int QualificationEdges = 256;
    const string Workload = "tinyrisc-hello-reset128-v1";
    static readonly string[] OutputNames = {
        "tx_valid", "tx_data", "rx_ready", "pc", "ir", "phase", "halted", "fault",
        "retired", "last_pc", "leds", "r0", "r1", "r2", "r3", "r4", "r5", "r6", "r7"
    };

    /// <summary>Run: ROM.hex, edges per trial, trial count, output directory.</summary>
    public static void Run(string[] args)
    {
        if (args.Length != 4)
            throw new ArgumentException("usage: --tinyrisc ROM.hex edges trials output-directory");
        int edges = int.Parse(args[1], CultureInfo.InvariantCulture);
        int trials = int.Parse(args[2], CultureInfo.InvariantCulture);
        if (edges < 1 || edges > 10000000 || trials < 3 || trials > 15)
            throw new ArgumentOutOfRangeException(nameof(args), "Use 1..10000000 edges and 3..15 trials.");
        string outputDirectory = Path.GetFullPath(args[3]);
        Directory.CreateDirectory(outputDirectory);
        var rom = File.ReadAllLines(args[0]).Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => Convert.ToUInt16(line.Trim(), 16)).ToArray();
        var module = TinyRisc.Build(rom, "BenchmarkTinyRisc");
        string rtlPath = Path.Combine(outputDirectory, "BenchmarkTinyRisc.v");
        File.WriteAllText(rtlPath, module.Verilog());
        var reference = module.Simulate();
        var timer = Stopwatch.StartNew();
        var compiled = new CompiledSequentialSimulator(module);
        timer.Stop();
        double compileMilliseconds = timer.Elapsed.TotalMilliseconds;
        var ports = module.Ports.ToDictionary(port => port.Name);
        var outputs = OutputNames.Select(name => ports[name]).ToArray();
        var inputValues = new Dictionary<string, int> {
            ["run"] = 1, ["step"] = 0, ["switches"] = 0,
            ["rx_valid"] = 0, ["rx_data"] = 0, ["tx_ready"] = 1
        };

        // Reset retains RAM in both engines. Hello World initializes the only cell
        // it reads; the warm-up completes that initialization before every trial.
        void Prepare(bool jit)
        {
            foreach (var pair in inputValues)
                if (jit) compiled.Set(ports[pair.Key], pair.Value);
                else reference.Set(ports[pair.Key], pair.Value);
            if (jit)
            {
                compiled.Tick(true);
                for (int i = 0; i < WarmupEdges; i++) compiled.Tick();
            }
            else
            {
                reference.Tick(true);
                for (int i = 0; i < WarmupEdges; i++) reference.Tick();
            }
        }

        Dictionary<string, ulong> ReadState(bool jit)
        {
            var state = new Dictionary<string, ulong>();
            foreach (var port in outputs)
                state.Add(port.Name, (ulong)(jit ? compiled.Read(port) : reference.Read(port)));
            return state;
        }

        void RequireEqual(Dictionary<string, ulong> left, Dictionary<string, ulong> right)
        {
            foreach (var name in OutputNames)
                if (left[name] != right[name]) throw new InvalidOperationException("CPU mismatch: " + name);
            if (left["fault"] != 0) throw new InvalidOperationException("CPU fault during benchmark.");
        }

        // Check every observed port on a two-period trajectory before timing.
        // Hash order is explicit and shared with the Verilator driver; this is a
        // deterministic rolling fingerprint, not a cryptographic equivalence proof.
        Prepare(false);
        Prepare(true);
        ulong traceHash = 14695981039346656037UL;
        for (int edge = 0; edge < QualificationEdges; edge++)
        {
            bool reset = edge % ResetPeriod == 0;
            reference.Tick(reset);
            compiled.Tick(reset);
            var state = ReadState(false);
            RequireEqual(state, ReadState(true));
            foreach (var name in OutputNames)
                traceHash = unchecked((traceHash ^ state[name]) * 1099511628211UL);
        }

        var referenceSeconds = new double[trials];
        var compiledSeconds = new double[trials];
        Dictionary<string, ulong>? finalState = null;
        double Measure(bool jit)
        {
            Prepare(jit);
            var watch = Stopwatch.StartNew();
            if (jit)
                for (int i = 0; i < edges; i++) compiled.Tick(i % ResetPeriod == 0);
            else
                for (int i = 0; i < edges; i++) reference.Tick(i % ResetPeriod == 0);
            watch.Stop();
            if (watch.ElapsedTicks == 0) throw new InvalidOperationException("Timing resolution insufficient.");
            return watch.Elapsed.TotalSeconds;
        }

        for (int trial = 0; trial < trials; trial++)
        {
            // Alternate managed-engine order to reduce a fixed ordering bias.
            if ((trial & 1) == 0)
            {
                referenceSeconds[trial] = Measure(false);
                compiledSeconds[trial] = Measure(true);
            }
            else
            {
                compiledSeconds[trial] = Measure(true);
                referenceSeconds[trial] = Measure(false);
            }
            var state = ReadState(false);
            RequireEqual(state, ReadState(true));
            if (finalState != null) RequireEqual(finalState, state);
            finalState = state;
        }
        var report = new {
            schema_version = 1, workload = Workload, edges_per_trial = edges, trials,
            warmup_edges = WarmupEdges, reset_period = ResetPeriod,
            qualification_edges = QualificationEdges,
            qualification_hash = traceHash.ToString("x16", CultureInfo.InvariantCulture),
            rom_sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[0]))).ToLowerInvariant(),
            rtl_sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(rtlPath))).ToLowerInvariant(),
            runtime = RuntimeInformation.FrameworkDescription,
            operating_system = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            tiered_compilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "runtime-default",
            stopwatch_frequency = Stopwatch.Frequency,
            compile_ms = compileMilliseconds,
            engines = new Dictionary<string, object> {
                ["interpreter"] = new { seconds = referenceSeconds },
                ["compiled_dotnet"] = new { seconds = compiledSeconds }
            },
            final_state = finalState
        };
        File.WriteAllText(Path.Combine(outputDirectory, "dotnet.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS matched .NET workload: {trials} x {edges} edges, trace {report.qualification_hash}, JIT construction {compileMilliseconds:F3} ms");
    }
}
