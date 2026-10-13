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
