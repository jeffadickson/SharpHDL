# TinyRisc RTL explorer

Open TinyRisc_Demo.html to view the bundled native-Icarus VCD immediately.
The program counter, instruction highlight, registers, LEDs and waveform cursor
all read the same VCD; there is no separate JavaScript CPU emulator.

- Next clock and Next instruction replay the recorded execution.
- Run RTL with VeriSim engine performs actual preprocessing, compilation and
  simulation with upstream VeriSim Icarus WebAssembly in an isolated worker.
  It replaces the trace after a successful self-checking simulation.
- Open in VeriSim loads the exact generated design and teacher bench into the
  upstream application with its documented inline URL format.
- Import/download VCD, selected waveform signals, cursor and zoom are supported.
- The fixed program sums5..1, stores/loads RAM word10 and drives LED value15.
  This is not a live assembly editor or a halted remote hardware debugger.

## Run online or offline

For browser engine loading, serve this directory with a static HTTP server:

```sh
python3 -m http.server 8090 --directory docs/tinyrisc
```

Open http://localhost:8090/TinyRisc_Demo.html. The default engine loads from
https://senolgulgonul.github.io/verisim/. If you want offline simulation, first
run `python3 tools/fetch_verisim_runtime.py`, then select Use local engine.
The downloader verifies the tested engine checksums; a changed upstream asset
is rejected for review. No engine binaries are checked into this repository.
The bundled trace viewer alone can be opened directly as a local HTML file.

## Rebuild and verify

Generate CountRun with tests/run-tinyrisc.sh. Compile docs/tinyrisc/demo_tb.v
against generated/tinyrisc/CountRun.v with Icarus; run vvp with docs/tinyrisc as
working directory so dump.vcd is written there. Then:

```sh
python3 tools/build_tinyrisc_demo.py
node tests/check-tinyrisc-demo.js
node tests/check-tinyrisc-browser.js
```

Browser tests require Playwright/Chromium. CHROME_BIN can select an installed
browser; VERISIM_ENGINE_DIR can supply the six downloaded assets through test
routing. QA_DIR optionally saves screenshots for review. Verification in this
environment used locally routed, hash-recorded upstream assets because browser
network requests were blocked; it executed the actual WebAssembly engine.
83 trace/payload assertions passed; desktop/mobile rendering was inspected.
A real browser test exposed premature worker URL revocation; the URL now remains
alive until the worker terminates. Worker cancel and a60-second timeout bound
simulation lifetime. VCD input is limited to8MB; X/Z display as unknown.

## Provenance

VeriSim: https://github.com/senolgulgonul/verisim, by senolgulgonul.
The engine pipeline follows its documented ivlpp -> ivl -> vvp stages and its
MEMFS/configuration interface (index.html SHA f54c25ca69c4f959ac5c2196ba43ee7e204bf273).
Our worker orchestration and trace UI are separate code; the externally loaded
Icarus-derived engine assets retain GPLv2-or-later licensing. Upstream identifies
Icarus14 development commit c7530db and provides its source/build provenance.
See upstream README for corresponding-source information and license details.
This repository does not redistribute those binaries or claim ownership of them.

The worker and VCD parser also power the seven smaller workbook experiments in
`../interactive/SharpHDL_WASM_Lab.html`. See `../../WASM_WORKBOOK.md` for setup,
lesson predictions and native-versus-WASM waveform comparisons. TinyRisc keeps
its original required PC signal and testbench PASS marker.
