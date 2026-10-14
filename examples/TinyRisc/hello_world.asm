; TinyRisc16 UART byte-interface Hello World.
; FF02 is the UART TX holding register, written through ST.
; This exercises the CPU's existing tx_valid/tx_ready handshake.
; It does not independently drive a physical UART TX pin.
LDI R2, -254 ; 0xFF02: UART transmit register
LDI R1, 72 ; H
ST R1, R0, 0 ; prove scratch RAM writes
LD R1, R0, 0 ; and synchronous scratch RAM reads
ST R1, R2, 0
LDI R1, 101 ; e
ST R1, R2, 0
LDI R1, 108 ; l
ST R1, R2, 0
ST R1, R2, 0 ; l again
LDI R1, 111 ; o
ST R1, R2, 0
LDI R1, 44 ; ,
ST R1, R2, 0
LDI R1, 32 ; space
ST R1, R2, 0
LDI R1, 119 ; w
ST R1, R2, 0
LDI R1, 111 ; o
ST R1, R2, 0
LDI R1, 114 ; r
ST R1, R2, 0
LDI R1, 108 ; l
ST R1, R2, 0
LDI R1, 100 ; d
ST R1, R2, 0
LDI R1, 33 ; !
ST R1, R2, 0
LDI R1, 13 ; CR
ST R1, R2, 0
LDI R1, 10 ; LF
ST R1, R2, 0
HALT
