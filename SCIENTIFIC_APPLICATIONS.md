# Scientific applications

SharpHDL is useful for bounded, repeated operations on streaming instrument data:
calibration, thresholding, sample energy, and packet processing. It has a growing
hardware-construction and CIL-compilation toolset, not a general scientific C#
compiler or a replacement for the whole Vitis HLS toolchain. In particular,
ordinary .NET floating-point code and arbitrary array algorithms are not
transparently transformed into optimized FPGA accelerators.

## A working instrument pipeline

The [mixed-language example](examples/MixedScience.cs) accepts signed 16-bit
samples, applies `saturate_int16(2*x - 10)` in handwritten Verilog, then squares
the calibrated sample in generated SharpHDL. An upstream skid buffer absorbs
backpressure. TUSER carries an 8-bit channel/tag; TLAST marks a frame end.

| Input sample | Calibrated sample | Unsigned 32-bit energy |
|---:|---:|---:|
| 0 | -10 | 100 |
| 100 | 190 | 36,100 |
| -100 | -210 | 44,100 |
| 32,767 | 32,767 (saturated) | 1,073,676,289 |
| -32,768 | -32,768 (saturated) | 1,073,741,824 |

These are exact integer operations. Calibration saturation is intentional and
currently has no separate saturation flag. For a real instrument, add a flag or
counter if clipping must be detected rather than simply bounded.

`ScientificStreams.SampleEnergy(module, input, name)` is reusable for byte-aligned
signed samples from 8 through 64 bits. Its output is unsigned and twice as wide.
Multiplication widens **before** multiplying, so the most-negative signed sample
also squares correctly. A signed fixed-point input with F fractional bits gives
an output with 2F fractional bits; there is no rounding or rescaling. Numerical
units must be documented by the application (e.g. ADC-count squared, not joules).

The output is an elastic one-entry stage: one clock of latency without stalls,
and one sample per clock while continuously ready. That is a logical rate, not a
frequency guarantee; wide multiplication may need additional pipelining for a
particular device. This helper does not automatically schedule its multiplier.

A full input TKEEP produces full output TKEEP. Any partial input sample produces
a beat with TKEEP=0. The computed data on that invalid beat must be ignored; LAST
and USER still propagate. No beat is dropped, so frame boundaries remain visible.
The native overload uses the same convention. A later reduction must ignore
invalid samples and still handle an invalid beat carrying TLAST.

Run `tests/run-axis-interop.sh` for native and mixed RTL verification. Native
calibration/energy tests enumerate all 65,536 input encodings. Additional graph
and native checks exercise 8/16/24/32/64-bit extrema, including signed minimum.
The mixed RTL test checks arbitrary keep masks, tags, frame ends, stalls and reset.

## Existing building blocks and remaining work

| Area | Available now | Important gap |
|---|---|---|
| Integer precision | Explicit bit widths, signed operations, widened typed arithmetic | Automated range/error analysis |
| Fixed point | Structural `SFixed`, explicit rounding and wrap/saturate conversion | Uniform native numerical types and automatic CIL lowering for every operation |
| Compact float | Custom finite formats, arithmetic and ROM-based functions | IEEE-754 compliance; ordinary C# float/double synthesis |
| Elementary functions | CORDIC and lookup-based hardware building blocks | Accuracy budgets, domain/error reporting and target-qualified implementations |
| Streaming | Ready/valid, skid/FIFO, scheduled pipelines, external AXI RTL | General rate-changing native networks and arbitrary mixed-module topology |
| Arrays/memory | Owned sequential RAM, Xilinx/Intel inference policies | Banking, burst DMA, tiled arrays, memory-aware scheduling |
| Scientific primitives | Exact signed sample energy, calibration example, frame sum/count/min/max/energy | FIR/FFT, complex arithmetic, covariance |
| Verification | Native/graph/RTL checks and domain-specific numerical error reports | Standard scientific reference datasets and wider accuracy budgets |

`CompactFloat` is explicitly **not IEEE-754**: it flushes its zero-exponent range
to zero, uses NaR, rounds nearest-even, and saturates finite overflow. Calling it
"half precision" would imply guarantees it does not provide. Existing numerical
wrappers construct graphs; they are not all ordinary native CPU numeric types.
The native scientific helper added here operates on `AxisBeat`/`BigInteger`.

## Prioritized requirements

| ID | Requirement | Status / acceptance criterion |
|---|---|---|
| SCI01 | Widen signed sample square and preserve fractional scale | Implemented; signed extrema and mixed RTL oracle |
| SCI02 | Define partial sample, metadata, stall and reset behavior | Implemented; invalid keep propagation and reset/discard scoreboard |
| SCI03 | Native execution of the same stream calculation | Implemented through explicit functional models, not RTL simulation |
| SCI04 | Frame sum/count/min/max with widened accumulators and overflow flags | Implemented in FrameReduction; empty/invalid frames, TLAST, overflow, stalls and reset tested |
| SCI05 | Dot products and complex multiply with explicit scaling | Planned; full-precision reference, rounding ties, extrema and DSP mapping |
| SCI06 | Numerical validation report | Initial facility implemented: absolute/relative/RMS error and special-case counts; exhaustive energy and quantization reports |
| SCI07 | Banked/tiled memory and burst transfer support | Planned; port conflicts, bandwidth, reset and target RAM mapping tests |
| SCI08 | Optional IEEE floating-point external IP adapters | Planned; explicit latency/backpressure and numerical exception contracts, native reference and RTL comparisons |
| SCI09 | AXI4-Lite configuration and status | Planned; independent address/data handshakes, byte strobes, reset and response tests |

[Frame reductions and numerical reports](FRAME_REDUCTIONS.md) are now implemented.
An instrument can compute per-frame energy, peak and sample count without sending
every sample to a host. The reducer has explicit overflow and invalid-sample
semantics and a dedicated native many-to-one runner. It does not extend the
one-beat native model of AxisPipeline.Run. Banked memory/DMA and AXI4-Lite remain
the next integration tasks; complex arithmetic and external IEEE floating-point
IP still need explicit numerical and timing contracts.
