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
ram = [0] * 256
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
        if address < 256:
            ram[address] = regs[rd]
        elif address == 0xff02:
            tx.append(regs[rd] & 0xff)
        else:
            raise AssertionError(f"unexpected store to {address:04X}")
    elif opcode == 9:  # synchronous scratch RAM load, ISA-level smoke model
        address = (regs[ra] + sext(word & 0x3f, 6)) & 0xffff
        assert address < 256
        if rd: regs[rd] = ram[address]
    elif word == 0xf000:
        halted = True
        break
    else:
        raise AssertionError(f"unsupported opcode {word:04X} in smoke program")

expected = b"Hello, world!\r\n"
assert halted, "program must halt"
assert ram[0] == 72, "RAM round trip was not exercised"
assert bytes(tx) == expected, f"{bytes(tx)!r} != {expected!r}"
print(f"PASS: {len(program)} words, {len(tx)} expected UART bytes, HALT")
print("Scope: assembly/ISA only. RTL handshake and physical 8N1 serial tests still required.")
