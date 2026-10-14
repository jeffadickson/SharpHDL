# AXI4-Lite control, banked memory and local stream capture

SharpHDL can now build a 32-bit AXI4-Lite register bank, independent RAM banks,
and a stream-to-local-RAM capture block. The complete [CaptureInstrument](examples/CaptureInstrument.cs)
example combines them: software arms capture over AXI4-Lite, a stream supplies
samples, and software reads the stored beats back through status registers.

This is a local memory transfer path, **not a full AXI4 burst DMA master**.
It does not access host DDR, issue burst addresses, manage descriptors, or handle
cache coherency. Those remaining requirements are listed below.

## Control registers

```csharp
var m = new Module("Controller");
var port = AxiLitePort.Slave(m, "s_axi", addressWidth: 16);
var bank = new AxiLiteRegisterBank(m, port, "csr");
var control = bank.AddRegister("control", 0x00, resetValue: 0);
var gain = bank.AddRegister("gain", 0x04, resetValue: 1);
bank.AddStatus("status", 0x08, statusExpression); // 32-bit Expr in this module
bank.Build();
```

Writable register values are hardware expressions (`control.Value`, `gain.Value`).
`Written` is a one-cycle commit event when a write has at least one byte enabled.
The new register value takes effect at that edge: other sequential logic sampling
`Written` on that same edge sees the **old** `Value`. Use the buffered value next
cycle or design a separate explicit command interface when same-edge command data
is needed. The example uses level controls rather than this event.

| Behavior | Contract |
|---|---|
| Bus width | 32-bit data; address width 2..32 bits; byte addressing |
| Address/write data order | Independent AW and W holding registers; either may arrive first |
| Outstanding work | One write pair/response and one read response; read/write paths independent |
| WSTRB | Bit i updates byte i; zero is a successful no-op on a writable address |
| Read snapshot | Captured at AR handshake, held unchanged until RREADY |
| Write response | Published after both address and data have been captured |
| Bad address | SLVERR (2) for unmapped or unaligned addresses; bad reads return zero |
| Read-only write | SLVERR, even with zero strobes |
| Read/write same edge | Read sees the old writable-register value |
| Protection bits | AWPROT/ARPROT accepted but ignored; no access-control policy |
| Reset | Clears queued address/data/responses and restores writable reset values |

The implementation inserts bubbles between transactions. It is intended for
control/status traffic, not one-transfer-per-clock sustained data movement.
READY depends on registered occupancy, not on the other channel's VALID.
Responses remain stable under backpressure; a pending write response prevents
acceptance of another write pair. Status values can change while an already
captured read response remains stable. There is no burst, ID, atomic, interrupt,
read-to-clear, or write-one-to-clear behavior automatically provided.

Generate `module.Verilog()` and `AxiLite.Wrapper(module)`. The wrapper uses `aclk`
and active-low `aresetn`, masks all recognized Lite/Stream handshake signals during
reset, and connects synchronous active-high reset to the core. Assert reset through
a clock edge and release it synchronously. No reset synchronizer or CDC is inserted.
The existing `AxisPipeline` compositor remains stream-only; a module exposing both
Lite and stream ports uses its own top-level wrapper, as the capture example does.

## Native register access

After building the map, `bank.CreateNative(name => ReadStatus(name))` creates an
independent functional register file. `Write(address, data, strobes)` returns an
AXI response byte; `Read(address)` returns Data and Response. It applies the same
address and byte-strobe policies. `Reset()` restores reset values. A native status
read requires a callback and fails clearly when none is supplied.

Native calls are transactions, not clock/handshake simulation. They do not
execute hardware `Written` consumers or automatically connect register values
to other native models. Application code must bind those behaviors explicitly.

## Independent cyclic RAM banks

```csharp
var layout = new BankedMemoryLayout(width: 16, banks: 4, rows: 256);
var ram = BankedRam.Create(m, "samples", layout,
    rowAddresses, writeEnables, writeData, readEnables,
    options: new MemoryOptions(MemoryTarget.Xilinx7Series, MemoryStorage.Block));
// Each argument array contains one Expr per bank.
// ram.ReadData[b] is that bank's synchronous output.
```

Banks must be a power of two from 1 to 16. Each has one independent synchronous
read-before-write port, with a shared row address for that bank's read/write.
Logical word mapping is cyclic:

| Logical words | Bank | Row |
|---|---:|---:|
| 0, 4, 8, ... | 0 | 0, 1, 2, ... |
| 1, 5, 9, ... | 1 | 0, 1, 2, ... |
| 2, 6, 10, ... | 2 | 0, 1, 2, ... |
| 3, 7, 11, ... | 3 | 0, 1, 2, ... |

`word = row*Banks + bank`. The helper exposes `BankForWord`, `RowForWord`, and
port widths. Initial contents are supplied in logical-word order and cloned.
Per-bank memory options override the module default; otherwise the existing
Xilinx/Intel/generic policy is inherited by every bank.

Simultaneous accesses to different banks are independent. Two logical accesses
to the same bank require arbitration or different scheduling in the caller;
this helper does not silently add a second port. No automatic CIL array banking,
replication, crossbar, or scheduler integration is claimed.

Disabled reads hold their previous output. Enabled reads outside the logical
row range yield zero; out-of-range writes are ignored. Reset clears read outputs
but retains stored words and suppresses writes. Physical depth is rounded to a
power of two (minimum two rows); extra rows initialize to zero and are protected
from writes. This preserves the bounds contract without placing a read-data
mux ahead of the RAM output register, which can obstruct block-RAM inference.

`NativeBankedRam` provides direct logical-word Read/Write operations without a
hardware graph. Invalid native indexes throw; invalid hardware row addresses
have the zero/ignore behavior above. Native `Clear()` explicitly erases memory;
controller reset is not a memory clear.

## Capture example register map

The example has two banks of 16 rows and stores 32 beats maximum. Each stored
word is 27 bits: DATA [15:0], KEEP [17:16], LAST [18], USER [26:19]. All beats,
including partial TKEEP beats, are stored as received.

| Byte offset | Register | Meaning |
|---:|---|---|
| 0x00 | COMMAND (RW) | Bit0 enable, bit1 clear; other bits stored but ignored |
| 0x04 | HOST_ROW (RW) | Low four bits select a row in both banks; upper bits ignored |
| 0x08 | COUNT (RO) | Captured beat count, 0..32 |
| 0x0C | STATUS (RO) | Bit0 busy, bit1 done (LAST seen), bit2 full |
| 0x10 | BANK0_DATA (RO) | Zero-extended 27-bit stored beat from selected bank0 row |
| 0x14 | BANK1_DATA (RO) | Zero-extended 27-bit stored beat from selected bank1 row |

Typical sequence:

1. Write COMMAND=2 to clear capture state. RAM contents are retained.
2. Write COMMAND=1 to enable capture from index zero.
3. Poll STATUS until done or full. Full without done means the frame did not fit.
4. Write HOST_ROW and read BANK0_DATA/BANK1_DATA for each stored pair.
5. Clear before starting another capture; simply toggling enable does not rearm a
   completed or full capture.

The capture block accepts at most one beat per clock and distributes successive
beats across banks. This serial producer does not exploit all bank ports for
writes; the independent ports permit parallel host reads and future parallel
processing. Capture stops on accepted LAST or capacity and never wraps. Dropping
enable pauses capture; raising it resumes at the same count unless clear occurred.
Clear takes priority over capture and disables both input acceptance and host reads
on that cycle. Host reads are synchronous and permitted only when not busy; while
capture is enabled and unfinished they hold their previous values. An instrument
should stop/finish capture before trusting its readback registers.

HOST_ROW updates are followed by a RAM read clock. In this register bank's
conservative timing, a subsequent read after the HOST_ROW write response sees the
updated row. The integrated test verifies that software sequence. Old RAM contents
beyond COUNT are not valid captured samples, even though they remain readable.

`NativeStreamCapture.TryAccept` supplies functional capture with the same packed
beat format, LAST/capacity stopping and reset-retains-memory policy. It exposes
Count, Done, Full and captured Read(index). `Reset()` discards controller state;
Memory remains available. It is not a clock model or an AXI4-Lite behavioral bridge.

## Verification

```bash
export SDK_ROOT=/path/to/dotnet-sdk
bash tests/run-control-memory.sh   # requires iverilog and vvp
bash tests/map-banked-memory.sh    # requires Yosys with xc7 and cyclonev support
```

The first command runs Debug and optimized graph/native tests, replays six
oracle-checked core RTL benches, and runs a separate integrated AXI wrapper/capture
bench. It exercises AW-first, W-first, simultaneous writes, all strobe masks,
bad/read-only addresses, read snapshots, response stalls, half-write reset,
simultaneous read/write, four-bank collisions/bounds/retention, and capture limits.
The integrated bench independently checks bus operations and stored payloads.

Mapping evidence is resource mapping, not vendor timing or primitive simulation.
The small 13-logical-row Xilinx fixture maps four independent banks after protected
padding. A 256-row Cyclone V fixture is used for block mapping: installed Yosys's
Intel mapper uses an efficiency threshold and maps the tiny 13-row fixture to
logic despite its Quartus `ramstyle` hint. Inference hints are not guarantees.
Arria 10 attributes and RTL are tested; Quartus/M20K mapping remains pending.

## Requirements

| ID | Requirement | Status |
|---|---|---|
| AXIL01 | Independent AW/W capture and exactly one ordered response | Implemented; both arrival orders and simultaneous channels tested |
| AXIL02 | Byte strobes, map validation and error responses | Implemented; masks, zero strobes, RO/invalid addresses tested |
| AXIL03 | Read snapshot, response stability and read/write old-data collision | Implemented; changing status during stall and directed collision |
| AXIL04 | Reset discards half-writes and pending responses | Implemented; core and wrapper tests |
| AXIL05 | Native functional register file and callback status | Implemented; transactional model, no native hardware event execution |
| BANK01 | Independent cyclic banks with logical address helpers | Implemented; four simultaneous bank ports tested |
| BANK02 | Collision, hold, bounds, initialization and reset retention | Implemented; native-array oracle and RTL replay |
| BANK03 | Propagate memory target policy and verify representative mappings | Xilinx/Cyclone V Yosys mapping; Arria 10/vendor qualification pending |
| CAP01 | Preserve complete beats and stop on LAST or capacity without wrap | Implemented; native, graph and RTL tests |
| CAP02 | Clear/reset retains RAM; host read access mutually exclusive with capture | Implemented; capture and integrated register tests |
| CAP03 | AXI-controlled capture and readable status/data | Implemented; integrated example |
| DMA01 | Full AXI4 burst master, address/length commands and stream transfers | Planned; not implemented by local capture |
| DMA02 | 4-KiB boundary splitting, response errors, outstanding beats and reset recovery | Planned; required before claiming AXI burst DMA |
| DMA03 | Host buffers, descriptors, cache ownership/coherency and platform driver | Planned; platform-specific integration |
| BANK04 | Automatic array partitioning and memory-aware scheduling | Planned; current banking is explicit structural construction |

Protocol background: [AMD AXI Reference Guide](https://www.amd.com/content/dam/xilinx/support/documents/ip_documentation/axi_ref_guide/latest/ug1037-vivado-axi-reference-guide.pdf).
