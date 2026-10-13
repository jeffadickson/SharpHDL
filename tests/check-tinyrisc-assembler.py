from pathlib import Path
import importlib.util
p=Path(__file__).resolve().parents[1]/'examples/TinyRisc/assemble.py'
s=importlib.util.spec_from_file_location('assembler',p);m=importlib.util.module_from_spec(s);s.loader.exec_module(m)
assert m.assemble('LDI R1, -256\nADD R2,R1,R0\nADDI R3,R2,-1\nHALT')==[0x8300,0x0440,0x76bf,0xf000]
assert m.assemble('loop: NOP\nBNE R1,R0,loop')==[0xf001,0xc23e]
assert m.assemble('JALR R7,R1')==[0xde40]
for text in ['LDI R8,1','LDI R1,256','ADDI R1,R0,32','LD R1,R0,-33','BEQ R1,R0,missing','x: NOP\nx: HALT','HALT 1','ADD R1,R2','BOGUS','']:
 try:m.assemble(text)
 except ValueError:pass
 else:raise AssertionError(text)
print('PASS assembler: 3 golden encodings and 10 rejection cases')
