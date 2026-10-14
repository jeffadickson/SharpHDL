# SharpHDL developer build workflow

## Tool ownership

- **.NET CLI/MSBuild** builds .NET projects; `global.json` pins the selected major SDK feature band.
- **just** provides developer command names in the root `justfile`. The root `Makefile` delegates compatible targets to just, so Make is optional.
- **Testcontainers** runs real HDL toolchain integration tests inside Docker, separately from fast xUnit tests.
- **GitHub Actions** runs the published CI workflow. Shell/Python scripts remain specialized HDL/board verification entry points.

## Quick start

Install Python 3, just, and a compatible .NET 8 SDK. Optional for hardware verification: Docker. From the repo root:

```sh
just doctor
just setup
just build
just test
just browser
```

On systems without just, `python3 tools/dev.py doctor` and `python3 tools/dev.py setup` work directly, and `dotnet build` / `dotnet test` remain available. The Makefile does **not** install just.

`just setup` restores NuGet dependencies for all declared .NET projects; it **does not** silently install system packages, start Docker, install an FPGA vendor IDE, install Icarus/Yosys/nextpnr, or change PATH. A missing prerequisite produces an actionable diagnostic. Use explicit OS package managers or a developer container if native tools are needed.

`just test` runs fast xUnit tests. `just test-hdl` runs the Docker-required Icarus integration project. `just verify` runs xUnit and the legacy main test program plus the Testcontainers test suite, and exits nonzero if Docker is missing. This is a developer verification tier rather than the complete board/document/browser regression represented by `tests/run-all.sh`; the latter has additional dependencies and stricter release criteria.

## Developer tasks

```sh
just --list
just simulate
just benchmark
just vscode
just clean
```

The VS Code extension is still a prototype; `just vscode` prints the setup instructions rather than claiming an extension has been installed.

## Reproducibility and version caveats

The root SDK policy is 8.0.100 with `latestPatch`, allowing a newer patch within the same 8.0 SDK feature band. Realized SDK version should be recorded by CI. NuGet versions are in existing project files, and `Testcontainers` currently bootstraps Icarus from Ubuntu at runtime; that integration image/package setup is not fully reproducible until digest-pinned. Full vendor FPGA synthesis and timing are not included in ordinary developer verification. A future `mise` manifest/devcontainer may pin language runtimes and HDL tools more comprehensively.

Do not call these scripts passing until they have been executed with recorded output.
