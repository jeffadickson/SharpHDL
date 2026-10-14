# Memory model

SharpHDL 0.11 adds explicit synchronous single-port RAM. It participates in the
same simulator and Verilog backend as registers and expressions. General arrays remain unsupported. Explicit `BuildSequential` additionally lowers
one owned `uint[]` parameter to a 32-bit synchronous RAM, with bounds faults and
a read wait state. See [the sequential contract](SEQUENTIAL_CIL.md).

```csharp
var address = m.Input("address", 8);
var writeData = m.Input("write_data", 16);
var writeEnable = m.Input("write_enable", 1);
var readEnable = m.Input("read_enable", 1);
var ram = SynchronousRam.Create(m, "packets", 16, 256,
    address, writeEnable, writeData, readEnable);
m.Drive(m.Output("read_data", 16), ram.ReadData);
```

This creates 256 words of 16 bits, addressed by an unsigned eight-bit bus. All
accesses occur at the same rising clock edge. There is one shared address port.

| Condition at rising edge | Output after edge | Stored word after edge |
|---|---|---|
| read=1, write=0, valid address | Selected old word | Unchanged |
| read=0, write=1, valid address | Holds previous output | write_data |
| read=1, write=1, valid address | Selected old word | write_data |
| read=0, write=0 | Holds previous output | Unchanged |
| reset=1 | Zero | Retained; write suppressed |
| Out-of-range address | Zero if read enabled, otherwise hold | No write |

Read-before-write means that simultaneous access returns the value from before
the edge. Reset clears the output register, not the storage array. Clearing every
cell at reset would often prevent block-RAM inference; use a deliberate scrub
controller when that is required. Simulator instances own independent cell arrays.

Optional `BigInteger[] initial` must contain exactly depth words, each representable
in width bits, and is copied at construction. Without it, every word initializes
to zero. Initialization is a configuration/power-up contract expressed by Verilog
initial assignments, not an ordinary reset operation. Target flows must support
that initialization; ASIC flows need a different implementation contract.

Width is 1..4096 bits, depth 1..65536, and address width at most 32 with enough
patterns for depth. Practical resource limits are much smaller. Depth need not
be a power of two. Addresses and write signals may be Expr computations, including
outputs of compiled scalar CIL functions. Gate enables with stream acceptance
when requests must happen exactly once; the RAM interface itself is not AXI.

The generated Verilog declares the memory and explains width, depth, initialization,
read timing, collision behavior and reset. The graph dump lists cell initialization
and write connections. This is inferred RTL memory, not vendor-specific IP.

## Evidence and limits

Independent array-model tests exercise initialized reads, writes, simultaneous
read/write, read-disable hold, reset retention and invalid addresses over 600
cycles for depths 13 and 256. Both generated RTL benches pass. The 256x16 instance
survives Yosys memory collection as one memory and maps to one iCE40 SB_RAM40_4K;
the mapped primitive simulation also passes the same trace. Different shapes and
targets may map to several blocks or to logic. No timing or board claim is made.

Dual-port RAM, byte enables, asynchronous reads, configurable collision modes,
external DDR, automatic port arbitration and general CIL arrays are future work.
Existing Lookup remains a constant ROM and the existing FIFO remains register-based.

## Xilinx and Intel/Altera inference targets

Memory behavior is independent of the physical inference policy. Choose a default
for a module or override it on an individual `SynchronousRam.Create` call:

```csharp
var policy = new MemoryOptions(MemoryTarget.Xilinx7Series, MemoryStorage.Block);
var m = new Module("PacketMemory", policy);
// All RAMs in this module inherit policy unless options: is supplied to Create.
```

`MemoryOptions` is immutable. `SynchronousRam.Options` exposes the effective policy.
The sequential CIL frontend accepts the same policy via
`SequentialOptions.MemoryOptions`, so the existing owned uint[] lowering can target
these memories too. `TinyRisc.Build(program, name, memoryOptions)` also accepts
the policy for its scratch RAM (the instruction lookup ROM is unchanged). Native software still uses ordinary arrays; see
[NATIVE_EXECUTION.md](NATIVE_EXECUTION.md).

| Target | Auto | Block | Distributed | Registers |
|---|---|---|---|---|
| Generic | Existing inferred RTL, no attribute | Rejected: select target | Rejected | Rejected |
| Xilinx7Series | No attribute | `ram_style="block"` | `ram_style="distributed"` | `ram_style="registers"` |
| IntelCycloneV | No attribute | `ramstyle="M10K"` | Rejected: MLAB contract not implemented | `ramstyle="logic"` |
| IntelArria10 | No attribute | `ramstyle="M20K"` | Rejected: MLAB contract not implemented | `ramstyle="logic"` |

These are **inferred RTL backends**, not instantiations of XPM or altsyncram IP.
The selected tool may use extra logic or multiple blocks to preserve enables,
initialization, bounds checks, output reset and old-data collisions. A shared
logical address does not require one physical port: a backend can use separate
read and write ports tied to the same address. Never assume a same-port vendor
mode is equivalent merely because it is called single-port RAM.

The RTL contract does not change with the attribute. We deliberately never emit
`no_rw_check`: losing collision guarantees would change observable behavior.
Unsupported enum values/policy combinations are rejected, and initial contents
are copied so later mutation of the caller's array cannot change the design.
The existing broad width/depth limits are logical limits, not a device capacity
promise. Selecting Block is an implementation request; a final mapping report
must confirm the actual hardware. This API does not certify device fit or timing.

## Target verification status

| Check | Result |
|---|---|
| .NET graph versus independent array oracle | 11 RAM configurations, 700 edges each; initial data, read-before-write, enables, reset retention and bounds |
| Generated RTL replay | All 11 RAM configurations plus three sequential array targets, Debug and optimized CIL |
| Xilinx 7-series open-source mapping | Yosys maps the 256x16 fixture to one RAMB18E1 |
| Cyclone V open-source mapping | Yosys synth_intel_alm maps the 256x16 fixture to one MISTRAL_M10K |
| Arria 10 M20K | Attribute emission and RTL behavior tested; technology mapping pending Quartus |
| Vivado / Quartus | Not installed in this environment; vendor synthesis, fitter and vendor primitive simulation remain unverified |

Run `tests/run-memory-targets.sh` with DOTNET_ROOT and Icarus on PATH, then
`tests/map-memory-targets.sh` with Yosys on PATH. Mapping evidence is in
`generated/memory-targets/mapping`. Open-source mapping checks resource types;
it is **not** vendor primitive simulation or physical FPGA validation.
Optional Vivado and Quartus scripts are in `tests/vendor-memory`. The Quartus
script requires an exact installed device and leaves the fitter resource/collision
review explicit. These vendor scripts are provided for subsequent validation and
have not been run here.

Targets currently apply only to SynchronousRam and sequential owned arrays.
`Expr.Lookup` remains combinational ROM; forcing it into synchronous block memory
would change latency, so ROM target selection is not silently applied. Multiport
interfaces, byte writes, MLAB, UltraRAM, external DDR, automatic memory-port
scheduling in stream pipelines, and automatic BRAM-backed FIFOs remain future work.

| Requirement | Status |
|---|---|
| MEM03: immutable module/per-RAM target policy, inherited by sequential arrays | Implemented; default/override/invalid-policy tests |
| MEM04: preserve all memory semantics across targets | Independent oracle and RTL tests for all accepted profiles; no collision relaxation |
| MEM05: Xilinx and Intel/Altera inference attributes with family-specific block choice | Xilinx7Series, CycloneV M10K, Arria10 M20K implemented; MLAB explicitly rejected |
| MEM06: verify mapping separately from functional correctness | Xilinx and Cyclone V open-source mapping gates; vendor flows and Arria 10 mapping pending |
| MEM07: preserve fast native execution of memory algorithms | Native uint[] plus original method; sequential target cross-checks; see NATIVE03 |

Attribute references: [AMD RAM_STYLE](https://docs.amd.com/r/en-US/ug912-vivado-properties/RAM_STYLE),
[Intel RAM inference controls](https://www.intel.com/content/www/us/en/docs/programmable/683082/25-1/controlling-ram-inference-and-implementation.html),
[Cyclone V memory handbook](https://docs.altera.com/r/docs/683375/current/cyclone-v-device-handbook-volume-1-device-interfaces-and-integration/ram-blocks-operations).

## Control and banked capture

[AXI4-Lite control, banked memory and local stream capture](CONTROL_AND_MEMORY.md)
adds independent Lite channels, byte-strobed register access, cyclic RAM banks,
and an integrated capture/readback example with native functional models.
Requirements AXIL01–AXIL05, BANK01–BANK04, CAP01–CAP03 and DMA01–DMA03 distinguish
implemented local transfers from the remaining full AXI burst DMA work.
