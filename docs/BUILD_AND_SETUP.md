# SharpHDL developer build workflow

## Make-first automatic setup and TinyRisc verification (2026-10-14)

GNU Make, Python 3, and an installed/running Docker Engine/Desktop are host prerequisites. The Makefile does **not** require `just` for toolchain provisioning. Use WSL2 with Docker Desktop integration on Windows.

```sh
make setup
make tinyrisc-hello
# Or in one command:
make setup-and-hello
```

`make setup` invokes `python3 tools/dev.py setup`: it builds the development image from `tools/Dockerfile.dev`, downloading the .NET 8 SDK base image and installing Icarus Verilog, Verilator, Yosys, nextpnr-ice40, IceStorm, Python, and related utilities inside Docker. It restores all six managed projects and checks HDL executables. No host administrator privileges are silently requested by the Makefile. It will fail explicitly if Docker is absent/unavailable.

`make tinyrisc-hello` uses the provisioned image to execute `tests/run-tinyrisc.sh` and a Yosys top-level structural check via `tools/run-tinyrisc-container.sh`. This includes ROM assembly, independent CPU model and RTL edge comparison, the Hello World UART byte stream, and a separate serial 8N1 decoder testbench. Exit code is nonzero on any failed command. A committed command/test is **not proof of a passing execution**; inspect the invocation logs.

`make container-verify` remains the broader smoke verification tier. The original `just setup` and `just container-verify` commands also remain supported. Package repositories are not fully locked; use versioned CI records for reproducibility evidence.

---


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
