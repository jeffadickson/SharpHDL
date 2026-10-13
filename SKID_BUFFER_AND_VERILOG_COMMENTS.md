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

Intermediate nets also support explicit `Expr.Named("meaning")` hints and
operation-based fallbacks. See [net naming requirements and examples](NET_NAMING.md).

## Compare buffering and backpressure in the browser

Open the [workbook WASM lab](WASM_WORKBOOK.md) and choose **Backpressure without
a buffer**, then **Backpressure with a skid buffer**. Both views simulate the
same generated `Backpressure` module. Two independent, handshake-aware producers
send token IDs 10, 11 and 12 to receivers with the same pause pattern. Each
producer advances only when its own input handshake succeeds; broadcasting one
shared producer to both lanes would require `StreamBlocks.Broadcast`.

Here “without a buffer” means a direct connection, not an incorrectly designed
register that overwrites stalled data:

```csharp
// Zero storage: payload/valid go forward, ready goes backward.
Stream.Connect(m, input, output);

// Two storage slots: head plus spill; no downstream combinational ready path.
Stream.Connect(m, StreamBlocks.SkidBuffer(m, input, "skid"), output);

// Another valid choice: a single elastic register, not a skid buffer.
Stream.Connect(m, StreamBlocks.Map(m, input, "stage", 8, x => x), output);
```

These are alternatives, not three connections to instantiate on the same ports.
The runnable C# is `examples/LearningLab/Backpressure.cs`; its emitted hardware
is `generated/learning/Backpressure.v`. The browser compares the first two.

| Property | Direct connection | Single elastic register | This skid buffer |
|---|---|---|---|
| Stored beats | 0 | 1 | 2 |
| Minimum latency | 0 cycles | 1 cycle | 1 cycle |
| Input Ready | Output Ready | Empty OR output Ready | Spill is empty |
| Combinational backward ready path | Yes | Yes | No |
| Sustainable rate when unstalled | 1 beat/cycle | 1 beat/cycle | 1 beat/cycle |

Backpressure means the receiver lowers Ready. A producer must keep Valid asserted
and DATA/KEEP/LAST/USER unchanged until the beat is accepted. Valid alone is not a
transfer. Count a transfer only on a rising edge with Valid AND Ready high, outside
reset. The demo's `accepted` and `delivered` wires show these conditions; the
`sent` and `received` counters are **testbench instruments**, not extra hardware
in the stream. Invalid payload values are irrelevant even when the viewer displays
a number.

### Predict the seven edges

After the reset edge, the receiver pauses for three edges, then stays ready.
Counts below are cumulative after each edge. A dash means no delivered token.

| Edge | Receiver Ready | Direct accepted | Skid accepted | Each receiver gets | Each received count |
|---:|---:|---:|---:|---|---:|
| 1 | 0 | 0 | 1 | — | 0 |
| 2 | 0 | 0 | 2 | — | 0 |
| 3 | 0 | 0 | 2 | — | 0 |
| 4 | 1 | 1 | 2 | 10 | 1 |
| 5 | 1 | 2 | 3 | 11 | 2 |
| 6 | 1 | 3 | 3 | 12, with LAST | 3 |
| 7 | 1 | 3 | 3 | — | 3 |

The direct producer holds 10 for the whole pause. The skid producer gets ahead by
two tokens, then holds 12. At edge 4, the full skid buffer delivers 10 but cannot
accept 12 yet: input Ready is still low before that edge. At edge 5 it delivers 11
and accepts 12 together. The output has no recovery gap in this experiment.
Buffering absorbs a short stall and cuts the ready timing path; it does not make
the receiver faster or absorb an unlimited stall. Both correct designs deliver
the same packet without loss. Merely delaying Ready by a register without room
for the extra accepted beat is unsafe.

### Requirements and verification

| ID | Requirement | Evidence |
|---|---|---|
| BP-01 | Provide C# direct and skid paths with explicit stream bus annotations. | Generated Backpressure RTL and embedded source. |
| BP-02 | Every transfer uses pre-edge Valid AND Ready; producers hold stalled beats. | Handshake-driven testbench and exact edge-count checks. |
| BP-03 | Preserve order, DATA, KEEP, LAST and USER without loss or duplication. | Native and browser self-checking scoreboard; idle drain edge. |
| BP-04 | Demonstrate two-slot capacity, stable stalled output, ready isolation and full recovery. | Directed assertions and seven-edge reference table. |
| BP-05 | Integrate predictions, waveforms and teacher explanations in the workbook. | Both selectable WASM experiments and native/browser trace comparisons. |

Run `tests/run-learning.sh` to regenerate the module and native fixtures, then
`node tests/check-wasm-workbook.js --browser` for the actual WASM comparisons.
The existing core skid tests additionally cover reset flushing, random stalls
and synthesis; this educational trace deliberately stays short and predictable.
