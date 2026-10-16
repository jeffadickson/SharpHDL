# AXI streaming FIR integration — implementation contract

## Reuse existing components

- `Stream.Input` and `Stream.Output` for AXI4-Stream interfaces.
- `AxiLitePort.Slave` and `AxiLiteRegisterBank` for optional configuration.
- `SynchronousRam.Create` for inferred on-chip memory.
- `HwStream<T>` and `CilCompiler.AttachProcess` for explicit streaming actors.

Do **not** claim the current CIL process compiler can access RAM arrays:
`STREAM_PROCESSES.md` explicitly excludes RAM accesses from process bodies.
The initial FIR must use a structural datapath or extend that compiler.

## Four-tap filter behavior

- Input and output: 16-bit unsigned AXI4-Stream sample words.
- Coefficients: four unsigned 16-bit registers; arithmetic modulo 2^16
  for the first baseline (signed/fixed-point mode requires a separate contract).
- History: four 16-bit samples, newest first, initially zero.
- On each accepted input handshake, compute the output from the new
  sample and the previous three accepted samples.
- Every accepted input produces exactly one output, in order.
- Output data and TLAST remain stable during stalls.
- No history shift or RAM write occurs when an input is offered but not accepted.
- Reset flushes in-flight transactions and clears logical history validity;
  physical RAM cells may retain data but must not leak into new outputs.
- Coefficient changes are sampled at input acceptance, not halfway through
  a transaction. This avoids mixing coefficient versions during stalls.

## Implementation stages

1. **Reference**: standalone C# array-based FIR with handshake scoreboard.
2. **Structural RTL**: implement four taps with explicit memory access
   schedule. A single-port synchronous RAM cannot read three historical
   words simultaneously: use a multicycle read sequence, banking,
   replication, or register history. Document selected II and latency.
3. **AXI wrapper**: connect stream ports and optionally AXI4-Lite CSRs.
4. **Verification**: same seeded trace through reference, SharpHDL simulator,
   and Verilog simulator; test stalls, TLAST, reset, coefficient changes.
5. **HLS**: only after the structural version passes, add RAM operations
   and resource constraints to the automatic scheduler.
6. **Physical mapping**: run Yosys resource mapping, and separately
   vendor synthesis/timing before making target Fmax claims.

## Scope warning

This file is a design/acceptance contract, **not evidence that the FIR RTL
has been implemented or that simulation has passed**.


## Implemented AXI4-Lite configuration variant (.NET 10)

`AxiFirStructural.BuildWithAxiLite()` reuses the verified four-tap
register-history datapath. The AXI4-Lite slave is named `s_axi` and
the AXI4-Stream slave/master are `s_axis` and `m_axis`.

| Byte offset | Register | Reset value | Used bits |
|---|---|---|---|
| 0x00 | coefficient0 | 1 | [15:0] |
| 0x04 | coefficient1 | 0 | [15:0] |
| 0x08 | coefficient2 | 0 | [15:0] |
| 0x0C | coefficient3 | 0 | [15:0] |

The register bank supports independent AW/W handshakes, byte strobes,
readback and backpressured B/R responses. A coefficient update takes effect
on its write-commit clock edge. Samples accepted on subsequent edges use
the updated coefficient values. An already registered output is unaffected
by later coefficient changes.

The current implementation does **not** provide atomic multi-coefficient
updates: software must quiesce the sample stream if a complete coefficient
set must change as one unit. A shadow-bank/commit mechanism is a future
extension. This distinction is important for uninterrupted DSP streams.

Test: `AxiFirStructuralTests.Axi_lite_writes_and_reads_change_fir_coefficients`
checks real bus transactions, readback, byte strobes and FIR arithmetic.
It is not evidence of AXI protocol qualification until CI confirms the test.

### Run the .NET 10 regression

```bash
dotnet test tests/SharpHDL.UnitTests/SharpHDL.UnitTests.csproj -c Release --filter "FullyQualifiedName~AxiFir"
```


## Atomic coefficient bank (double-buffered)

`AxiFirStructural.BuildWithAtomicAxiLite()` is a separate variant, so the
original direct-register API remains compatible. The AXI4-Lite map is:

| Offset | Name | Access | Reset | Meaning |
|---|---|---|---|---|
| 0x00 | staging0 | R/W | 1 | Tap 0 staging coefficient, bits 15:0 |
| 0x04 | staging1 | R/W | 0 | Tap 1 staging coefficient |
| 0x08 | staging2 | R/W | 0 | Tap 2 staging coefficient |
| 0x0C | staging3 | R/W | 0 | Tap 3 staging coefficient |
| 0x10 | apply | W/R | 0 | Any committed write with nonzero WSTRB triggers APPLY; written data ignored |
| 0x20 | active0 | R/O | 1 | Active tap 0 coefficient |
| 0x24 | active1 | R/O | 0 | Active tap 1 coefficient |
| 0x28 | active2 | R/O | 0 | Active tap 2 coefficient |
| 0x2C | active3 | R/O | 0 | Active tap 3 coefficient |

Software writes all staging words, waits for their AXI write responses,
then writes APPLY. On APPLY's write-commit edge all active taps capture
the pre-edge staging values simultaneously. An input accepted on that same
edge uses the old active bank; later accepted inputs use the new bank.
An output already in flight never changes. Partial writes to staging
honor AXI WSTRB, and a zero-strobe APPLY is ignored.

The hardware does not clear sample history on APPLY. To change coefficients
at a packet boundary, software must coordinate stream acceptance with
the APPLY operation; an explicit TLAST-triggered commit policy is not yet
implemented.

**Verification:** the atomic-register regression test covers staging
isolation, zero-strobe APPLY, readback, sample arithmetic, and reset.
The new variant is not yet included in the standalone generated-Verilog
replay job, which currently targets `Build()`.


## Single-port RAM FIR prototype

`AxiFirRam.Build()` implements a separate four-tap FIR using an inferred
16 × 16-bit synchronous RAM, a circular write pointer, and a multicycle
controller. It is deliberately not described as automatic HLS scheduling.

- **IDLE**: accept an AXI beat, write it at HEAD, capture coefficients,
  and calculate the newest-sample product.
- **ISSUE**: present a historical address to the synchronous RAM.
- **ACCUMULATE**: consume the registered RAM output and add the product
  for the selected tap. Repeat for taps 1, 2, and 3.
- **OUTPUT**: hold the result and all sidebands until AXI TREADY.

The address of historical tap `k` is `(head_after_write - k - 1) mod 16`.
The valid-history count is incremented only after output retirement;
uninitialized history is logically zero regardless of RAM cell contents.
This distinction is required for reset and initial startup correctness.

The design captures all coefficients at input acceptance, so changing
external coefficient ports during the multicycle operation cannot mix
coefficient sets. It accepts at most one sample every eight clock cycles
when the output consumer is continuously ready; downstream stalls increase
that interval. It does **not** claim II=1.

`AxiFirRamTests` provides a deterministic software scoreboard, random
AXI backpressure, circular-address wraparound, and transaction counting.
The test and design must pass CI before this implementation is considered
verified. Generated-Verilog replay for this RAM variant is still pending.


## HLS cycle-aware scheduling milestone

The independent `SharpHDL.HLS.HlsSinglePortFirPlanner` now produces a
resource-constrained schedule for 1–256 FIR taps using one synchronous RAM
port. Unlike `HlsGraphAnalyzer`'s abstract operator weights, each step
is a **real clock offset**:

| Cycle (four taps) | Action |
|---|---|
| 0 | Accept sample and write it to RAM; compute tap 0 |
| 1 | Issue RAM read for tap 1 |
| 2 | Accumulate registered tap 1 data |
| 3 | Issue RAM read for tap 2 |
| 4 | Accumulate tap 2 |
| 5 | Issue RAM read for tap 3 |
| 6 | Accumulate tap 3 |
| 7 | Offer output and wait for AXI handshake |

For N taps the no-stall initiation interval is `2*N` cycles, with
output-valid offered in cycle `2*N-1`. The planner guarantees no
same-cycle contention for the one RAM port. Output backpressure extends
the interval without losing transactions.

**Scope:** This is a validated *schedule description*, not yet a
Core IR state-machine emitter. The structural `AxiFirRam.Build()`
implements the four-tap case. The next step is to consume the schedule
and generate the controller automatically, then compare its Core IR RTL
against the verified structural reference. Timing closure and DSP
resource allocation remain separate work.
