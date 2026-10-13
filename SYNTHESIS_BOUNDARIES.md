# Why SharpHDL

SharpHDL is useful when a circuit's structure benefits from ordinary programming:
parameterized widths, repeated channels, packet fields, reusable streaming blocks,
and automatically generated families of designs. C#, F# and Visual Basic can call
the same .NET library, so different source styles can construct the same circuit.

The original builder frontend works as follows. The language compiler handles names, type checking, generics, function calls and
CIL generation. Running that CIL executes the circuit builder. Overloaded Expr
operators and library calls record a hardware graph containing wires, arithmetic,
selectors, registers and explicit stream control. SharpHDL validates that graph,
simulates it and emits Verilog. FPGA synthesis, placement and routing then create
an implementation for a chosen device. The FPGA does not execute .NET or CIL.

Version 0.11 also provides a restricted behavioral frontend. CilCompiler reads
ordinary static method bodies, translates scalar operations, explores branches,
unrolls constant loops, and constructs that same hardware graph. It does not
execute those methods during translation. See BEHAVIORAL_CIL.md for the exact
supported CIL subset and native-oracle test results.

That division of work keeps the frontend small. The actual hardware work still
belongs to SharpHDL and the FPGA tools: widths, state, latency, ready/valid behavior,
signedness, numerical policies, RTL lowering and physical implementation cannot
be inferred just because the software compiler decomposed an expression.

| Useful application | What SharpHDL contributes | What remains the designer's job |
|---|---|---|
| Packet processing | Explicit fields, CRC, queues, arbitration, stream arithmetic | Protocol boundaries, throughput, buffering and timing |
| Repeated DSP channels | Reusable builder functions, loops, typed widths | Quantization, approximation error, pipeline placement and resources |
| Small controllers | Registers and muxes constructed from familiar code | State transitions, reset and handshake correctness |
| Teaching hardware | Binary cards, visible registers, cycle simulator, readable RTL | Teaching concurrency and testing; hiding tool setup does not eliminate it |
| Mixed-language teams | Common .NET API and assemblies | Staying inside the hardware library contract |

This is an executable prototype with tested examples, not a production replacement
for established HDL and synthesis tools. The Go Board starter has completed a
software implementation flow; there has been no physical-board validation. Mature
IP integration, advanced timing closure, CDC, vendor primitives and certification
remain larger engineering tasks. Source-language familiarity reduces syntax work;
it does not remove hardware design.

# What it won't synthesize

The boundary is **host code that constructs a circuit** versus **data-dependent
behavior that must exist in the circuit**. Host arrays, lists, classes, strings,
reflection and file input may be useful while building a fixed graph. They do not
become a garbage collector, filesystem or dynamically allocated memory on an FPGA.

| Construct or request | Current behavior / required alternative |
|---|---|
| Arbitrary C#, F#, VB or CIL method | Restricted Int32/UInt32/Boolean static methods can use CilCompiler; arbitrary programs are rejected. |
| Ordinary int/double computation | Host builder arithmetic executes normally. Methods explicitly selected for CilCompiler translate supported scalar operations; double is unsupported. |
| Signal-dependent C# if/while | Ordinary scalar if/switch is supported by CilCompiler. Runtime-dependent loops require explicit BuildSequential with a watchdog. |
| Recursion or loops with runtime hardware bounds | No recursion or hardware call stack. BuildSequential schedules supported runtime loops one CIL operation per edge. |
| System.Math calls on runtime hardware values | No automatic CORDIC/ROM substitution. Select the implemented hardware math block explicitly. |
| Runtime strings, objects, allocation, exceptions, tasks, async/await or threads | No hardware runtime for these facilities. Model finite state, queues and protocols explicitly. |
| File/network/console I/O in the FPGA | Host-side I/O only. Build actual interfaces and protocol engines. |
| Variable-shift or variable-slice Expr operators | Raw Expr has constant shifts/slices. CilCompiler lowers variable integer shifts to a masked barrel network; variable slices remain explicit. |
| Automatic RAM inference from a C# array | Use SynchronousRam or one owned uint[] parameter in BuildSequential (see SEQUENTIAL_CIL.md). General arrays and aliases remain unsupported. Lookup is ROM and FIFO is register based. |
| Implicit pipelines, automatic resource sharing or timing-driven scheduling | Registers and iterative blocks are explicit. A local variable is not a pipeline stage. |
| Custom pragma or unrecognized attribute that changes implementation | Pass architecture and numeric-policy options to the builder. The recognized PortWidth attribute controls CilCompiler boundary widths. |
| Direct SFixed division or selectable compact-float rounding | Not implemented. Fixed conversion supports Floor/NearestEven; compact-float packer uses its defined nearest-even behavior. |
| Full IEEE 754 behavior | Compact floats have custom finite encodings; no complete NaN/infinity/subnormal/exception system. |
| Multiple clock domains, CDC or asynchronous reset in a core | Core API is single clock with synchronous active-high reset. Use separately engineered wrappers. |
| X/Z four-state or analog/timing simulation | Core simulator uses two-state values and discrete clock edges. |
| Device pins, PLLs, transceivers or a complete board image from arbitrary code | Requires board-specific shells, primitives, constraints and FPGA tool flow. |
| Unbounded width or arbitrary stream shapes | Expr widths and each block's bounds are checked; stream data is byte aligned. |

Unsupported API use often fails during .NET compilation or graph validation.
A normal host computation may instead run successfully and produce a constant:
that is why the boundary must be understood, not merely enforced by hoping for
an error message. A supported graph also does not guarantee that a given device
has sufficient capacity or meets a requested clock frequency.

## Worked boundary examples

```csharp
// Host loop builds four distinct adders; this loop is gone after construction.
for (int i = 0; i < 4; i++)
    m.Drive(outputs[i], inputs[i] + Expr.Const(8, 2));

// Runtime hardware choice: a mux controlled by a one-bit signal.
m.Drive(y, Expr.Mux(select, a, b));

// Explicit clocked state: RHS is evaluated from the previous cycle's registers.
m.NextWhen(count, enable, count + Expr.Const(8, 1));
```

The alternative `if (select)` is not a valid conditional for an Expr object. An
ordinary int/bool method selected for CilCompiler can use a hardware-dependent if.
Computing `int x = 3 + 2` is a host calculation. Calling an explicitly selected
iterative divider constructs a real stateful circuit whose latency is carried by
ready/valid, rather than making an ordinary software call take FPGA clock cycles.

## Version 0.12 coverage update

See CONSTRUCTS_AND_REQUIREMENTS.md for the audited gap matrix and acceptance
requirements. Constant SFixed shifts and a named explicit StateMachine helper
are now implemented. Automatic sequential CIL loop extraction remains planned.

## Version 0.13 boundaries

See SEQUENTIAL_CIL.md and CONSTRUCTS_AND_REQUIREMENTS.md for the authoritative
new subset: unsigned UBits widths, flat read-only packet fields, explicit clocked
loops, one uint[] RAM, assertion status, and VeriSim launch links.
