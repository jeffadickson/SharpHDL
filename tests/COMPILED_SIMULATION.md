# Initial compiled combinational evaluation

The first compiled-execution slice is `CompiledCombinational`. It lowers supported *pure combinational* SharpHDL expressions to cached .NET expression-tree delegates and explicitly rejects stateful paths and unsupported operations. The same module remains simulatable through the existing graph engine.

```sh
dotnet test tests/SharpHDL.UnitTests/SharpHDL.UnitTests.csproj -c Release
```

This is **not yet** a compiled cycle-accurate simulator: register commit, memory and streaming protocols remain in the existing simulator. The next step is a benchmark with identical cone, stimuli and per-sample oracle, separating delegate compilation cost, graph evaluation and compiled execution. Do not report clock cycles/sec for a combinational-only delegate.

## Initial supported operation subset

Unsigned add/subtract/multiply, bitwise operations, comparisons, constants, mux, concatenation, resize, slices, shifts, sign extension and sign-aware comparisons; unsupported memory and register accesses fail rather than producing misleading results.

Conformance requirements: identity across interpreted/compiled evaluation for all supported widths, boundary values and 64-bit-plus `BigInteger` cases. Benchmarks must record warm-up, compilation/setup, GC and runtime metadata.

No build or benchmark pass is claimed until CI executes.
