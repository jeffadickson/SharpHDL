# Packet checksum: measured core comparison

Device: iCE40 HX1K, VQ100; 25 MHz target; placement seeds 1, 2, 3.
Automatically assigned pins: routed core estimates, not board measurements.

| Implementation | LUT4 | Flip-flops | Packed logic cells | Carry cells | RAM | DSP | Routed Fmax range (MHz) |
|---|---:|---:|---:|---:|---:|---:|---:|
| SharpHDL committed-state baseline | 54 | 51 | 90 | 15 | 0 | 0 | 142.86–156.81 |
| SharpHDL accepted-state optimized | 35 | 34 | 55 | 15 | 0 | 0 | 202.06–202.06 |
| Handwritten RTL | 35 | 34 | 55 | 15 | 0 | 0 | 202.06–202.06 |

All three versions: latency **1 clock**, sustained **1 word/clock**. At the tested
25 MHz constraint this is **25 million 16-bit payload words/s = 400 Mbit/s**,
before transport overhead; it is calculated throughput, not a measured link rate.

The independent traffic oracle passes for all three RTL versions and all three mapped
iCE40 cell netlists:

```text
PASS packet traffic: cycles=12000 accepted=4051 received=4049 reset_discarded=2 packet_ends=305 stalled_cycles=6554 replacements=3655; saturated II=1 latency=1
```

Fmax is nextpnr static timing for the routed register paths, not a measured
silicon limit. Inputs and outputs have no physical I/O delay budget. A board
wrapper and endpoint timing constraints are required before a system-rate claim.

Tool versions:
```text
Yosys 0.33 (git sha1 2584903a060)
"nextpnr-ice40" -- Next Generation Place and Route (Version 0.6-3build5)
```
