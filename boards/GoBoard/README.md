# Go Board classroom starter

This is an original SharpHDL classroom target for the Nandland Go Board,
iCE40HX1K in VQ100, with a 25 MHz oscillator. Pin numbers were checked against
Nandland's published constraints:
https://nandland.com/goboard/Go_Board_Constraints.pcf
Display polarity and digit numbering follow the board maker's documentation:
https://nandland.com/project-5-seven-segment-display/

`StudentLogic.cs` is the learner's editable file. Its `b + constant` expression
feeds the right-hand digit. The core keeps a four-bit input number B and an
eight-bit counter. Displays show hexadecimal, not decimal: A means ten and
F means fifteen. Start the class with B=0..7 before introducing hexadecimal.

| Button | Action |
|---|---|
| 1 | Clear B and the counter; run/pause and display mode are unchanged |
| 2 | Increment B once per debounced press, modulo 16 |
| 3 | Toggle the counter between run and pause |
| 4 | Toggle between arithmetic and counter displays |

Arithmetic mode: left digit B; right digit (B+2) modulo 16; four LEDs show B in
binary. Counter mode: the two digits show the eight-bit count in hexadecimal;
LEDs show its low four bits. It increments once per second while running; its
low LED consequently changes once per second, for a two-second blink period.
The tick divider runs continuously, so the first increment after Run may occur
in less than one second. This is not a precision stopwatch/reaction timer.

The teacher-owned `classroom_top.v` provides two-flop input synchronization,
10 ms debouncing, FPGA startup reset, and active-low seven-segment encoding.
`ClassroomCore.cs` generates the application logic and uses clock enables;
no button or divided counter signal becomes a clock. The startup counter's
initial state relies on iCE40 FPGA initialization support. Changing FPGA family
requires review of that reset scheme. Digital simulation does not model
metastability or validate the physical switches.

## Build and check

Use .NET 8 SDK, Python 3, Icarus, Yosys, nextpnr-ice40, and IceStorm/icepack.
OSS CAD Suite distributes the FPGA tools for common desktop platforms:
https://github.com/YosysHQ/oss-cad-suite-build
The provided shell scripts assume a POSIX shell; the bundled Python compiler
build is cross-platform. Teacher setup is required for shell and USB drivers.

From the project root:

```sh
SDK_ROOT=/path/to/dotnet-sdk tests/run-three-languages.sh
boards/GoBoard/build.sh
```

After changing the constant in `StudentLogic.cs`, use the lesson build with
what the child predicts the constant should be. For a change to add three:

```sh
SDK_ROOT=/path/to/dotnet-sdk boards/GoBoard/lesson.sh 3
```

This compiles the changed source, checks its simulated behavior against the
requested addend, checks the emitted core in Icarus, and builds a fresh FPGA
image. It does not silently change the expected answer to match the source.
The full regression suite remains a baseline test of the supplied add-two
example; after purposeful edits, use the lesson build or update expectations.

The first command builds the language examples, tests them, and emits the
25 MHz ClassroomCore. The second synthesizes, places/routes at 25 MHz, and
packs `build/goboard/classroom.bin` and refreshes the checked-in image, reports
and SHA-256 manifest in `generated/goboard`. This script does not program hardware.
The teacher should install and test the board's programming/USB interface on
one laptop and confirm the device revision before distributing a class image.

To exercise the board shell before connecting a board:

```sh
iverilog -g2012 -s tb -o build/goboard_shell \
  generated/three-languages/ClassroomCore.v \
  boards/GoBoard/classroom_top.v boards/GoBoard/classroom_top_tb.v
vvp build/goboard_shell
```

`classroom_top_tb.v` uses a shortened debounce interval for tests; the real
build defaults to 250000 clocks. The generated core is tested separately with
a short tick interval so run/pause, wrap, clear and display modes can be checked
without simulating millions of clocks per button action.

## Actual implementation result

The submitted build passed Yosys iCE40 synthesis, nextpnr placement/routing at
25 MHz, and IceStorm bitstream packing. The mapped design uses 230 of 1280
logic cells (17%), no block RAM, and 23 I/O. The final reported internal clock
estimate is 154.63 MHz; the requested target was only 25 MHz. This estimate is
not a measured board clock limit and is not an input/output timing guarantee.
No hardware was connected or programmed. Physical startup, button feel, USB
programming and display behavior still require one pilot-board check.

The built image and reports are in `generated/goboard`, including a source/image
manifest. Only the classroom starter was placed and routed for this small FPGA.
The much larger floating-point and packet examples are not implied to fit it.
Do not begin a children's class with those advanced numerical blocks.
