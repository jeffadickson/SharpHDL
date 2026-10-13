# Meaningful generated net names

Expression numbers identified graph nodes, not design intent. Names now retain
explicit intent or identify an operation; they do not alter the hardware.

```csharp
var address = (baseAddress + offset).Named("effective_address");
var selected = address.Eq(Expr.Const(16, 0xff02))
    .Named("uart_tx_selected")
    .Describe("UART transmit register selected at word address 0xFF02.");
```

The result includes `n_effective_address` and `n_uart_tx_selected`. Ordinary
unnamed additions use names such as `n_add_17`; slices use `n_bits_7_0_18`.
These describe operations, not guessed application meaning. TinyRisc explicitly
names its decode fields, operands, effective address, writeback, next PC,
execution controls and memory-mapped register comparisons. Repeated comparisons
can have suffixes because naming does not merge or optimize expression nodes.

CIL arithmetic uses the method name, IL offset and opcode, for example
`n_Accumulate_IL_0002_add`. Existing comments retain source provenance.
This does not recover C#/F#/VB local variable names: those require optional
compiler/debug metadata and may disappear during compilation. Structural DSL
construction uses explicit `Named` calls; PDB local-name recovery is not implemented.

## Requirements and acceptance

| ID | Requirement | Verification |
|---|---|---|
| NAME-01 | `Expr.Named(string)` attaches intent, returns the same object and changes no arithmetic, latency, ports or state. | Identity assertion; exhaustive 256-input RTL arithmetic and narrowing test; CPU regressions. |
| NAME-02 | Emit deterministic legal unique identifiers even for keywords, digits, punctuation, duplicate names, long hints and suffix collisions. | Naming regression plus Icarus compilation. |
| NAME-03 | Retain explicit names; give other intermediate nodes bounded operation/slice names. | Naming regression and TinyRisc generated RTL. |
| NAME-04 | Preserve every intermediate width, slice and truncation, including slice expressions emitted as `>> 0`. | Four-bit slice of wrapping eight-bit addition tested for all inputs. |
| NAME-05 | Keep comments, bus descriptions and CIL provenance; name CIL arithmetic by method/IL offset/opcode. | Behavioral regression and inspection of generated RTL. |
| NAME-06 | Regenerate TinyRisc RTL and the embedded VeriSim design; retain PC/register/waveform behavior. | CPU, synthesized CPU, demo payload and real WASM browser tests. |

Names use an `n_` prefix, separate from `s_` signals, `mem_` memories and ROM
function identifiers. Each non-ASCII-letter/digit/underscore becomes `_`; the
hint is limited to 64 characters before a deterministic collision suffix.
Blank/null hints are rejected. Identifiers are case sensitive. The first emitted
node gets the unsuffixed name; later collisions get `_2`, `_3`, etc. Stable graph
construction gives stable output; names are not promised stable across edits.

Like `Describe`, `Named` mutates metadata on a shared expression: the last call
wins for all uses of that object. Naming a signal or constant does not rename
its existing interface or force a literal into a wire. Hint metadata is not
propagated through arbitrary graph rewrites. Synthesis may optimize away or
rename nets; these names describe the emitted RTL, not a physical placement.

Run `tests/run-tinyrisc.sh` and `tests/run-advanced.sh`, then
`python3 tests/check-net-naming.py` with
`DOTNET_ROOT` and Icarus on PATH. See `docs/tinyrisc/README.md` for demo tests.
