# Run workbook circuits with WebAssembly

The workbook companion `docs/interactive/SharpHDL_WASM_Lab.html` offers seven experiments using
SharpHDL-generated designs using VeriSim's Icarus WebAssembly engine. Each
experiment includes the original SharpHDL source file, emitted Verilog, a
prepared self-checking testbench, a simulation log and a waveform viewer.

Start by predicting the answer. Use the recorded trace to learn the controls,
then press **Run RTL with WebAssembly** to compile and simulate the real RTL.
A successful run replaces the trace with the browser engine's VCD. First/Next/
Last event and the waveform cursor replay that completed trace; they do not
pause an executing simulator. No separate JavaScript model calculates results.

| Workbook connection | Experiment | Prediction | Signals to watch |
|---|---|---|---|
| Lessons 3–4 | Add two and wrap | 0 → 2; 7 → 9; 254 → 0 with eight-bit wrap | `s_b`, `s_a`; `clk` remains low |
| Lessons 5–6 | Count, pause, resume | 0, 1, 2, 3, 3, 3, 4 after successive rising edges | `clk`, `rst`, `s_enable`, `s_value` |
| Clock lab | Coach the hockey drill | Start enters Running with zero ticks; four following enabled edges reach Done | `s_start`, `s_ticks`, `s_phase`, `s_done` |
| Loop extension | Twelve additions, no clocks | 2 + 12 × 3 = 38 without clock edges | `s_a`, `s_b`, `s_y`, `s_fault` |
| Stream bonus | Direct connection | Producer holds 10 while receiver pauses; then delivers 10, 11, 12 | input ready/data, accepted/delivered, sent/received |
| Stream bonus | Skid buffer | Accept 10 and 11 into two slots; hold 12 until room returns | input ready, output valid/data/last, sent/received |
| Clocked-loop extension | A loop that takes clock ticks | 10 + 3 + 2 + 1 = 16 when Done is high | `s_busy`, `s_done`, `s_result`, `s_fault`, `s_timeout` |

Waveform bus labels and the value table use unsigned decimal and binary.
Unknown/undriven X/Z values remain unknown. Time is displayed in nanoseconds when the VCD timescale is recognized; the
prepared delays separate stimuli, not predict FPGA propagation delay. For the
counter, inspect rising edges at 2, 6, 10, 14, 18, 22 and 26 ns. The trace also
contains falling edges: use **Next rising edge** for clocked lessons. **Next event**
remains available for individual signal changes. Edge counts distinguish all clock
edges from those outside reset. Stream lessons show the input/output transfers
sampled immediately before the last edge; the value table shows settled values
at the cursor. The combinational lessons have no rising edges.

## Teacher setup

From the repository root:

```sh
python3 -m http.server 8090 --directory docs
```

Open [the workbook lab](http://localhost:8090/interactive/SharpHDL_WASM_Lab.html).
The workbook's clickable experiment links use this address, with `#addtwo`,
`#counter`, `#fsm`, `#forloop`, `#sequential`, `#direct` or `#skid` to select a lesson. Run the server
on the computer opening those links. On a tablet accessing a teacher's server,
replace localhost with the teacher computer's address.

The default engine loads from VeriSim's host. To prepare offline operation:

```sh
python3 tools/fetch_verisim_runtime.py
```

Then select **Use downloaded engine**. The download verifies the recorded
SHA-256 checksums and needs network access once. Keep the `docs/interactive`
and `docs/tinyrisc/verisim` directory relationship. A standalone copied HTML
file can display its bundled traces offline, but fresh worker/WASM execution
should use HTTP hosting. Load failures are displayed; cancellation and a
60-second limit stop a worker. Selecting another lesson also stops the old run.

The existing Clock Lab remains a separate interactive graph simulator. Its
new link opens this real-RTL lab. **Open in VeriSim** is an additional way to
send the same exact RTL and bench to the upstream application in a new tab;
it does not embed that application's UI in an iframe.

For a larger exercise, open `docs/tinyrisc/TinyRisc_Demo.html`. Its PC,
registers, instruction highlight and LEDs follow a recorded or freshly
simulated execution. The program sums 5 through 1, producing LED value 15.

## Rebuild and verify

After regenerating core, three-language, behavioral, advanced and learning
examples, run:

```sh
python3 tools/build_verisim.py
python3 tools/build_wasm_lab.py
python3 tools/build_learning_html.py
node tests/check-wasm-workbook.js
node tests/check-wasm-workbook.js --browser
python3 tests/check-verisim.py
```

Native trace generation needs Icarus. Browser tests need Playwright/Chromium;
`CHROME_BIN` selects an existing installation. Prepare the verified engine
files above for offline browser testing, or set `VERISIM_ENGINE_DIR` to their
location. Browser tests execute all seven WASM experiments and compare each
selected signal at the union of native/browser event times, not just final
answers. They also test cursor controls, cancellation and mobile layout.
The full `tests/run-all.sh` includes these checks. Workbook rebuilding remains
`python3 tools/create_classroom_proposal.py docs/SharpHDL_Classroom_Proposal.docx`;
render and visually inspect changed pages before release.

## Requirements

| ID | Requirement | Verification |
|---|---|---|
| WASM-01 | Use actual generated RTL with the VeriSim Icarus WASM engine. | Seven real browser runs and native trace comparisons. |
| WASM-02 | Match exercises to workbook predictions and named signals. | Fixture assertions, initial/wrap cases, counter hold timing and workbook links. |
| WASM-03 | Keep recorded replay distinct from fresh simulation and scheduling. | UI labels, workbook explanation and documentation. |
| WASM-04 | Show bus values, clock/control waves and honest X/Z values. | VCD parser, waveform/value display and cursor checks. |
| WASM-05 | Support bounded/cancellable execution and offline engine setup. | Worker timeout, cancel test and locally served verified engines. |
| WASM-06 | Refresh embedded source, RTL, benches and traces together. | Builders, exact upstream-link payload checks and full runner integration. |

Engine provenance and licensing are documented in `docs/tinyrisc/README.md`.
The lab reuses the existing worker and VCD parser; parser callers can now
specify a required signal, with TinyRisc's original requirement still the default.
Engine binaries are not redistributed in this repository. VeriSim and this
integration are separate from the VeloSim project.

For the seven-edge backpressure comparison, alternative elastic register, and
BP-01 through BP-05 requirements, see [the stream guide](SKID_BUFFER_AND_VERILOG_COMMENTS.md#compare-buffering-and-backpressure-in-the-browser).

Native and WASM runs share Icarus ancestry and the same RTL/bench. Their agreement
checks integration and trace handling, not independent proof of hardware correctness.
Independent expected-value and stream-queue scoreboards provide functional checks.

| Additional teaching requirement | Verification |
|---|---|
| EDU-WAVE-01: Offer rising-edge navigation, ns labels and reset-aware counts. | Timescale helper tests, combinational disabled state and every backpressure edge. |
| EDU-WAVE-02: Separate sampled transfers from settled values. | Seven direct/skid transfer summaries checked against independent token expectations. |

Pre-edge summaries apply to these prepared benches, whose inputs settle before
the edge. They use the value at the preceding VCD time tick; this is not a general
Verilog event-region debugger or an arbitrary trace-upload facility.

For a child-directed first session, use the [learner-led pilot guide](docs/LEARNER_LED_PILOT.md).
It includes an explicitly simulated focus-group critique, feasible learner choices,
and an observation sheet for an actual nine-year-old pilot. No child trial is claimed.
