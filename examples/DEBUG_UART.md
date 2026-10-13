# A C# mini controller behind a debug UART

DebugUartController.cs builds a small command state machine, a writable control
register and a running counter. The byte-level controller is generated from C#;
the serial RX/TX adapter is explicitly hand-written Verilog. Together they form
a runnable debug-monitor example, not a memory loader or complete CPU debug unit.

## Command protocol

Bytes are binary values, not ASCII command characters. Host operation is
stop-and-wait: send one complete command and read its expected response before
sending the next. Multi-byte counter responses are little-endian.

| Host bytes | Operation | Response |
|---|---|---|
| `00` | Ping | `A5` |
| `01 xx` | Echo one byte | `xx` |
| `02` | Read coherent 32-bit counter snapshot | Four bytes, least significant first |
| `03 xx` | Write eight-bit control register | `06` acknowledgement |
| `04` | Read control register | One byte |
| Other command byte | Unknown command | `E0` |

Control resets to 01. Bit zero enables the counter. Counter resets to zero and
increments on every clock with the old control bit zero set to one. Other control
bits are stored and exposed but have no additional behavior. A command snapshot
captures the counter value before the increment on that acceptance edge.

For example, `03 00` stops the counter after the write-acceptance edge, returns
06, and `02` then reads a stable four-byte value. `03 01` resumes counting.

## How the C# builder describes the state machine

There are four state encodings:

| State | Behavior |
|---|---|
| 0 | Accept command byte |
| 1 | Await echo payload byte |
| 2 | Await control-register payload byte |
| 3 | Send one or four reply bytes; stall input until reply accepted |

The actual source constructs these hardware conditions:

```csharp
var idle=state.Eq(Expr.Const(2,0));
var echo=state.Eq(Expr.Const(2,1));
var write=state.Eq(Expr.Const(2,2));
var sending=state.Eq(Expr.Const(2,3));
m.Drive(rx.Ready,~sending);
m.Drive(tx.Valid,sending);
```

The generated HDL has comparators and signal drivers for these expressions.
Changing state is an explicit next-state Mux tree, not a C# switch executed by a
processor at runtime. All register next values are computed from old state.

The counter itself is a small example of conditional state update:

```csharp
m.NextWhen(counter, control.Slice(0,0),
    counter + Expr.Const(32,1));
```

It emits a 32-bit incrementer feeding a register with an enable/hold condition.
Control writes use `m.NextWhen(control, received & write, rx.Data)`.

### Replies and backpressure

The controller has a 32-bit reply register, a reply length, a two-bit byte index
and the command's User tag. Its elaboration-time loop builds a byte-selection
Mux tree selecting reply bits 7:0, 15:8, 23:16 or 31:24. The loop is not running
in the circuit; the muxes are.

The byte index advances only on Tx.Valid AND Tx.Ready. Reply data and metadata
therefore hold if the serializer is busy. On the final accepted reply byte,
state returns to zero. The snapshot register prevents the four-byte count from
mixing different counter values while serialization takes many clocks.

Rx/Tx are eight-bit ready/valid streams. Keep must be one. Rx.Last does not delimit
commands: the command byte defines how many payload bytes are needed. Tx.Last
marks the final reply byte. Tx.User preserves the command-byte tag, not the
payload-byte tag. The physical serial adapter ties Rx.User to zero and does not
transmit User/Last as serial data.

## Serial integration

examples/Verilog/debug_uart_serial.v provides:

- UART RX input synchronized through two registers.
- Start-bit check, eight data-bit samples and stop-bit validation.
- One receive holding register and a sticky overrun flag.
- An 8N1 transmitter with a ready/valid byte interface.
- Generated controller instance plus observable control/counter outputs.

The top-level pins are clk, rst, uart_rx, uart_tx, control_out, counter_out,
rx_overrun and framing_error. Reset is synchronous and active-high. The error
flags clear only on reset. An invalid stop bit discards that received byte.
If the receive holding register is still occupied, a new byte is dropped and
rx_overrun is asserted. This is intentional bounded-buffer behavior, not flow
control on the physical UART wire.

DIVISOR is the rounded clock_hz/baud ratio, at least eight in this implementation.
For a 50 MHz clock and 115200 baud, 434 is approximately 115207 baud. The actual
board clock, constraints and pin assignments must be supplied separately.
There is no autobaud, parity, oversampling majority vote, hardware CTS/RTS, or
proven baud-mismatch tolerance. This example should be reviewed/extended for a
production UART. Do not send unbounded pipelined commands into its one-byte RX
holding register; follow the stop-and-wait protocol.

A command awaiting a missing echo/control payload has no timeout. Reset restarts
framing and discards pending replies. Counter reads and control-register access
are the entire debug feature set; no CPU halt, single step, memory access or
security/authentication protocol is implemented.

## Verification

The byte-level C# simulator checked independent expected responses under random
input bubbles and output stalls, including counter snapshots, unknown commands,
control changes and metadata. Its emitted Verilog passed the identical trace.
It passed Yosys RTL checks and post-synthesis trace simulation too.

An independent UART output decoder and serial input driver exercised the RX/TX
pins at an eight-clock divisor. Eleven reply bytes were verified across ping,
echo, stop/resume, control reads, counter reads and unknown-command handling.
No framing-error/overrun fault injection or baud-drift sweep was performed.
The complete serial/controller RTL passed Yosys structural synthesis checks.
No FPGA place-and-route or board execution is claimed.

Run `bash tests/run-comparison.sh` after the included generated files are present.
Rebuild the C# examples/test traces with `bash tests/run.sh` when modifying them.
