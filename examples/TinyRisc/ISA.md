# TinyRisc16: a processor built with SharpHDL

This is an original, small load/store processor example. It is not PDP-11, Intel
4004, RISC-V, or RISCue compatible. TinyRisc.cs constructs registers, gates,
selectors, instruction ROM and synchronous RAM through the structural API.
It does not compile a software emulator into a sequential CIL engine.

## Machine state

Eight 16-bit registers; R0 always reads zero and discards writes. Sixteen-bit
word-addressed PC, 16-bit instruction register, and three control phases. Separate
instruction ROM (1..256 words) and 256-word scratch RAM. RAM powers up zero;
synchronous reset clears registers/control/LEDs but retains RAM. Arithmetic wraps
modulo 65536. No flags register, carry instruction, interrupts, stack convention,
privilege, caches, multiply/divide, pipeline or branch prediction.

The instruction ROM is supplied to Build as a ushort array. The assembler emits
hex words; Program.cs reads them before hardware elaboration. This release does
not provide a runtime ROM loader or a compiler for C programs targeting this ISA.

## Instruction encoding

Bit15 is most significant; all instructions are one 16-bit word. Register indexes
are three bits. Hex opcode occupies bits15:12. Reserved encodings trap.

| Opcode | Assembly | Encoding below opcode | Effect |
|---|---|---|---|
| 0 | ADD rd,ra,rb | rd[11:9], ra[8:6], rb[5:3], 000[2:0] | rd = ra + rb |
| 1 | SUB rd,ra,rb | Same register format | rd = ra - rb |
| 2 | AND rd,ra,rb | Same | Bitwise AND |
| 3 | OR rd,ra,rb | Same | Bitwise OR |
| 4 | XOR rd,ra,rb | Same | Bitwise XOR |
| 5 | SHL rd,ra,rb | Same | Logical left shift by rb & 15 |
| 6 | SHR rd,ra,rb | Same | Logical right shift by rb & 15 |
| 7 | ADDI rd,ra,imm | rd[11:9], ra[8:6], signed immediate[5:0] | rd = ra + sign_extend(imm) |
| 8 | LDI rd,imm | rd[11:9], signed immediate[8:0] | rd = sign_extend(imm), range -256..255 |
| 9 | LD rd,ra,offset | rd[11:9], ra[8:6], signed offset[5:0] | rd = memory[ra + offset] |
| A | ST rs,ra,offset | rs[11:9], ra[8:6], signed offset[5:0] | memory[ra + offset] = rs |
| B | BEQ lhs,rhs,offset | lhs[11:9], rhs[8:6], signed offset[5:0] | If equal, PC = PC + 1 + offset |
| C | BNE lhs,rhs,offset | Same branch format | If unequal, PC = PC + 1 + offset |
| D | JALR rd,ra | rd[11:9], ra[8:6], 000000[5:0] | rd = old PC + 1; PC = old ra |
| E | Reserved | Any | Fault |
| F | HALT / NOP | Exactly F000 / F001 | Stop until reset / no operation |

Offsets range -32..31. PC/address arithmetic wraps at16 bits; bounds are checked
on the resulting word address. No alignment rule is needed because all addresses
are words. Untaken branches advance PC by1. JALR reads the old source even when
rd equals ra. Writes to R0 are discarded but LD R0 still performs the memory or
peripheral access. HALT retires and advances PC before stopping. Faulting
instructions do not retire, advance PC or write architectural state. A branch
outside ROM retires normally, then the subsequent fetch faults at its target.

## Memory-mapped I/O

| Word address | Read | Write |
|---|---|---|
| 0000..00FF | Scratch RAM | Scratch RAM |
| FF00 | LED/output latch | Set 16-bit LED/output latch |
| FF01 | 16-bit switches input | Fault |
| FF02 | Fault | Transmit low8 bits; wait for tx_ready |
| FF03 | Receive zero-extended byte; wait for rx_valid | Fault |
| FF04 | bit0 rx_valid; bit1 tx_ready; bits15:2 zero | Fault |
| Other | Fault | Fault |

RX/TX are synchronous ready/valid **byte** interfaces. A transfer occurs once at
a rising edge when both signals are high. These ports need a separate UART
serializer/deserializer for serial pins. The consumer/producer must obey hold
rules while stalled. Switching run low cannot cancel an in-flight transfer.
Reset overrides instruction effects; system integration must also suppress/reset
external peripheral handshakes during reset. I/O inputs must already be clock
synchronous; physical buttons need synchronization and debouncing.

## Cycles and debug

Phase0 is fetch or paused; phase1 executes; phase2 completes a RAM load.
Non-RAM-load instructions take2 clocks from fetch acceptance to retirement,
including the fetch edge and execute edge. A scratch load takes3 clocks. UART
access adds as many execute wait edges as necessary; there is no watchdog.

run=1 fetches continuously. run=0 pauses at the next boundary, completing any
in-flight instruction first. A rising step input while paused accepts one
instruction. Holding step high cannot repeat; edges while busy are ignored.
HALT/fault are sticky until synchronous reset. Outputs expose PC, instruction,
phase, all eight registers, LED latch, fault, halted, retired and last_pc.
Retired pulses for one clock; last_pc identifies the completed instruction.
These are hardware debug signals, not a GDB/debug transport implementation.

## Assemble and run

From the project root with .NET8 and Icarus installed:

```sh
export DOTNET_ROOT=/path/to/dotnet
bash tests/run-tinyrisc.sh
bash tests/synthesize-tinyrisc.sh  # requires Yosys
```

count.asm computes 5+4+3+2+1, stores15 at RAM word10, loads it into R3 and writes15
to FF00. Its LED pattern is 0000 0000 0000 1111. An educational front panel can
show decimal15 alongside those same bits; the hardware need not be a decimal CPU.
The assembler supports labels, signed decimal/0x literals, semicolon comments,
case-insensitive mnemonics/registers, and case-sensitive labels. Branch labels
become offsets relative to the next instruction. Invalid encodings/ranges fail
with source-line diagnostics. No pseudo-instructions or linker are provided.

The independent interpreter in Program.cs defines architectural effects with
ordinary integer operations. It compares registers, PC, traps and I/O at
retirement; generated benches check the hardware graph against RTL each edge.
Synthesized CountRun is also simulated against its original per-edge bench.
This is tested prototype hardware, not a claim of general correctness or a
placed/routed board implementation.
