# TinyRisc16 mapped and routed baseline

Existing CountRun CPU, with every architectural register observable through a
16-bit debug mux. Target: **iCE40 HX8K CT256**, 25 MHz, placement seeds 1, 2, 3.
This is a different device from the HX1K packet test; timing is not directly comparable.

| LUT4 | Flip-flops | Carry cells | Block RAM | Routed core Fmax range |
|---:|---:|---:|---:|---:|
| 916 | 181 | 104 | 1 | 45.45–47.13 MHz |

The established CountRun cycle trace passes on the mapped CPU. The mapped wrapper
also matches the source CPU over 1,000 cycles, probing all 16 debug selectors each
cycle under reset, run, step and peripheral controls. The full TinyRisc architecture
regression is a prerequisite of the runner. This is sampled simulation evidence,
not a formal equivalence proof.

TinyRisc uses explicit structural construction and does not call the CIL feedback
scheduler. Therefore the accepted-state optimization does not change this CPU;
these numbers establish a larger-design baseline, not a claimed CPU improvement.
The debug mux is included in the resource counts. The counting ROM image is fixed;
results do not establish costs for every program image.

Pins are assigned automatically, with no board-specific pin or external I/O timing
constraints. Fmax is a routed register-path estimate, not physical FPGA operation.
It is not instructions/second: instruction execution and stalls take multiple clocks.

Reproduce with `bash tests/run-tinyrisc-benchmark.sh` and the same environment as
the packet benchmark. Tool versions and hashes are in `results.json`.
