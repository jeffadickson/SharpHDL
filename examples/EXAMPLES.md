# Example gallery — SharpHDL 0.7

| Example | Language | What it demonstrates |
|---|---|---|
| Same adder in three languages | C#, F#, VB | Actual CIL, identical graph/RTL, FPGA primitive mapping |
| Mixed-language pipeline | F#, C#, VB | +2 stage, skid buffer, multiply stage, stall-safe metadata |
| Explicit pipelining | C# | One stage versus two stages, measured latency and throughput |
| Go Board classroom | C# with board shell | Binary LEDs, add-two digits, run/pause counter; routed image |
| Typed gain and complex power | C# and F# | Bit widths, fixed rounding/saturation, compact floats and skid buffers |
| Debug UART controller | C# + Verilog serial adapter | Command FSM, snapshot, control register and serial integration |
| Hand-written filter bank | Verilog | Equivalent RTL with explicit wiring and stall handling |
| Counter | C# | Explicit register widths and enable |
| Packet router | C# | First-beat classification, packet retention, two queues |
| Cosine / reciprocal / polynomial log | C# | Choosing a hardware math implementation |
| Packet peak reducer | C# | Many samples in, one summary out |
| Configurable sensor filter bank | F# | One configuration generates broadcast, parallel filters, queues and tagged merge |
| Binomial FIR | F# | A coefficient list generates parallel filter hardware |
| Sample-indexed oscillator | F# | Phase state feeding a sine/cosine pipeline |
| Scrambler round trip | F# | Functional composition of stateful elastic stages |

See ../HOW_THE_COMPILER_WORKS.md for the source-to-IR-to-Verilog walkthrough.

Start with FSharp/Designs.fs and FSharp/README.md. The three F# designs are small
and the test program records their actual simulator traces into HDL testbenches.
Generated source and testbenches are in generated/fsharp.

## Packet peak reducer

PacketPeakExample.Build() accepts signed 16-bit samples on a ready/valid stream.
Each Last terminates a frame. It emits one 32-bit summary per frame:

- Bits 31:16: sample count, saturating at 65535.
- Bits 15:0: unsigned peak absolute magnitude, including 32768 for input -32768.
- User: the input User value on the final sample.
- Keep: 0xf; Last: one.

Input Keep must be 3; it is not interpreted by this sample-processing example.
Frames contain at least one accepted sample. All samples up to Last contribute.
The peak and counter reset after the final sample is accepted. A synchronous
module reset discards an incomplete frame and any buffered result.

The block can process non-final samples while the previous frame's summary is
stalled. It backpressures a final sample if there is no room for its summary.
That illustrates why a hardware function need not produce one output for every
input. It is useful as a building block for telemetry summaries or level detection,
but has no frame timeout, malformed-frame detection or trigger capture RAM.

The test covers 60 variable-length frames, negative values, -32768, single-sample
frames, source bubbles and prolonged/random output stalls. The count-saturation
limit is implemented but the regression uses short frames.

## Verification of this release

- Three F# designs compiled with F# 8.0 from .NET SDK 8.0.425 and executed on
  .NET 8.0.31, calling a separately Roslyn-compiled SharpHDL.dll.
- 2,307 F# assertions passed.
- C# suite: 15,212 base + 42,226 feature assertions passed.
- All four new example Verilog testbenches passed Icarus simulation.
- All four passed Yosys RTL synthesis/checks and post-synthesis Icarus simulation.
- F# example AXI4-Stream wrappers compiled.

The container's dotnet CLI/MSBuild process-introspection startup failed, so the
included direct-compiler path was used and tested. The standard fsproj is supplied
but a full MSBuild project build is not claimed. FPGA place-and-route and hardware
execution were not performed. Previous v0.2 tests and synthesis logs are retained.

See FSharp/DATAFLOW_AND_USEFULNESS.md for a practical assessment and worked stream wiring.


## Numeric examples (0.6)

`NumericExamples.cs`: typed signed Q8.16 gain, explicit nearest-even rounding
and saturation, with input/output skid buffers; and Compact24 complex power
from a packed real/imaginary input. `FSharp/NumericDesigns.fs` implements the
same two circuits using F# composition. See the root numeric and skid guides.

## Packet processing with measured hardware costs

[Packet checksum benchmark](PacketBenchmark/README.md): the same 16-bit packet
checksum in C# and handwritten Verilog, compared with an independent traffic
oracle, mapped iCE40 simulation, and three place-and-route seeds. Includes
readable bus layouts, latency/throughput checks, measured resource reports,
and explicit limits on interpreting core timing as board performance.
