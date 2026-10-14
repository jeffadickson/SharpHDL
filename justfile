# SharpHDL developer entry points. just is a command runner, not the compiler.
set positional-arguments

default:
    @just --list

doctor:
    python3 tools/dev.py doctor

setup:
    python3 tools/dev.py setup

build:
    dotnet build src/SharpHDL/SharpHDL.csproj -c Release
    dotnet build tests/Tests.csproj -c Release
    dotnet build tools/SharpHDL.Live/SharpHDL.Live.csproj -c Release

test:
    dotnet test tests/SharpHDL.UnitTests/SharpHDL.UnitTests.csproj -c Release

test-hdl:
    dotnet test tests/SharpHDL.IntegrationTests/SharpHDL.IntegrationTests.csproj -c Release

simulate:
    dotnet run --project tests/Tests.csproj -c Release -- --pipelines

benchmark:
    dotnet run --project tests/SharpHDL.Benchmarks/SharpHDL.Benchmarks.csproj -c Release

browser:
    dotnet run --project tools/SharpHDL.Live/SharpHDL.Live.csproj -c Release

vscode:
    @echo "Open vscode/sharphdl in VS Code; see vscode/sharphdl/README.md"

verify:
    python3 tools/dev.py verify

clean:
    dotnet clean src/SharpHDL/SharpHDL.csproj
    dotnet clean tests/Tests.csproj
    dotnet clean tests/SharpHDL.UnitTests/SharpHDL.UnitTests.csproj

# Builds and tests require only Docker after 'just setup'.
container-build:
    docker run --rm -v "{{justfile_directory()}}:/workspace" -w /workspace sharphdl-dev:local dotnet build tests/Tests.csproj -c Release

container-test:
    docker run --rm -v "{{justfile_directory()}}:/workspace" -w /workspace sharphdl-dev:local dotnet test tests/SharpHDL.UnitTests/SharpHDL.UnitTests.csproj -c Release

container-shell:
    docker run --rm -it -v "{{justfile_directory()}}:/workspace" -w /workspace sharphdl-dev:local bash


# Clean container smoke path; does not require .NET installed on host.
container-verify:
    docker run --rm -v "{{justfile_directory()}}:/workspace" -w /workspace sharphdl-dev:local bash tools/container-smoke.sh

# Full TinyRisc16 ROM/RAM/UART end-to-end verification in provisioned toolchain.
tinyrisc-hello:
    docker run --rm -v "{{justfile_directory()}}:/workspace" -w /workspace sharphdl-dev:local bash tools/run-tinyrisc-container.sh

setup-and-hello:
    python3 tools/dev.py setup
    docker run --rm -v "{{justfile_directory()}}:/workspace" -w /workspace sharphdl-dev:local bash tools/run-tinyrisc-container.sh
