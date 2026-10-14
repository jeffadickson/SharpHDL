#!/usr/bin/env python3
"""Quick, dependency-free assembly/ISA smoke test; NOT HDL or serial-UART verification."""
from pathlib import Path
import sys
sys.path.insert(0, str(Path(__file__).resolve().parent))
from assemble import assemble

ROOT = Path(__file__).resolve().parent
program = assemble((ROOT / "hello_world.asm").read_text())
regs = [0] * 8
tx = []
halted = False

def sext(value, width):
    return value - (1 << width) if value & (1 << (width - 1)) else value

for word in program:
    opcode = word >> 12
    rd = (word >> 9) & 7
    ra = (word >> 6) & 7
    if opcode == 8:  # LDI
        if rd: regs[rd] = sext(word & 0x1ff, 9) & 0xffff
    elif opcode == 10:  # ST
        address = (regs[ra] + sext(word & 0x3f, 6)) & 0xffff
        if address != 0xff02:
            raise AssertionError(f"unexpected store to {address:04X}")
        tx.append(regs[rd] & 0xff)
    elif word == 0xf000:
        halted = True
        break
    else:
        raise AssertionError(f"unsupported opcode {word:04X} in smoke program")

expected = b"Hello, world!\r\n"
assert halted, "program must halt"
assert bytes(tx) == expected, f"{bytes(tx)!r} != {expected!r}"
print(f"PASS: {len(program)} words, {len(tx)} expected UART bytes, HALT")
print("Scope: assembly/ISA only. RTL handshake and physical 8N1 serial tests still required.")
