# SharpHDL developer build workflow

## Automatic toolchain provisioning (current workflow)

**Yes: `just setup` now builds a development container that installs the .NET 8 SDK, Python, Node.js, Icarus Verilog, Verilator, Yosys, nextpnr-ice40 and IceStorm.** It also restores the core and xUnit .NET dependencies inside the container. You do not need to install those compilers/simulators separately on the host.

Docker Engine or Docker Desktop must already be installed, started, and accessible to your user. It is the one bootstrap prerequisite for this container-first path, in addition to Python 3 and `just` for the command interface. The setup returns a failure when Docker is unavailable; it does not silently modify the host OS or request administrator privileges.

```sh
just setup
just container-build
just container-test
just container-shell
```

The provisioning recipe is [tools/Dockerfile.dev](../tools/Dockerfile.dev). Image tag `sharphdl-dev:local` is locally built, not a published trusted image. Package versions still follow the selected Debian/Ubuntu package repositories; **this is automated provisioning but not yet a digest-locked/hermetic toolchain**. Portable browser testing, proprietary FPGA toolchains and VS Code packaging are not all included. A second, optional native installer could be implemented for developers who want HDL tools on the host.


## Tool ownership

- **.NET CLI/MSBuild** builds .NET projects; `global.json` pins the selected major SDK feature band.
- **just** provides developer command names in the root `justfile`. The root `Makefile` delegates compatible targets to just, so Make is optional.
- **Testcontainers** runs real HDL toolchain integration tests inside Docker, separately from fast xUnit tests.
- **GitHub Actions** runs the published CI workflow. Shell/Python scripts remain specialized HDL/board verification entry points.

## Quick start

For automatic provisioning install Python 3, just, and Docker. The .NET 8 SDK and open-source HDL tools are installed in the container; native SDK installations are optional. From the repo root:

```sh
just doctor
just setup
just container-build
just container-test
just browser
```

On systems without just, `python3 tools/dev.py doctor` and `python3 tools/dev.py setup` work directly, and `dotnet build` / `dotnet test` remain available. The Makefile does **not** install just.

`just setup` restores NuGet dependencies for all declared .NET projects; it automatically installs open-source HDL tools **inside the development container**, never silently onto the host. It does not install/start Docker itself or install proprietary FPGA vendor IDEs. A missing prerequisite produces an actionable diagnostic. Use explicit OS package managers or a developer container if native tools are needed.

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

The root SDK policy is 8.0.100 with `latestFeature`, allowing newer .NET 8 SDK feature bands. Realized SDK version should be recorded by CI. NuGet versions are in existing project files, and `Testcontainers` currently bootstraps Icarus from Ubuntu at runtime; that integration image/package setup is not fully reproducible until digest-pinned. Full vendor FPGA synthesis and timing are not included in ordinary developer verification. A future `mise` manifest/devcontainer may pin language runtimes and HDL tools more comprehensively.

Do not call these scripts passing until they have been executed with recorded output.

## Clean environment verification

Run `just container-verify` after `just setup`. The container smoke test checks executable availability, builds the existing test driver and live debugging host, runs the xUnit suite and legacy reference regression, parses generated Verilog using real Icarus and processes it with Yosys. The provisioning CI workflow performs a clean `docker build` followed by this test, storing test and generated RTL artifacts.

`just setup` restores **all six** .NET projects and verifies key HDL commands inside the container. This is automated open-source tool provisioning, not a claim of fully hermetic/reproducible versions: the base image is tag-based and Debian packages are not snapshot-pinned. Docker is still a bootstrap prerequisite. Proprietary FPGA toolchains and browser Playwright dependencies are outside this initial smoke gate. Testcontainers with nested Docker is not exercised inside the development container; the separate integration CI job runs on a Docker-capable host.
