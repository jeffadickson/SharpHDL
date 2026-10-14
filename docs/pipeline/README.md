# Pipeline Inspector — milestone 0.15 preview

A static, offline-capable browser replay tool driven by actual SharpHDL simulator observations. It does **not** implement a separate JavaScript hardware simulator.

## Generate and open

From the repository root, with .NET 8 SDK installed:

```sh
dotnet run --project tests/Tests.csproj -c Release -- --pipeline-browser
python3 -m http.server 8092 --directory docs/pipeline
```

Open http://localhost:8092/ . The first command regenerates `docs/pipeline/trace.json`, which the browser loads. Alternatively, open `index.html` directly and import a JSON trace using the file picker.

The UI provides previous/next, first cycle, timed playback, random-access cycle seek, real input/output handshake and stage-valid inspection, a selectable graph node table, and CIL-derived operation names where the scheduler provides them.

## Semantics

- `PipelineDataflowGraph` is the *compile-time* scheduling DAG. IDs, stage, abstract level, bit width and operand edges come from SharpHDL's `PipelineScheduler`.
- `PipelineTraceRecorder` observes `PipelineSignalProbe` **before** each rising edge. Signals are read from the real `Simulator`; samples are strictly increasing and schema versioned.
- A cycle marked `InputAccepted` or `OutputAccepted` means valid and ready were both asserted at the observation point, prior to the edge.
- `StageOccupied` is derived directly from exposed `StageValid` registers. It does not yet identify individual token IDs or source operands at a given stage.
- Node names are hints (including CIL method/IL offsets when supplied). Full file-and-line source mappings are not implemented.
- The browser only replays captured observations; it cannot alter stimuli, compile C#, or run the DUT live. This distinction must remain explicit.

## Verification and limitations

```sh
dotnet test tests/SharpHDL.UnitTests/SharpHDL.UnitTests.csproj -c Release
```

The fixture and schema unit test have been committed but have not been run in this environment. The HTML currently has no full browser-automation test, so UI compatibility remains unverified. Add headless browser smoke tests, token-ID tracking (without guessing), a trace file size budget, source span metadata and a local live-session transport before calling this a finished debugger.

## Related references

- [Design record](../DESIGN.md)
- [Pipeline scheduler](../../PIPELINE_SCHEDULER.md)
- [Feature parity](../FEATURE_PARITY.md)
- [Issue #23](https://github.com/jeffadickson/SharpHDL/issues/23)

## Live simulation on localhost

```sh
dotnet run --project tools/SharpHDL.Live/SharpHDL.Live.csproj -c Release
```

Open **http://127.0.0.1:8093/**. This serves the same pipeline inspector with **Connect localhost**, **Reset live**, and **Step live clock** controls. Adjust input data, input valid, and output ready before stepping. The browser sends POST requests to the actual .NET simulator, then shows live stage occupancy and accepted transaction sequence IDs. Live control does not rely on `trace.json`.

API: `GET /api/state`, `POST /api/reset`, `POST /api/step` with JSON `{"valid":true,"data":10,"ready":true}`. Only localhost is bound; do not expose this unauthenticated development API publicly.

**Current limits:** ID queue tracks overall accepted transaction order, **not** exact token placement per stage. Source hints are generated method/IL identifiers, **not** genuine source file/line mappings. The runnable service has been committed but not yet build-tested here. The inspector's replay mode still works independently of the live server.
