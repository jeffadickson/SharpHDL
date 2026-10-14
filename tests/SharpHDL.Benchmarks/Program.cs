using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using SharpHDL;

const int iterations = 100000;
const int repetitions = 7;
const int seed = 104729;
var m = new Module("CompiledBenchmark");
var a = m.Input("a", 16);
var b = m.Input("b", 16);
var select = m.Input("select", 1);
var y = m.Output("y", 16);
m.Drive(y, Expr.Mux(select, (a + b) * b, (a ^ b) + a));
var simulator = m.Simulate();
var compilation = Stopwatch.StartNew();
var compiled = new CompiledCombinational(m, y);
compilation.Stop();
var samples = new (BigInteger A, BigInteger B, BigInteger S)[iterations];
uint state = seed;
for (int i = 0; i < iterations; i++)
{
    state = unchecked(1664525u * state + 1013904223u);
    var av = state & 65535u;
    state = unchecked(1664525u * state + 1013904223u);
    var bv = state & 65535u;
    samples[i] = (av, bv, (uint)(i & 1));
}
BigInteger EvalGraph()
{
    BigInteger checksum = 0;
    foreach (var (av, bv, sv) in samples)
    {
        simulator.Set(a, av); simulator.Set(b, bv); simulator.Set(select, sv);
        checksum += simulator.Read(y);
    }
    return checksum;
}
BigInteger EvalCompiled()
{
    BigInteger checksum = 0;
    foreach (var (av, bv, sv) in samples)
        checksum += compiled.Evaluate(av, bv, sv);
    return checksum;
}
var oracle = EvalGraph();
if (EvalCompiled() != oracle) throw new InvalidOperationException("Compiled output diverges");
var graph = new double[repetitions];
var native = new double[repetitions];
for (int i = 0; i < repetitions; i++)
{
    var stopwatch = Stopwatch.StartNew();
    if (EvalGraph() != oracle) throw new InvalidOperationException("Graph checksum divergence");
    stopwatch.Stop(); graph[i] = stopwatch.Elapsed.TotalSeconds;
    stopwatch.Restart();
    if (EvalCompiled() != oracle) throw new InvalidOperationException("Compiled checksum divergence");
    stopwatch.Stop(); native[i] = stopwatch.Elapsed.TotalSeconds;
}
static double Median(double[] samples) { var c = (double[])samples.Clone(); Array.Sort(c); return c[c.Length / 2]; }
var result = new
{
    Fixture = "16-bit combinational conditional multiply/add/xor",
    Samples = iterations, Repetitions = repetitions, Seed = seed,
    CompileSeconds = compilation.Elapsed.TotalSeconds,
    GraphSeconds = graph, CompiledSeconds = native,
    GraphMedianEvaluationsPerSecond = iterations / Median(graph),
    CompiledMedianEvaluationsPerSecond = iterations / Median(native),
    RelativeSpeedup = Median(graph) / Median(native),
    Runtime = RuntimeInformation.FrameworkDescription,
    OS = RuntimeInformation.OSDescription,
    Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    CorrectnessPassed = true,
    Limitations = "Combinational evaluation only; not cycle-accurate simulation throughput. Input validation and host calls included."
};
Directory.CreateDirectory("generated");
File.WriteAllText("generated/compiled-combinational-benchmark.json",
    JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PASS {iterations} parity-checked samples; compiled/graph median speedup {result.RelativeSpeedup:F2}x");
