# Mixing SharpHDL and Verilog

SharpHDL can now compose generated modules and external Verilog in a single-clock,
linear AXI4-Stream pipeline. The top-level RTL wires payload and sidebands forward,
and ready backward. External logic remains Verilog: it is not translated to CIL.

## Working example

[MixedScience.cs](examples/MixedScience.cs) builds:

1. A generated two-entry skid buffer that breaks the combinational ready path.
2. A handwritten [sensor calibrator](examples/MixedAxis/calibrate.v).
3. A generated signed-sample energy stage.

```csharp
var pipeline = MixedScience.Build();
File.WriteAllText("mixed_science.v", pipeline.Verilog());
// Compile mixed_science.v plus each path in pipeline.ExternalSources.
```

The actual composition API is:

```csharp
var format = new AxisFormat(16, 8); // sample bits, metadata bits
var calibrator = AxisComponent.External(
    "sensor_calibrate", "examples/MixedAxis/calibrate.v",
    "s_axis", "m_axis", format, format, MixedScience.Calibrate)
    .Parameter("GAIN", 2)
    .Parameter("OFFSET", -10);

var pipeline = new AxisPipeline("instrument")
    .Add(AxisComponent.FromSharp(skidModule, "s_axis", "m_axis", b => b))
    .Add(calibrator)
    .Add(AxisComponent.FromSharp(energyModule, "s_axis", "m_axis",
                               MixedScience.Energy));
```

`skidModule` and `energyModule` are the structural modules constructed in the
complete example. C#, F#, or VB can construct those same `Module` objects through
the .NET API; this new mixed example itself is C# plus Verilog. Existing CIL
language tests are separate evidence, not a claim that this example compiles F#.

`FromSharp` requires exactly two stream interfaces and no extra scalar ports.
It validates the graph, checks every stream port width and direction, and emits
the original core plus its `Axi4Stream.Wrapper`. Multiple instances of an identical
module share one definition. Conflicting known definitions and top/core name
collisions are rejected. Dependencies hidden inside external RTL are resolved by
the RTL tools.

## Interface contract

| Signal | Meaning |
|---|---|
| `aclk` | Shared rising-edge clock |
| `aresetn` | Shared active-low reset; hold through a rising edge; release synchronously |
| `*_tdata` | Byte-aligned payload, lane zero in bits 7:0 |
| `*_tkeep` | One validity bit per payload byte |
| `*_tlast` | Last beat of a frame/packet |
| `*_tuser` | Application metadata, at least one bit |
| `*_tvalid` | Producer offers a beat and retains it until accepted |
| `*_tready` | Consumer accepts; flows opposite data |

The source must hold VALID, DATA, KEEP, LAST, and USER stable while stalled. It
must not wait for READY before offering VALID. Each external component must obey
its reset contract and discard pending transfers at reset. The generated wrapper
masks handshakes during reset; its core reset is synchronous. No synchronizer is
inserted. Connections have exactly matching data and metadata widths; conversion,
CDC, buffering, and protocol adaptation are explicit design work.

`AxisComponent.External` declares a port contract, not a Verilog parser. The RTL
compiler must validate the actual source. A declared width cannot prove a
parameterized external module has that width. Treat width/direction warnings as
integration failures; verify parameters and source together. Integer parameter
values are supported; string/type parameters and arbitrary Verilog expressions
are not. `ExternalSources` is a distinct list of paths supplied by the caller;
files are not read, copied, or executed during construction. Compile dependency
files and include directories explicitly when an IP block needs them.

The current API is deliberately a **linear, single-input/single-output chain**.
Each internal connection has one producer and one consumer by construction.
There is no automatic combinational-loop analysis inside opaque external RTL.
Use a registered skid boundary where needed and run RTL lint/timing checks.
Fanout, arbitrary hierarchical port binding, multi-clock designs, AXI4-Lite,
AXI memory-mapped transactions, TID/TDEST/TSTRB and optional omitted stream fields
are not yet supported by this composition API. Existing structural modules can
still implement richer networks behind a two-stream wrapper.

## Native PC execution

```csharp
using var input = HwStream<AxisBeat>.FromEnumerable(new[] {
    new AxisBeat(100, 3, true, 7) // signed 16-bit sample, full keep, frame end, channel 7
});
using var output = HwStream<AxisBeat>.ToAction(b => Console.WriteLine(b.Data));
MixedScience.Build().Run(input, output); // (2*100 - 10)^2 = 36100
```

The runner calls each supplied model once per beat. Models must implement the
same **one-beat-in/one-beat-out functional transformation** as their RTL. Skid
buffering is an identity operation at this level. The runner is not cycle-accurate
and does not simulate RTL or backpressure. There are no implicit threads, queues,
latency simulation, packet dropping, or rate-changing models. Models should be
stateless; captured state is not automatically reset between runs. All stages
are checked for missing models before consuming input. Input/output beat widths
are checked; user exceptions propagate. Parameters do not rewrite a model:
changing GAIN/OFFSET requires updating the model and rerunning differential tests.

For hardware behavior, compile the mixed Verilog and run a common RTL testbench.
A passing native model alone never proves the external implementation correct.

## Reproduce verification

```bash
export SDK_ROOT=/path/to/dotnet-sdk
# Put iverilog and vvp on PATH.
bash tests/run-axis-interop.sh
```

This builds Debug and optimized .NET versions, generates annotated RTL and
`generated/mixed-axis/sources.f`, runs native checks, then runs Icarus against the
actual handwritten and generated modules. The RTL scoreboard independently
calculates the arithmetic, checks all sidebands and stalled-output stability,
and accounts separately for beats discarded by reset. It drains the pipeline
at the end. No Vivado/Quartus mapping, board execution, or formal proof is claimed.

## Requirements and evidence

| ID | Requirement | Evidence/status |
|---|---|---|
| MIX01 | Compose generated and external RTL with named instances and integer parameters | `AxisPipeline`, mixed RTL replay |
| MIX02 | Check widths, directions, duplicate definitions and identifiers before emission where known | `AxisInteropTests`; external declarations still require RTL compilation |
| MIX03 | Preserve KEEP/LAST/USER and valid/ready under stalls | 12,000-cycle independent RTL scoreboard |
| MIX04 | Common clock/reset contract; no implicit CDC | Shared ports, reset flushing test, explicit API limitation |
| MIX05 | Annotate buses and emit external source list | Generated RTL and `sources.f` |
| MIX06 | Run optional native one-beat models without synthesis | Exhaustive 16-bit sample test; missing-model preflight and exception checks |
| MIX07 | Repeat test in Debug and optimized builds | `tests/run-axis-interop.sh` |
| MIX08 | AXI4-Lite control register bridge | Planned; not implemented |
| MIX09 | Arbitrary port binding, multi-input/output composition | Planned; not implemented |
| MIX10 | Automated external signature checking, including parameter widths | Planned; currently rely on RTL tool diagnostics |

Protocol reference: [AMD AXI4-Stream guide](https://docs.amd.com/r/en-US/ug1483-model-composer-sys-gen-user-guide/AXI4-Stream-Support-in-Model-Composer).
