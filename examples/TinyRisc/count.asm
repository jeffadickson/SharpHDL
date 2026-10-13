; Sum 5 + 4 + 3 + 2 + 1, store/read scratch RAM, show 15 on LEDs.
LDI R1, 5
LDI R2, 0
loop: ADD R2, R2, R1
ADDI R1, R1, -1
BNE R1, R0, loop
ST R2, R0, 10
LD R3, R0, 10
LDI R4, -256 ; FF00 LED word address
ST R3, R4, 0
HALT
