# Hand-written Verilog comparison

filter_bank_handwritten.v is a hand-authored version of the same fixed four-channel
monitor built by F#. It is not the generated Verilog renamed or reformatted.

It uses reusable modules:

| Module | Responsibility |
|---|---|
| filter_bank_handwritten | Exactly-once broadcast and explicit four-branch/merge-tree wiring |
| feature_branch | Signed four-tap FIR, elastic result register, FIFO, channel tagging |
| feature_fifo | Small register-array queue with simultaneous push/pop |
| feature_merge2 | Round-robin single-beat merger, stable selection under stall |

The default coefficients and queue depths match the F# configuration. Shorter
filters pad unused taps with zero coefficients; synthesis removes unused history.
Input Last is carried through queues but output Last is one for every feature
message. Input Keep must be 3. There is no CDC or RAM-backed queue.

## The arithmetic is straightforward in RTL

This actual source excerpt makes signed extension and arithmetic explicit:

```verilog
reg signed [15:0] delay1,delay2,delay3;
wire signed [31:0] x0={{16{s_data[15]}},s_data};
wire signed [31:0] x1={{16{delay1[15]}},delay1};
wire signed [31:0] x2={{16{delay2[15]}},delay2};
wire signed [31:0] x3={{16{delay3[15]}},delay3};
wire signed [31:0] p0=x0*C0,p1=x1*C1,p2=x2*C2,p3=x3*C3;
wire signed [31:0] sum=p0+p1+p2+p3;
```

Every product and the sum are 32 bits, matching the SharpHDL expression widths.
Coefficient parameters are signed 32-bit values. No automatic precision growth
is implied. For the supplied filters the results fit; arbitrary coefficients
could overflow and wrap.

## The state is also explicit

Inside the elastic FIR stage, this is the actual non-reset update logic:

```verilog
if (s_ready) valid<=s_valid;
if (fire) begin
    delay1<=s_data;delay2<=delay1;delay3<=delay2;
    data<=sum;keep<=4'hf;last<=s_last;user<=s_user;
end
```

Here `s_ready = !valid || fifo_ready` and `fire = s_valid && s_ready`. Nonblocking
assignments shift old delay values together and capture the old-history result.
If the stage is stalled, valid/payload/history hold. In the F# example, Map and
NextWhen construct this behavior from reusable library code.

## Broadcast is not just connecting Valid four times

The complete control for preventing duplicate branch delivery is compact:

```verilog
assign branch_valid = {4{s_axis_tvalid}} & ~delivered;
assign s_axis_tready = &(delivered | branch_ready);
always @(posedge clk) begin
    if (rst) delivered <= 4'b0000;
    else if (input_fire) delivered <= 4'b0000;
    else delivered <= delivered | (branch_valid & branch_ready);
end
```

Each branch receives shared Data/User plus its own Valid and Ready connection.
The four delivered flags remember which consumers already accepted the held
sample. This is the hand-written counterpart of StreamBlocks.Broadcast.

## Structural wiring versus the F# builder

A hand-written branch instantiation looks like this:

```verilog
feature_branch #(.C0(1),.C1(3),.C2(3),.C3(1),.DEPTH(4),.CHANNEL(0)) smooth (
    .clk(clk),.rst(rst),.s_data(s_axis_tdata),.s_last(s_axis_tlast),.s_user(s_axis_tuser),
    .s_valid(branch_valid[0]),.s_ready(branch_ready[0]),
    .m_data(result_data[31:0]),.m_keep(result_keep[3:0]),.m_user(result_user[15:0]),
    .m_valid(result_valid[0]),.m_ready(result_ready[0]));
```

There are four such instances, followed by three two-input merger instances.
By comparison, the F# builder maps over its configuration and recursively creates
the merge tree. Changing its list can alter channel count, tap count and queue
size. This hand-written example intentionally has a fixed four-channel topology
and a four-tap maximum. A generic SystemVerilog implementation could use arrays,
generate loops and parameters to achieve comparable configurability.

| Aspect | Hand-written example | F# / SharpHDL example |
|---|---|---|
| Arithmetic and state visibility | Direct RTL, easy to inspect | Builder plus graph/HDL dumps |
| Wire and metadata connections | Named ports and flattened buses | Stream bundles and connection helper |
| Topology changes | Edit this fixed top, or add HDL generation | Change configuration list |
| Tool dependency | Verilog compiler/synthesis | .NET builder then Verilog compiler/synthesis |
| Timing/resource control | Direct architecture control | Explicit stages still required; no automatic timing closure |
| Shared software models | Separate infrastructure needed | Convenient .NET model/test integration |

Neither approach is inherently faster. The generated and hand-written examples
implement the same architecture. An experienced RTL designer can improve either
one, for example with RAM FIFOs, timing cuts or different output bandwidth.

## How the comparison was verified

filter_bank_trace_adapter.v renames the hand-written ports to match the existing
FsFilterBank4 testbench. It contains no processing logic. The exact same 96-sample,
384-result input/stall trace checks Ready and all output fields cycle by cycle,
including invalid-cycle field values. The hand-written implementation passed.
It also passed Yosys RTL checks and post-synthesis simulation against that trace.
This is a finite regression comparison, not a formal equivalence proof.

To repeat with normal installed Icarus/Yosys tools, run:

```sh
bash tests/run-comparison.sh
```

The existing generated FsFilterBank4 testbench is included, so the comparison
can run without recompiling F# first. Run tests/run-fsharp.sh to regenerate it.

## UART files

The same folder contains debug_uart_serial.v, a separate small hand-written 8N1
serial adapter for the generated C# DebugUartController. It is not part of the
filter bank. Its independent serial-pin test is debug_uart_serial_tb.v. See
../DEBUG_UART.md for the protocol, reset, buffering and baud-rate contracts.
