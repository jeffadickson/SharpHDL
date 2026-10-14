# TinyRisc16 UART Hello World milestone

## Current deliverable

[`hello_world.asm`](hello_world.asm) is a real TinyRisc16 program using the already implemented `ST` memory-mapped transmit port at word address `0xFF02`. It emits the bytes `Hello, world!\r\n`, then HALTs. Because the CPU stalls stores until `tx_ready`, each byte must be accepted exactly once before the next instruction proceeds.

The CPU already has a separate instruction ROM, 256 x 16-bit scratch RAM, and byte-stream `tx_valid`/`tx_data`/`tx_ready` signals. The program currently targets that *byte handshake interface*, **not** a real serial TX pin; a serializer is required for a physical UART.

## Acceptance gates before closing issue #21

1. Assemble `hello_world.asm` with `examples/TinyRisc/assemble.py` and feed its words into `TinyRisc.Build`; no assembler errors, no bounds fault.
2. Exercise tx_ready high and with randomized backpressure. Record TX handshakes and assert exactly `48 65 6C 6C 6F 2C 20 77 6F 72 6C 64 21 0D 0A`, no drops/duplicates, then HALT without fault.
3. Integrate an independently tested 8N1 UART serializer with `tx_valid`/`tx_ready` at a configurable clock/baud divisor. Check idle-high, start, LSB-first eight data bits, stop, bit period, and stability under stalls.
4. Decode serialized output using an independent UART receiver/scoreboard and assert the same literal `Hello, world!\r\n`.
5. Demonstrate scratch RAM read/write within the same system using a separate test program or expand Hello World to fetch a stored string from RAM. The instruction ROM is already separate from scratch RAM.
6. Run simulator-versus-generated-Verilog differential tests and a Yosys synthesis check, retaining commands, tool versions, and logs in CI.
7. Expose run/reset, PC, instruction, RAM and UART bytes in a CLI and the browser/VS Code debug service, including trace export.
8. Update the feature parity matrix and only close issue #21 with links to passing reproducible evidence.

## Proposed system topology

```text
assembled hello_world.asm -> 16-bit instruction ROM
                                  |
                             TinyRisc16 CPU <--> 256x16 scratch RAM
                                  |
                       FF02 TX ready/valid byte port
                                  |
                          UART serializer 8N1
                                  |
                            serial TX pin
                                  |
                      independent UART decoder -> "Hello, world!"
```

## Debugging policy

VS Code is the **primary integrated developer UI**, through the Debug Adapter Protocol and Portable PDB source mapping. The browser workbench remains available for education, remote or offline demonstration, and waveform/dataflow inspection. Both must use a common simulation/debug service and avoid divergent breakpoint or stepping semantics.

## Status (2026-10-14)

Assembly source committed. CPU memory and byte transmit interface pre-exist. A working serial UART integration, automated Hello World test, and source-level debugger acceptance have **not** yet been verified. This document is a completion checklist, not a completion claim.
