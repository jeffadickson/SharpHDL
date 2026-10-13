# Guided shot counter

`ShotCounter.Build(1)` makes a four-bit one-point counter;
`ShotCounter.Build(2)` makes the two-point variant. These are working templates,
not incomplete source files. First explain the expected score, then change the
argument in a teacher-owned caller. The learning suite constructs both, so the
learner can inspect tested results before making an independent variant.

Inputs `shot` and `clear` are active high. The teacher supplies synchronized,
debounced levels. On a rising clock edge a new low-to-high shot increments the
score; holding shot does not repeat. Clear has priority over a new press. If shot
stays high through clear, it must be released and pressed again before counting.
Reset clears score and button history. An already-held shot after reset counts
on the first non-reset edge. Scores wrap modulo 16.

## Teacher setup

Run from the repository root, using .NET 8 SDK, Python 3, Node.js and Icarus:

```sh
DOTNET_ROOT=/path/to/dotnet-sdk bash tests/run-learning.sh
```

Alternatively, with Mono's `mcs` and `mono` on PATH, omit `DOTNET_ROOT`.
The shared learning runner also rebuilds the workbook labs from the checked-in
core/behavioral/three-language RTL. Use the full regression runner when updating
those compiler outputs.

The default and two-point RTL/benches are emitted in `generated/learning` as
`ShotCounter` and `ShotCounterDouble`. The directed expected-value tests cover
press/hold/release, clear priority, reset and repeated presses past wrap. They
compare both the SharpHDL graph simulator and native Icarus RTL to a stated
score sequence. They are not physical-switch tests.

This template is **not connected to the Go Board image** and is not a new
interactive WASM experiment. Use cards and the native test results for this guided
extension. Board integration needs the existing synchronization/debounce shell,
port wiring, display selection and another complete board acceptance run.

## Requirements

| ID | Requirement | Check |
|---|---|---|
| EDU-SHOT-01 | Begin with a complete small design and one configurable change. | Build(1) and Build(2), workbook prediction table. |
| EDU-SHOT-02 | Count once per cleaned button press; define clear, reset and wrap. | Directed graph and RTL tests for both configurations. |
| EDU-SHOT-03 | Separate software testing from board integration. | Explicit setup and boundary statements in workbook and this guide. |
