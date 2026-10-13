# Generated output audit

The semantic-name change affects every emitted intermediate wire. The audit
rebuilt all example families, their RTL and synthesis products, both HTML labs,
the classroom FPGA image, and the workbook. Simulation checks passed.

| Area | Result |
|---|---|
| Core C# | 516,474 assertions; 204 RTL benches |
| F# | 8,597 assertions; eight RTL benches and AXI wrapper compilation |
| C#, F#, VB examples | 3,002 assertions; seven RTL benches; AddTwo mapped trace |
| Behavioral CIL | 70,360 assertions; 70 RTL benches; six mapped traces |
| Advanced CIL | 7,917 assertions; 36 RTL benches |
| Learning | 8,970 assertions; two RTL benches; 11,139 graph comparisons |
| TinyRisc | 4,304 checks; 11 RTL benches; synthesized trace and real browser WASM |
| Naming | Collisions, sanitization, deterministic output; 256-input RTL wrap/slice checks |
| Go Board | Shell, lesson prediction and rejection, synthesis, place/route, bitstream |
| Workbook | 25 rendered pages; 24 unchanged pages pixel-identical; new naming page reviewed |

The workbook and compiler walkthrough explain explicit names, operation names,
and CIL provenance. Actual-output excerpts in the compiler walkthrough and
three-language guide match current RTL. Both HTML labs embed refreshed output;
the TinyRisc viewer has been retested with actual Icarus WebAssembly. The other
three external VeriSim links have payload/local-Icarus checks, not a hosted-UI
end-to-end test. Old version-specific logs remain historical evidence.

The Go Board image still uses 230 logic cells. Its refreshed final timing
estimate is 154.63 MHz, passing the 25 MHz constraint. Naming can change tool
ordering and placement; unchanged logic does not imply identical physical
routing or timing estimates. No physical hardware test is claimed.

## Reproduction

Set `DOTNET_ROOT` to .NET 8, `YOSYS_DATDIR` to the Yosys share directory,
`CHROME_BIN` to Chromium, and optionally `VERISIM_ENGINE_DIR` to the verified
engine downloads from `tools/fetch_verisim_runtime.py`. Put Icarus, Yosys,
nextpnr-ice40 and icepack on PATH. Python needs python-docx for the workbook;
Node needs Playwright for browser checks.

Run `bash tests/run-all.sh`. It rebuilds in dependency order and captures logs
under `build/audit`. The complete wrapper passed in one invocation, including
the actual browser/WASM stage and classroom place/route. Its successful stage
journal is `generated/audit/run-all.txt`. The rebuilt workbook package contents
match the visually reviewed version exactly. Render and inspect any future
changed DOCX before release; document rendering is separate from software tests.

The core and learning runners accept `DOTNET_ROOT` for direct SDK compilation,
without MSBuild or Mono. The standalone naming test lives under `tests/naming`
and is excluded from the main SDK project to avoid duplicate entry points.
`tests/synthesize-examples.sh` now reproduces previously manual F# and AddTwo
synthesis outputs. Go Board builds refresh their archived reports and hashes.

The runner checks command availability, the SDK executable, Yosys simulation
cells, nextpnr HX1K support, python-docx, Playwright and the browser path
before rebuilding. Missing browser and missing HX1K support were separately
checked to fail before suite execution. `build/audit/stages.log` records
individual outcomes; a successful full run publishes `generated/audit/run-all.txt`.

## Requirements

| ID | Requirement | Check |
|---|---|---|
| REGEN-01 | Rebuild generated RTL and dependent examples after emitter changes. | Full runners; scan all generated RTL for legacy names. |
| REGEN-02 | Match actual-output documentation to emitted code. | `tests/check-generated-docs.py` checks counter, FIR and AddTwo excerpts. |
| REGEN-03 | Keep classroom image and source hashes synchronized. | Build archives its outputs; audit verifies every manifest hash. |
| REGEN-04 | Rebuild embedded HTML output and workbook content. | HTML payload/reference/browser checks; DOCX content assertion and visual review. |
| REGEN-05 | Keep each test entry point independently buildable. | Core suite, standalone naming test, SDK project exclusion. |
| REGEN-06 | Reject missing tool dependencies before rebuilding and record full-run completion only after all stages pass. | Startup checks, negative dependency checks and stage journal. |

Core counts above sum base, feature, numeric, multiply and divide assertions.
Repeated post-synthesis runs are not additional independent architectural tests.
