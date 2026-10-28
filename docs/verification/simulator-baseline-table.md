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
