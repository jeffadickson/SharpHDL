# Compiled simulator: executed verification and measured performance

Verified October 14, 2026, Pacific time (October 15 UTC in GitHub Actions logs).

## Result

The scoped two-state compiled simulator milestone in issue #2 is verified: the .NET JIT backend executes register, memory and streaming designs; agrees with independent oracles, the reference interpreter and generated RTL on the acceptance fixtures; runs TinyRisc16 Hello World; and has measured throughput. This is not complete RHDL parity, a four-state HDL simulator, a completed Core/HLS/Simulation extraction, or a production FPGA qualification.

**Final acceptance source:** `5c5fea0d7db7f0d3645055bfd4e18bcea8942581`.
**Successful workflow:** [compiled simulator / run 38676806154](https://github.com/jeffadickson/SharpHDL/actions/runs/38676806154).
**Evidence artifact:** [compiled-simulator-evidence, 11945729375](https://github.com/jeffadickson/SharpHDL/actions/runs/38676806154/artifacts/11945729375).
**Artifact ZIP SHA-256:** `94a931b1b4101b7008d6a4eca8df69f29c8be30683c9a6e4cc1b3bdf50a1a747`.

The artifact was downloaded and inspected, including raw JSON timing samples, TRX counters, conformance logs and CPU/UART results. A successful workflow alone is not substituted for those contents.

## Executed acceptance

| Gate | Observed result | Evidence |
|---|---|---|
| Linux xUnit suite | 60 executed, 60 passed, zero failed/skipped | Final run `compiled-simulator.trx` |
| Windows xUnit suite | 60 executed, 60 passed, zero failed/skipped | [run 38675599579](https://github.com/jeffadickson/SharpHDL/actions/runs/38675599579), source `0082cc000356e7b28cfe00788ea9e88369bd98df`; same simulator implementation, before later benchmark-only additions |
| Accumulator | Independent integer oracle + interpreter + JIT + Icarus; 241 edges | `conformance-oracles.log`, `conformance-rtl.log` |
| Four-state-count FSM (two-bit encoded, not X/Z logic) | Independent integer state oracle + interpreter + JIT + Icarus; 241 edges | Same conformance logs |
| Synchronous RAM | Independent software array + interpreter + JIT + Icarus; 321 edges | Old-data collisions, read enable, invalid address, retained RAM across reset |
| Streaming pipeline | Independent queue/arithmetic scoreboard + interpreter + JIT + Icarus; 253 edges | Backpressure, stable offered input, reset and drain |
| TinyRisc16 regression | 4,851 independent checks, 290 retired instructions, 853 traced edges, 12 RTL benches | `generated/tinyrisc/verification.txt`; actual Icarus execution in run logs |
| Compiled TinyRisc Hello World | Full output-port agreement per edge under repeatable UART stalls; exact 15-byte message, HALT without fault | Standard `tests/run-tinyrisc.sh` compiled gate |
| Serial UART | Independent 8N1 decoder passed under Icarus and Verilator | `backend-benchmark/verilator-uart.log` and workflow output |
| CPU + UART Yosys structural check | Success | [run 38676328510](https://github.com/jeffadickson/SharpHDL/actions/runs/38676328510), source `c734461e0b9fef29576ca7707c21c9cfebd89df2`; no FPGA place/route or physical board claim |
| Shared live rewind API | Step/back/replay/reset API test passed | [run 38675514780](https://github.com/jeffadickson/SharpHDL/actions/runs/38675514780), source `d8ff6c419b3df84208bb1fa0b068422f4f75e26d`; this host uses the reference simulator, not a verified VS Code desktop session |

The 60 unit cases include shared-DAG fanout, arithmetic and signed/shift/masking tests at widths 1, 3, 8, 17, 31, 32, 33, 63, 64, 65, 127 and 257; atomic register behavior; register-free graphs; multiple RAMs; CIL-generated pipeline backpressure; checkpoint and breakpoint tests. Test counts are not exhaustive semantic or formal verification.

## Matched TinyRisc benchmark

Workload: `tinyrisc-hello-reset128-v1`, **five trials of 100,000 timed rising edges per engine**, after 100 warm-up edges. Reset is asserted every 128 timed edges; run and TX-ready stay high, RX-valid stays low. This repeats active ROM execution but also includes reset and halted cycles. These are simulated clock edges, not retired instructions or FPGA operating frequency.

| Engine | Median simulated edges/s | Median time per 100,000 edges |
|---|---:|---:|
| SharpHDL reference interpreter | 22,929 | 4.3612477 s |
| SharpHDL compiled .NET | 766,166 | 0.1305200 s |
| Verilator native C++ | 7,382,178 | 0.013546138 s |

**Compiled .NET / interpreter: 33.414x. Verilator / compiled .NET: 9.635x.** JIT simulator construction was **47.8625 ms**, measured separately. The preceding matched CI run observed 32.424x and 9.218x respectively; report workload-specific results, not a fixed product-wide speed guarantee. The earlier single-run 44.178x figure used a shorter, differently warmed harness and is not the final qualified comparison.

Toolchain: .NET SDK 8.0.426, runtime 8.0.32, Linux x86-64, Verilator 5.020, GCC 13.3.0. `DOTNET_TieredCompilation=0` is set for the matched managed benchmark to keep tier promotion outside the comparison. The microbenchmark uses its separately recorded default runtime behavior.

The timing loops exclude construction, warm-up, output inspection, correctness assertions, process startup, serialization and tracing. Verilator uses its normal clock-low/rising/clock-low `eval()` calls; that settling overhead remains part of its timed execution. Native durations come from `steady_clock`, not `/usr/bin/time` rounded to 0.00 s. Managed engine order alternates between trials. Native runs occur in a separate process on the same CI runner; this is not a controlled cross-machine performance study.

Before timing, both managed engines compare all 19 visible CPU output ports on a 256-edge two-period trajectory. Verilator produces the same rolling trace fingerprint, `f15dd42a4544d121`. After each timing repetition, all visible output ports agree and no fault is asserted. The fingerprint is not a formal proof and every timed edge is not traced; the separately executed conformance and Hello World tests provide directed per-edge comparisons.

Raw samples and metadata are retained in [verification/simulator/2026-10-14-results.json](verification/simulator/2026-10-14-results.json). The qualified report rejects different workloads, counts, traces, final states, missing timings and altered exported RTL. All three timing paths are required; a missing Verilator fails qualification rather than silently skipping it.

## What changed

The first executable run exposed a Portable PDB build error (`Handle` versus `EntityHandle`) and a stale typed-channel `_tready` test lookup. Both were corrected, preserving real typed port references in the test.

Actual timing then exposed a bigger compiler flaw: the previous fused expression tree duplicated shared hardware nodes. On TinyRisc it produced only 1,840 edges/s versus 19,005 for the interpreter, and construction took 1,741.66 ms. Merely putting every register expression into one delegate was not enough.

The replacement compiler schedules each unique DAG node once into a local variable, aliases wires to their drivers, shares intermediates between register roots, and keeps 1..32-bit operations in `uint` and 33..64-bit operations in `ulong`. Wider operations use `BigInteger`. The graph architecture and generated hardware are unchanged. See [COMPILED_SIMULATOR_DESIGN.md](COMPILED_SIMULATOR_DESIGN.md).

## Reproduce

With the provisioned Docker image and the existing Make prerequisites:

```sh
make setup
make benchmark-tinyrisc
```

On a native Linux toolchain, set `DOTNET_ROOT` to the .NET 8 SDK root and run:

```sh
bash tests/benchmark-tinyrisc-backends.sh
```

Optional environment variables `SHARPHDL_BENCH_EDGES` and `SHARPHDL_BENCH_TRIALS` control counts; the report preserves their realized values. The runner requires Icarus, Verilator, Python, a C++ compiler and Make, and runs correctness gates before reporting timings. This record verifies GitHub-hosted execution, not a fresh Docker installation on every operating system.

## Remaining boundaries

Two-state, single-clock, synchronous-reset semantics only. Registers must be reset before compiled state-dependent evaluation. No X/Z resolution, generic event/delta-cycle engine, multi-clock scheduling, full source breakpoints, physical hardware validation or formal equivalence claim. The compiled backend is still in the compatibility assembly; moving it to `SharpHDL.Simulation` remains issue #24. Reference-debugger checkpoint tests do not establish compiled-backend checkpoint integration. RHDL was not executed in this benchmark; no speed claim versus RHDL is made.
