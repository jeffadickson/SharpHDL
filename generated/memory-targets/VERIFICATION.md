# Memory targets and native execution verification

Toolchain: .NET SDK 8.0.100, Icarus Verilog 12.0, Yosys 0.33.

- `tests/run-memory-targets.sh`: PASS in Debug and optimized configurations.
- 15,464 assertions per configuration; independent array oracle across 11 RAM profiles.
- 14 RTL replays per configuration: 11 RAM profiles and three sequential-array targets.
- Reset retains memory, suppresses writes and clears output; read/write collisions return old data.
- Non-power-of-two bounds, initialization ownership, module defaults, per-RAM override and invalid policies checked.
- Native original methods: packet values/boundaries/wrapping, finite and empty input, consumer exception propagation, enumerator cleanup and disposed-input rejection.
- One million native array updates and one million native stream tokens checked for correct results.
- Optimized observation on this host: 0.002396 s for array updates; 0.020199 s for stream tokens. No speed threshold or zero-overhead claim; results depend on host/JIT/load.
- `tests/map-memory-targets.sh`: PASS, one RAMB18E1 for Xilinx7Series Block and one MISTRAL_M10K for IntelCycloneV Block.
- PASS all seven software/RTL regression scripts: core, behavioral, advanced, F#, three-languages, learning and TinyRisc.
- PASS semantic net-naming checks.

Vivado and Quartus were unavailable. Arria 10 M20K mapping, vendor primitive
simulation, vendor timing/fit and physical-board validation are **not verified**.
Open-source mapped resource counts do not replace those checks. Vendor scripts
are provided under tests/vendor-memory but were not executed here.
