#!/usr/bin/env python3
"""Two-pass TinyRisc16 assembler. Decimal or 0x hexadecimal literals; word addresses."""
import re,sys
from pathlib import Path
OPS={'ADD':0,'SUB':1,'AND':2,'OR':3,'XOR':4,'SHL':5,'SHR':6,'ADDI':7,'LDI':8,'LD':9,'ST':10,'BEQ':11,'BNE':12,'JALR':13}
def assemble(text):
 labels={};lines=[]
 for number,line in enumerate(text.splitlines(),1):
  line=line.split(';')[0].strip()
  if ':' in line:
   label,line=line.split(':',1);label=label.strip()
   if not re.fullmatch(r'[A-Za-z_]\w*',label) or label in labels:raise ValueError(f'line {number}: invalid/duplicate label')
   labels[label]=len(lines);line=line.strip()
  if line:lines.append((number,line))
 if not 1<=len(lines)<=256:raise ValueError('Program must contain 1..256 words')
 def reg(x):
  if not re.fullmatch(r'R[0-7]',x.upper()):raise ValueError('register must be R0..R7')
  return int(x[1])
 def imm(x,bits,pc,relative=False):
  v=labels[x]-(pc+1) if relative and x in labels else int(x,0)
  if not -(1<<(bits-1))<=v<(1<<(bits-1)):raise ValueError(f'immediate must fit signed {bits} bits')
  return v&((1<<bits)-1)
 result=[]
 for pc,(line,text) in enumerate(lines):
  t=text.replace(',',' ').split();op=t[0].upper();a=t[1:]
  try:
   if op in ('HALT','NOP'):
    if a:raise ValueError('no operands expected')
    word=0xf000+(op=='NOP')
   elif op=='LDI':
    if len(a)!=2:raise ValueError('expected rd, immediate')
    word=0x8000|(reg(a[0])<<9)|imm(a[1],9,pc)
   elif op=='JALR':
    if len(a)!=2:raise ValueError('expected rd, ra')
    word=0xd000|(reg(a[0])<<9)|(reg(a[1])<<6)
   else:
    if op not in OPS or len(a)!=3:raise ValueError('unknown instruction or wrong operand count')
    code=OPS[op];tail=reg(a[2])<<3 if code<=6 else imm(a[2],6,pc,op in ('BEQ','BNE'))
    word=(code<<12)|(reg(a[0])<<9)|(reg(a[1])<<6)|tail
   result.append(word)
  except (ValueError,KeyError) as e:raise ValueError(f'line {line}: {e}') from e
 return result
if __name__=='__main__':
 if len(sys.argv)!=3:raise SystemExit('usage: assemble.py source.asm output.hex')
 try:words=assemble(Path(sys.argv[1]).read_text())
 except ValueError as e:raise SystemExit(str(e))
 Path(sys.argv[2]).write_text(''.join(f'{x:04x}\n' for x in words))
 print(f'Assembled {len(words)} words')
