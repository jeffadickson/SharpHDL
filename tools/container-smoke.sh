#!/usr/bin/env bash
set -euo pipefail
cd /workspace
echo 'Checking provisioned toolchain'
for command in dotnet python3 node iverilog vvp verilator yosys nextpnr-ice40 icepack; do
  command -v "$command" >/dev/null || { echo "MISSING: $command" >&2; exit 1; }
done
dotnet --version
iverilog -V | head -n 2
verilator --version
yosys -V
nextpnr-ice40 --version
echo 'Building Core, HLS, Simulation, compatibility library, tests and live host'
dotnet build src/SharpHDL.Core/SharpHDL.Core.csproj -c Release
dotnet build src/SharpHDL.HLS/SharpHDL.HLS.csproj -c Release
dotnet build src/SharpHDL.Simulation/SharpHDL.Simulation.csproj -c Release
dotnet build tests/Tests.csproj -c Release
dotnet build tools/SharpHDL.Live/SharpHDL.Live.csproj -c Release
dotnet test tests/SharpHDL.UnitTests/SharpHDL.UnitTests.csproj -c Release --logger 'trx;LogFileName=container-unit.trx'
echo 'Executing existing reference simulation regression'
dotnet run --project tests/Tests.csproj -c Release --no-build
test -s generated/counter.v
echo 'Checking real RTL parsing and synthesis'
iverilog -g2012 -tnull generated/counter.v
yosys -Q -T -p 'read_verilog generated/counter.v; hierarchy -check; proc; opt; stat' >/tmp/sharphdl-yosys.log
echo 'PASS: container toolchain, .NET build/unit tests, simulation regression, Icarus and Yosys'
