# Frame reductions for instrument streams

A frame is the sequence of accepted beats up to and including TLAST. The reducer
turns each frame into one result containing signed sum, unsigned sum of squares
(energy), complete-sample count, signed minimum and maximum, and status flags.
It does not need a frame buffer: only running statistics and one output register.

## Build hardware

```csharp
var options = new FrameReductionOptions(
    sampleWidth: 16, sumWidth: 64, energyWidth: 64, countWidth: 32);
var module = new Module("InstrumentStatistics");
var samples = Stream.Input(module, "s_axis", 16, userWidth: 8);
var statistics = FrameReduction.Build(module, samples, "statistics", options);
Stream.Connect(module, statistics,
    Stream.Output(module, "m_axis", options.ResultWidth, userWidth: 8));
File.WriteAllText("statistics.v", module.Verilog());
File.WriteAllText("statistics_axi.v", Axi4Stream.Wrapper(module));
```

[FrameStatisticsExample.cs](examples/FrameStatisticsExample.cs) is the complete
build example. Like other structural helpers, `FrameReduction.Build` constructs
a graph; it is not an additional CIL compiler intrinsic.

For mixed RTL, `AxisComponent.FromSharp(module, "s_axis", "m_axis")` can place
this module in an `AxisPipeline`. Hardware valid/ready wiring accommodates its
many-to-one rate. The existing `AxisPipeline.Run` native API still requires
one-beat models and cannot represent this reduction. Use the dedicated native
frame runner below; do not attach a fake identity model to the hardware reducer.

## Run directly on a PC

```csharp
var options = new FrameReductionOptions();
var samples = new[] {
    new AxisBeat(1,     3, false, 7),
    new AxisBeat(65534, 3, false, 7), // -2 encoded in signed 16 bits
    new AxisBeat(3,     3, true,  7)
};
foreach (var beat in NativeFrameReducer.Reduce(samples, options, userWidth: 8)) {
    var result = options.Decode(beat);
    // Sum=2, Energy=14, Count=3, Minimum=-2, Maximum=3, Flags=0, User=7
    Console.WriteLine(result.Sum);
}
```

`Reduce` is a lazy enumerable with fresh state for each enumeration. It streams
input and emits only completed frame results; it does not synthesize hardware or
simulate clocks. A caller can apply calibration before reduction with
`samples.Select(MixedScience.Calibrate)`. Use `NativeFrameReducer.Push` for
incremental operation and `Reset()` to explicitly discard a partial frame.
A finite enumerable ending inside a frame throws rather than silently discarding
it. Empty input emits nothing. Stopping enumeration early cannot diagnose an
unread tail. User exceptions propagate.

## Arithmetic and validity contract

| Quantity | Meaning | Overflow policy |
|---|---|---|
| Sum | Signed sum of complete samples | Wrap at SumWidth; sticky signed-overflow flag on any addition |
| Energy | Unsigned sum of signed sample squares | Wrap at EnergyWidth; sticky unsigned-overflow flag |
| Count | Number of complete samples | Saturate at all ones; sticky flag on the next complete sample |
| Minimum / maximum | Signed extrema of complete samples | Remain correct even after other quantities overflow |

Samples are byte-aligned signed two's-complement values, 8 through 64 bits wide.
SumWidth is at least SampleWidth and at most 256; EnergyWidth is at least twice
SampleWidth and at most 256; CountWidth is 1 through 64. These constraints ensure
an individual square fits; an arbitrarily long frame can still overflow.
For frames of at most N samples, conservative safe accumulator sizes are
`SampleWidth + ceil(log2(N))` for sum and
`2*SampleWidth + ceil(log2(N))` for energy. Count needs `ceil(log2(N+1))` bits.
Use N>=1; wider accumulators consume additional hardware.

A sample contributes only when every TKEEP bit is one. Partial or zero masks
exclude the entire sample from all arithmetic and set InvalidSample. Such a beat
still carries its TLAST and can close the frame. A frame with no complete samples
produces all-zero statistics with Empty and InvalidSample set. There is no
zero-beat frame on a stream; one invalid LAST beat represents an empty frame.

For fixed-point samples with F fractional bits, sum/min/max retain F, and energy
has 2F. No rounding, division, averaging, or square root is performed. Energy is
in squared input units; it is not automatically physical energy in joules.
After overflow, sum and energy are modular results, not valid exact statistics.
Flags report arithmetic loss even if subsequent samples bring a value back into
range. Count saturation does not stop the other accumulators or extrema.

## Result bus

The default widths (sample=16, sum=64, energy=64, count=32) produce a 200-bit beat:

| Bits | Field |
|---|---|
| 63:0 | Signed sum |
| 127:64 | Unsigned energy |
| 159:128 | Saturating unsigned count |
| 175:160 | Signed minimum |
| 191:176 | Signed maximum |
| 199:192 | Flags |

Flag bits are 0=Empty, 1=SumOverflow, 2=EnergyOverflow, 3=CountOverflow,
4=InvalidSample; bits 7:5 are zero. Custom widths pack fields consecutively in
this same order, followed by the flags byte and zero padding to a byte boundary.
`FrameReductionOptions` exposes offsets and a `Layout` description, which is also
emitted in the Verilog comments. `Decode` rejects invalid result framing, reserved
flags, and padding. It interprets sum/min/max as signed.

Every result has full TKEEP and TLAST=1. TUSER is copied from the **closing input
beat**, including when its sample is invalid. The reducer does not check tag
consistency or maintain separate accumulators for interleaved channels. Route
channels to separate reducers or frame them consistently before use.

## Timing, backpressure and reset

The result becomes valid after the clock edge accepting TLAST. It remains stable
until accepted. While a result is pending and the consumer is stalled, input
READY goes low, preventing the next frame from overwriting the result. An accepted
output can be replaced by a newly completed frame on the same edge. With the
consumer continuously ready, the structure accepts one beat per clock, including
one-beat frames; achievable clock frequency requires target synthesis/timing.
The multiplier and accumulation are not automatically split into pipeline stages.

Core reset is synchronous active-high. The AXI wrapper exposes active-low reset;
hold it through a rising edge and release it synchronously. Reset clears both the
partial frame and any unconsumed result. No result is emitted for discarded work.
The first accepted beat after reset starts a new frame. If an upstream source
continues an old frame after reset, its remaining tail is treated as a new frame;
there is no automatic discard-until-LAST resynchronization.

## Numerical reports

`NumericalErrorReport.Add(reference, actual)` accumulates maximum absolute error,
maximum relative error, and RMS absolute error. Its RMS calculation scales errors
to avoid overflow from squaring a large finite error. Relative error excludes
zero references; absolute/RMS error includes them. Non-finite pairs are counted
separately, not treated as passing samples. Finite pairs whose subtraction
exceeds double range are reported with infinite error and a separate count.

Reports generated by the tests:

- [Calibrated energy](generated/frame-reduction/numerical-error.md): exact native
  arithmetic over every 16-bit input encoding, zero error.
- [Fixed-point rounding](generated/frame-reduction/quantization-error.md): graph
  evaluation of all 16-bit encodings with 12 fractional bits, rounded nearest-even
  to 8 fractional bits. Maximum absolute error is 1/512 (half an output LSB).

Relative error can be 100% near zero even when absolute error is small. Neither a
single maximum nor a zero average should hide special-case failures. The reports
use double references; they do not establish bit-exactness for integers beyond
double's exact range. Frame arithmetic correctness is checked separately with
BigInteger. Reports document their evaluated domain and execution mode; they are
not a proof for every input or a claim of IEEE floating-point synthesis.

## Verification and requirements

Run `tests/run-frame-reduction.sh` with SDK_ROOT set and Icarus on PATH.
Debug and optimized builds run an independent retained-frame BigInteger oracle,
native reduction, graph simulation, and generated RTL replay. The large replay
bench is regenerated under build/, while RTL, reports and coverage are committed.
Tests cover negative extrema through 64 bits, empty/invalid frames, count
saturation, sticky sum/energy overflow, tag propagation, backpressure, and resets
of both partial and completed frames. The RTL replay checks the graph's cycle
trace after graph values have been checked against the independent oracle.

| ID | Requirement | Evidence/status |
|---|---|---|
| FRAME01 | Sum, energy, count, signed extrema once per accepted LAST | Native and graph oracle; RTL replay |
| FRAME02 | Explicit widths, modular sum/energy, saturating count, sticky flags | Overflow frames and wide extrema tests |
| FRAME03 | Invalid samples excluded, invalid LAST still closes, empty flagged | Directed and randomized frames |
| FRAME04 | Stable output and no result loss under backpressure | Randomized replay and final drain |
| FRAME05 | Reset discards partial frames and pending results | Four reset events, including pending-output reset |
| FRAME06 | Native many-to-one execution and explicit truncation handling | Push/Reset/Reduce tests |
| FRAME07 | Packed layout and metadata contract documented and decoded | Annotated RTL, decode and reserved-bit rejection tests |
| NUMREP01 | Absolute/relative/RMS errors and special-case counts | Known metrics, zero/non-finite/overflow tests |
| NUMREP02 | State domain and execution mode for each numerical report | Exhaustive native-energy and graph-quantization reports |

These implement SCI04 (frame reductions) and the initial SCI06 numerical report
facility. General rate-changing mixed native networks, DMA/banking, AXI4-Lite,
complex arithmetic, FFTs and IEEE floating-point adapters remain separate work.
