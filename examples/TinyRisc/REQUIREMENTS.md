# TinyRisc16 requirements and acceptance

| ID | Requirement | Implementation / evidence |
|---|---|---|
| CPU01 | Build CPU using explicit SharpHDL hardware | TinyRisc.cs; separate software reference in Program.cs |
| CPU02 | 16-bit words, eight registers, immutable R0, wrapping arithmetic | RandomAlu native oracle; all register outputs compared |
| CPU03 | Document every instruction bit and reserved encoding | ISA.md; IllegalOpcode, IllegalEncoding and assembler golden encodings |
| CPU04 | Conditional branches and indirect link jump | CountRun, Branches, JumpAlias including rd=ra |
| CPU05 | Synchronous scratch memory with explicit reset policy | CountRun store/load; ResetRetainsRam runs twice with retained contents |
| CPU06 | Finite instruction ROM and address faults | RomBounds and BadAddress; trap suppresses retirement |
| CPU07 | Memory-mapped output, input and byte-stream UART interface | PeripheralStalls with varying ready/valid, data and exact transfer checks |
| CPU08 | Pause at instruction boundary, edge-triggered instruction step | CountStep and stepped PeripheralStalls; held request cannot repeat |
| CPU09 | Observable PC/IR/registers/phase/retirement/fault | Commented RTL ports and per-edge RTL comparisons |
| CPU10 | Independent architectural model plus HDL execution | 11 benches; generated/tinyrisc/verification.txt and test.log |
| CPU11 | Portable two-pass assembler with bounds diagnostics | assemble.py; 3 golden vectors and 10 invalid-source checks |
| CPU12 | Synthesis and simulation after synthesis | CountRun Yosys check -assert and synthesized RTL simulation |
| CPU13 | State limits plainly | No board timing claim, runtime ROM loader, interrupt, binary UART pin, GDB, pipeline or ISA compatibility claim |
| EDUCPU01 | Keep simpler decimal-first children's computer separate | EDUCATIONAL_COMPUTER_HANDOFF.txt; design/implementation intentionally for next chat |

Actual acceptance records are retained under generated/tinyrisc. Test counts do
not imply exhaustive ISA or temporal verification. CPU07 uses a byte interface;
physical UART integration is deferred. CPU08 has no debugger writes to registers
or memory; reset is the way to resume after HALT/fault.
