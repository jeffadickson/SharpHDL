# WP-09: controlled-input simulator baselines

The requested baseline slice is complete. No simulator algorithm optimization or
before/after speedup is claimed. BenchmarkDotNet 0.15.8 is the existing dependency;
its generated worker, memory diagnoser and raw JSON supply reproducible measurement
infrastructure. BCL expression compilation is deferred: Native AOT constraints and
hardware semantics need independent qualification before choosing that approach.
[BenchmarkDotNet toolchain documentation](https://benchmarkdotnet.org/articles/configs/toolchains.html)
and [configuration API](https://benchmarkdotnet.org/api/BenchmarkDotNet.Configs.ManualConfig.html)
were checked before selecting the pinned Native AOT toolchain and serialized builds.

The workload constructs a fresh machine, executes 256 edges and observes three
ports per edge, including the independent four-edge protocol checksum and final
word 64. Hardware compilation is outside timing; construction, observations and
checksum validation are inside. Address widths 1 and 8 mean two and 256 RAM words.
Both existing observation APIs must return checksum 8969665363863638565. Four
positive/negative tests pass against the actual workload assembly; matching only
the final word cannot hide corrupt ready/valid history. Shared workload coverage
is 100% line / 100% branch; this is not whole-simulator or native-code coverage.

The workload library is separate from the reflective ScheduledBench launcher.
The first monolithic attempt produced only four JIT measurements and failed AOT
project discovery after an in-checkout documentation copy appeared. A second
monolithic build was explicitly stopped after expensive native compilation; it is
not a measurement. The final library worker publishes successfully (39.94 seconds
for the recorded publish step), and all eight JIT/AOT measurements complete with
zero process failures. The CLI now rejects missing/incomplete measurements.
Third-party native trimming/single-file warnings remain visible; the measured
worker does not qualify the general benchmark launcher for Native AOT deployment.

Final run: /tmp/sharphdl-baseline-OPyHjp, log
build/verification/wp09-baselines-isolated-final.log. Linux Ubuntu 24.04.5,
SDK 10.0.402; both runtimes 10.0.13, ILCompiler explicitly pinned 10.0.13,
Speed/native instruction set, workstation GC, processor budget two, one launch,
two warmups, three measured iterations. No other SharpHDL verification job ran
during this final measurement. Other host work remained outside our control.
These short shared-host timings have large statistical errors and are a baseline,
not a reliable performance bound or evidence of an optimization. Allocations are
managed bytes per 256-edge operation. AOT and JIT results stay separate.

[Full BenchmarkDotNet JSON](verification/simulator-baseline-full.json) retains
statistics, environment and all measurements; [generated table](verification/simulator-baseline-table.md)
includes errors/standard deviations. Raw working-byte [source hashes](verification/simulator-baseline-source-hashes.txt)
are accompanied locally by base commit da2689b, relevant source patch and complete
untracked workload/library source copies. Checkout line-ending conventions can
change working-byte hashes without changing C# semantics.

Focused Stryker on the final workload library: 41 killed, two timeout-detected
infinite-loop variants, two survivors, zero uncovered/errors: 95.56% of 45 selected
variants; six compile-invalid and 27 filtered variants excluded. Survivors swap the
output-equivalent API selection and alter generic exception text. The score is
not adjusted. Negative tests added after the first run kill the weakened oracle
and missing-throw variants. Raw report: build/verification/wp09-isolated-mutation;
checked config: tests/SharpHDL.BenchmarkTests/stryker-config.json.

From a checked repository directory run `bash tools/run-simulator-baselines-linux.sh`
after local verification jobs finish. It retains a new native output directory,
source hashes, relevant patch, SDK info and reports. Fixed inputs and equal-output
checks must precede any future optimization or performance comparison.

## Recorded measurements

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Core i5-10505 CPU 3.20GHz (Max: 3.19GHz), 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.402
  [Host]           : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3
  AOT10-ILC10.0.13 : .NET 10.0.13, X64 NativeAOT x86-64-v3
  JIT10            : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3

Server=False  IterationCount=3  LaunchCount=1  
WarmupCount=2  

```
| Method              | Job              | Runtime        | AddressWidth | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0      | Gen1    | Allocated | Alloc Ratio |
|-------------------- |----------------- |--------------- |------------- |----------:|----------:|----------:|------:|--------:|----------:|--------:|----------:|------------:|
| **RepeatedOutputReads** | **AOT10-ILC10.0.13** | **NativeAOT 10.0** | **1**            |  **9.950 ms** | **26.891 ms** | **1.4740 ms** |  **1.01** |    **0.18** | **1640.6250** | **31.2500** |   **9.88 MB** |        **1.00** |
| SnapshotOutputReads | AOT10-ILC10.0.13 | NativeAOT 10.0 | 1            |  6.809 ms | 12.590 ms | 0.6901 ms |  0.69 |    0.10 |  890.6250 | 15.6250 |   5.42 MB |        0.55 |
|                     |                  |                |              |           |           |           |       |         |           |         |           |             |
| RepeatedOutputReads | JIT10            | .NET 10.0      | 1            | 11.636 ms | 17.591 ms | 0.9642 ms |  1.00 |    0.10 | 1640.6250 | 31.2500 |   9.86 MB |        1.00 |
| SnapshotOutputReads | JIT10            | .NET 10.0      | 1            |  4.690 ms |  8.483 ms | 0.4650 ms |  0.40 |    0.04 |  890.6250 | 15.6250 |   5.41 MB |        0.55 |
|                     |                  |                |              |           |           |           |       |         |           |         |           |             |
| **RepeatedOutputReads** | **AOT10-ILC10.0.13** | **NativeAOT 10.0** | **8**            |  **9.420 ms** | **25.770 ms** | **1.4125 ms** |  **1.01** |    **0.18** | **1812.5000** | **78.1250** |  **10.87 MB** |        **1.00** |
| SnapshotOutputReads | AOT10-ILC10.0.13 | NativeAOT 10.0 | 8            |  5.335 ms |  5.950 ms | 0.3261 ms |  0.57 |    0.08 | 1070.3125 | 54.6875 |   6.41 MB |        0.59 |
|                     |                  |                |              |           |           |           |       |         |           |         |           |             |
| RepeatedOutputReads | JIT10            | .NET 10.0      | 8            |  7.092 ms |  7.249 ms | 0.3974 ms |  1.00 |    0.07 | 1812.5000 | 78.1250 |  10.85 MB |        1.00 |
| SnapshotOutputReads | JIT10            | .NET 10.0      | 8            |  7.644 ms | 20.602 ms | 1.1293 ms |  1.08 |    0.15 | 1062.5000 | 54.6875 |    6.4 MB |        0.59 |

