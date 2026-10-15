# SharpHDL modular architecture and migration

## Decision (2026-10-14)

One repository, three primary assemblies:

| Assembly | Ownership | Reference rule |
|---|---|---|
| `SharpHDL.Core` | Hardware types, versioned IR contracts, validation, structural primitives and backends' common semantics | No dependency on HLS or Simulation |
| `SharpHDL.HLS` | CIL language analysis, processes, scheduling and high-level hardware lowering | References Core |
| `SharpHDL.Simulation` | Reference interpreter, compiled simulator, event-breakpoints, replay and trace engines | References Core |

Support products, including Verilog exporter, VS Code extension, live debugger and tests, remain in the same repository.

## Migration status

**The project scaffolding and dependency direction are committed; implementation migration is NOT finished.** The existing `src/SharpHDL/SharpHDL.csproj` is preserved as the compatibility assembly and still contains the historical structural, HLS, simulator and Verilog implementation. New assemblies currently contain initial contracts, not duplicate implementations. The new `HardwareNode` representation is *not yet connected* to the production compiler. No class has been claimed migrated based on creating its destination project alone.

This staged approach avoids breaking public consumers while extensive `internal` references and combined source files are untangled. The key follow-on work is a real migration with end-to-end tests:

1. Inventory ownership and internal dependencies among current source files. Keep hardware semantics explicit and invariant across existing frontends.
2. Move the foundational expressions, signals, registers, RAM model and typed IR into Core. Separate Verilog emission from simulator evaluation; preserve public signatures through compatible references or documented forwards.
3. Move CIL/process frontends, pipeline scheduler and high-level numerical kernels into HLS. Both high-level and explicit structural interfaces must produce the same canonical IR.
4. Move reference and compiled sequential engines, checkpoints, debug session, waveforms and breakpoints into Simulation. Do not duplicate simulator state or depend on HLS types.
5. Update all example/test project references and assembly usage. Add clean-clone build, binary compatibility tests, C# / F# / VB smoke tests, CPU/UART differential simulation and a dependency-cycle test.
6. Retire the old assembly only with compatibility evidence and a migration version; don't silently break existing packages or example code.

### Build

```sh
make setup
just build
make container-verify
```

The provisioning script restores the new three projects along with the legacy projects. The `just build` and container smoke workflow compile them. Docker is still the bootstrap prerequisite for automated tool provisioning. New build/test results are not verified in this session.

## Explicit non-goals

We are not creating three repositories or three competing hardware languages. HLS and the structural API remain complementary authoring styles. The simulator runs the elaborated hardware design, never the source-level CIL application as though it were ordinary software.

## 2026-10-14 Core IR implementation increment

`src/SharpHDL.Core/HardwareGraph.cs` is a real backend-neutral immutable topological graph validator. It checks nonnegative unique node IDs, earlier-node-only operand references (rejects cycles and forward references), widths 1–4096, binary/unary/mux operand signatures and explicit unsupported operation types. `CoreHardwareGraphTests` is included in the existing xUnit suite through a direct Core project reference. `tests/check-project-layers.py` enforces HLS→Core and Simulation→Core references with no unexpected cross-layer dependencies; container smoke runs this check.

**Not yet migrated:** Production `Module`/`Expr`, CIL lowering, `Simulator`, Verilog and debugger types still live in the compatibility assembly. The new graph is not yet wired to synthesis or simulator execution. Existing end-to-end tests are not known to have passed after this change. The next extraction must use a genuine shared IR adapter without duplicate hardware state or breaking consumers; binary compatibility implications require explicit review.

## 2026-10-15 — first production graph-to-Core lowering

The legacy compatibility assembly now references `SharpHDL.Core` and exposes
`Module.ExportCoreGraph()`. This performs **real lowering** of a validated
production `Module` into the immutable, width-checked Core graph for the
currently supported expression subset: input/state, constants, add/subtract,
multiply, bitwise AND/OR/XOR/NOT and mux. Combinational outputs and register
next-state cones are included; pre-edge register state is represented as an
explicit input boundary. Literal values and signal names are preserved.

`SharpHDL.Simulation.HardwareGraphEvaluator` evaluates this canonical graph
without depending on the legacy assembly or HLS frontend. Arithmetic is
two-state, unsigned and masked to the declared hardware width. A differential
test compares the new evaluator with the original simulator for 300 clock
edges, including a stateful 8-bit multiply-accumulate with enable control.
Additional tests reject unsupported operators, missing input state and
out-of-range values. The new evaluator is an independent combinational
snapshot evaluator; the original simulator remains responsible for memory,
clock scheduling and other unsupported operations.

**Migration status:** This is a supported-subset compatibility bridge, not
a claim that all legacy hardware now executes through Core. In particular,
shifts, slices, comparisons, signed operations, RAM, DSP primitives,
concatenation, pipeline control and multi-clock semantics are not yet lowered
by `ExportCoreGraph`; those fail explicitly rather than being silently
misrepresented. HLS production scheduling and legacy simulator implementations
have **not** moved to their new assemblies. The next steps are extending the
canonical operation contract and lowering to cover full TinyRisc, then moving
simulation execution and frontends without breaking the public API. Keep
issue #24 open until end-to-end parity and clean builds pass.
