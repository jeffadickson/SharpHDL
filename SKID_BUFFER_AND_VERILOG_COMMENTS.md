# A C# skid buffer and readable Verilog

`StreamBlocks.SkidBuffer` is implemented in `src/SharpHDL/Streams.cs`. It is a
**two-entry registered skid buffer**, with an output/head register and a spill
register. It has one cycle minimum latency and can sustain one beat each cycle.
Both forward payload and backward readiness are separated from downstream
combinational paths: input Ready depends only on the spill-valid register.
It is a single-clock primitive; it is not a clock-domain crossing.

```csharp
var input = Stream.Input(m, "input", 16, 8);
var output = Stream.Output(m, "output", 16, 8);
Stream.Connect(m, StreamBlocks.SkidBuffer(m, input, "skid"), output);
```

All DATA, KEEP, LAST and USER bits are stored atomically. An asserted output
VALID and its payload remain stable during a stall. Reset clears both valid
bits and discards both queued beats. Core reset is synchronous active-high;
use `Axi4Stream.Wrapper` for the existing active-low interface/reset masking.

The extra slot is essential: after downstream stops, upstream may still see
Ready for one accepted beat. That beat lands in the spill register. Ready then
goes low without a combinational downstream-to-upstream path.

| Before rising edge | Output offered | Downstream Ready | Upstream offered | Input Ready | Action on this edge |
|---|---|---:|---|---:|---|
| Empty | None | 0 | A | 1 | Capture A into head |
| Head A | A | 0 | B | 1 | Capture B into spill |
| Head A, spill B | A | 0 | C | 0 | Hold everything |
| Head A, spill B | A | 1 | C | 0 | Deliver A; move B to head |
| Head B | B | 1 | C | 1 | Deliver B; capture C into head |
| Head C | C | 1 | None | 1 | Deliver C; become empty |

There is one upstream acceptance bubble when leaving the full state. Output
can remain continuous. This is the price of deriving readiness from registered
occupancy without looking ahead at downstream Ready. It is not a zero-latency
fall-through bypass buffer. Use `Fifo` for deeper queues; use `Map` when only an
elastic arithmetic stage is needed and its ready path is acceptable.

The C# graph-building code expresses the key control directly:

```csharp
var advance = ~valid | output.Ready;
m.Drive(input.Ready, ~full);
m.Next(valid, Expr.Mux(advance, full | input.Valid, valid));
m.NextWhen(head, advance & (full | input.Valid),
           Expr.Mux(full, spill, beat));
m.Next(full, Expr.Mux(advance, Expr.Const(1,0), full | input.Fire));
m.NextWhen(spill, ~advance & input.Fire, beat);
```

`Next` constructs a next-state expression. All these registers update together
at a rising edge; statement order does not serialize the hardware.

## Verilog comments are generated from design metadata

The emitter now documents clock/reset behavior, ports, stream directions,
byte-lane numbering, Keep semantics, registers, expressions, slices, extension,
truncation, concatenations and ROMs. Packed fields receive explicit bit ranges.
For the 16-bit-data, 8-bit-user skid example:

```verilog
// Packed stream beat: DATA [15:0], KEEP [17:16], LAST [18],
// USER [26:19]. Output holding register.
reg [26:0] s_skid_head;

// Spill occupied. Input ready is its inverse,
// independent of downstream ready.
reg [0:0] s_skid_spill_valid;
```

The emitter writes each annotation as a single comment line; the example above
wraps it for reading. See the full emitted `generated/SkidBuffer16.v`.

Numeric annotations explain sign, exponent, scale, rounding and saturation.
The existing CORDIC bundle identifies phase/Y/X; polynomial bundles identify
argument/accumulator; interpolated-ROM bundles identify offset/base/slope and
padding. FIFO entries and packet layouts likewise identify field offsets.

Add application-specific meaning directly in C# or F#:

```csharp
input.Data.Describe("ADC: signed sample [15:0], units = counts; full TKEEP required.");
input.User.Describe("Timestamp [23:0] in sample clocks; channel ID [31:24].");
var header = new PacketLayout().Field("version",4).Field("type",12)
                               .Field("length",16).Field("source",32);
header.Describe(headerWord); // Generates actual named bit ranges.
```

Metadata changes comments only. `Describe` returns the same expression so it
can be chained; its annotation is not a numeric cast or semantic enforcement.
Comments cannot infer physical units from arbitrary expressions: annotate
application-defined payloads and metadata at the boundary. Stream connections
carry payload/user descriptions to their destination. The emitter still gives
all intermediate expressions sized wires to preserve exact truncation, even
where that produces more lines than hand-written RTL. Synthesized netlists
come from Yosys and do not retain all source-level comments.

## Checks

The C# skid test checks capacity, order, sidebands, stalled-output stability,
reset flushing, one-cycle latency, continuous throughput and readiness
independence from changes to downstream Ready. It produces an executable
Verilog trace. `tests/synthesize-numeric.sh` additionally synthesizes this
buffer and reruns that trace on the resulting RTL. The C#/F# numeric pipelines
exercise the same buffer at 24-bit payload width. No physical timing target
or CDC claim is made.
