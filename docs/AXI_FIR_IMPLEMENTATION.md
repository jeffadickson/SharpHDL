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
