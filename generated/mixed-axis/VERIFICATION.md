# Mixed AXI verification

Command: `bash tests/run-axis-interop.sh` with .NET SDK 8.0.100 and Icarus 12.0.

Debug and optimized builds each passed:

- 262,347 native/graph/API assertions.
- Exhaustive 65,536 signed 16-bit sample encodings, checked against independent calibration/energy arithmetic and metadata expectations.
- Graph/native sample-energy extrema at widths 8, 16, 24, 32, 64.
- A 12,000-clock mixed RTL test using the actual generated skid/energy cores and handwritten calibrator.
- 5,388 accepted beats; 5,382 delivered; six explicitly discarded by reset.
- 4,476 stalled-output cycles with payload/sideband stability checks.
- Arbitrary TKEEP, frame markers and user tags; complete drain and no extra/missing outputs.
- No Icarus compilation diagnostics with `-Wall -Wno-timescale`.

The scoreboard calculates expected values independently in Verilog. Native tests
calculate expected values independently of the supplied native model. These are
functional and cycle-level simulation checks, not a formal proof, vendor timing
qualification or a physical FPGA demonstration. External declaration widths must
still be checked against actual RTL by the downstream toolchain.
