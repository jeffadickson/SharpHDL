# Frame reduction verification

Test command: `bash tests/run-frame-reduction.sh`.
Toolchain: .NET SDK 8.0.100 and Icarus Verilog 12.0.

Debug and optimized builds each passed 112,057 checks and the same RTL replay:

| Measure | Result |
|---|---:|
| Cycles | 17,452 |
| Accepted input beats | 10,627 |
| Delivered frame results | 506 |
| Completed results discarded by reset | 1 |
| Partial-frame beats discarded by reset | 38 |
| Stalled-output cycles | 4,190 |
| Reset events | 4 |

The graph and native models are compared with a retained-frame BigInteger oracle.
Directed cases include all-invalid/empty frames, signed sum overflow, energy
overflow, saturated count, invalid closing samples, metadata and native reset.
Wide graph/native tests cover signed minima at 8, 24, 32 and 64 sample bits.
The 16-bit RTL replay checks cycle-level handshakes and outputs from the
oracle-validated graph trace. Final drain detects missing results. One reset is
selected while an output is pending to exercise result discard.

The replay testbench is regenerated under build/frame-reduction; the synthesizable
RTL and numerical reports are committed. Compilation used -Wall -Wno-timescale
and emitted no diagnostics. No target timing, physical FPGA execution or formal
proof is claimed. Scientific arithmetic is structural hardware, not a new CIL
compiler intrinsic.

Numerical reports separately cover all 65,536 encodings of signed 16-bit input:
exact native calibrated energy has zero error; graph fixed-point conversion from
12 to 8 fractional bits has maximum absolute error 1/512 and RMS error about
0.0011320335. Their reference values fit exactly in double. Special-case unit
tests exercise zero references, non-finite pairs, undefined empty RMS, and large
errors that would overflow a naive sum of squared errors.
