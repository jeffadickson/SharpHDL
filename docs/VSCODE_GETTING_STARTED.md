# Getting started with SharpHDL in Visual Studio Code

This guide installs the SharpHDL extension, builds the existing TinyRisc16 example, runs Verilator, and explains how the reference hardware debugger differs from Verilator.

## 1. Install prerequisites

Install [Visual Studio Code](https://code.visualstudio.com/), [Docker Desktop](https://www.docker.com/products/docker-desktop/) or a working Docker Engine, Git, and Make. Docker must be running. Python 3 is needed for the existing `make setup` bootstrap. Windows users can use **WSL2 with Docker Desktop integration**; run Make inside the WSL2 shell and open the repository using the VS Code WSL extension. On macOS and Linux, use a normal terminal. VS Code's C# Dev Kit (or C# extension) is recommended for editing C#; an established Verilog/SystemVerilog extension, such as `mshr-h.veriloghdl`, is recommended for RTL syntax and waveform viewing. The SharpHDL extension does **not** replace either language service.

Clone and enter the repository:

```sh
git clone https://github.com/jeffadickson/SharpHDL.git
cd SharpHDL
make doctor
make setup
```

`make setup` builds the project's .NET 8, Icarus, Yosys and Verilator development image. It does not install tools globally on your machine.

## 2. Build and install the VS Code extension

From the repository root:

```sh
make vscode
```

This target first executes the Node.js parser/manifest tests, then downloads the official Node 22 Docker image if necessary, runs the pinned VS Code extension packaging tool, and writes `dist/sharphdl-0.3.0.vsix`. Host Node/npm installation is not required.

Install the resulting extension using **VS Code → Extensions → ... → Install from VSIX**, selecting that file. Alternatively, when the `code` CLI is available:

```sh
code --install-extension dist/sharphdl-0.3.0.vsix
code .
```

Reload the VS Code window after installation. To develop the extension itself, open `vscode/sharphdl` as an extension-development workspace and use an Extension Development Host; the packaged VSIX is the recommended first-run route.

## 3. Generate and verify the TinyRisc16 example

Run the regression once to produce the generated HDL:

```sh
make tinyrisc-hello
```

The repository root contains `sharphdl.project.json`, a version-1 design manifest listing the TinyRisc UART testbench, generated Verilog, top module and output directory. In the VS Code Explorer sidebar, expand **SharpHDL Projects → TinyRisc16 — Hello World UART**. Choose **Build Verilator design** or **Run Verilator test**. These commands use `verilator --binary --timing --trace` and the sources from the manifest, run in VS Code's task terminal, and report process failures. Terminate the task in VS Code to cancel the run. A Verilator build error is displayed in Problems when it matches the Verilator task problem matcher.

For the full CPU/reference/JIT/Icarus/Verilator regression and benchmarks, use the **SharpHDL Verification** Explorer view and select **Run TinyRisc verification**. That command runs the existing `make benchmark-tinyrisc` target, then exposes generated reports and logs.

## 4. Inspect waveforms and generated RTL

Click a generated `.v`, `.sv`, `.vcd`, or `.fst` file in the Explorer. VS Code dispatches the file to its registered editor, including an installed waveform viewer if available. A waveform is shown only when the testbench actually emits one. The extension does not invent traces or claim a built-in waveform renderer.

To add your own design, extend `sharphdl.project.json`:

```json
{
  "version": 1,
  "projects": [
    {
      "id": "myCounter",
      "label": "My Counter",
      "top": "tb_counter",
      "sources": ["generated/Counter.v", "tests/counter_tb.sv"],
      "outputDir": "build/verilator_counter",
      "waveforms": ["build/verilator_counter/counter.vcd"],
      "sourceMap": "generated/Counter.source-map.json"
    }
  ]
}
```

The source-map field is optional. If supplied, the map uses `{"version":1,"mappings":[{"source":{"file":"src/Counter.cs","line":12},"rtl":{"file":"generated/Counter.v","line":24},"signals":["count"]}]}`. From an open source or RTL editor, invoke **SharpHDL: Go to Source / Generated RTL**. Navigation requires actual provenance data from the compiler; the current compiler does not yet automatically emit this manifest format.

All file paths must be relative to the workspace root. The manifest describes files and top modules, **not executable commands**, to avoid arbitrary shell execution.

## 5. Hardware clock debugger (separate from Verilator)

For the experimental SharpHDL localhost clock debugger, run:

```sh
dotnet run --project tools/SharpHDL.Live/SharpHDL.Live.csproj
```

This command requires a native .NET 8 SDK; alternatively, run it in the provisioned container with appropriate localhost port forwarding. In VS Code, create `.vscode/launch.json` with:

```json
{
  "version": "0.2.0",
  "configurations": [
    {"type":"sharphdl","request":"launch","name":"SharpHDL hardware (localhost)"}
  ]
}
```

Start debugging, use F10 to step hardware clocks, and inspect state in the Variables pane. Step Back uses the localhost reference-simulator rewind API. **This debugger does not attach to the Verilator executable**. Source breakpoints, generalized conditional hardware breakpoints, and synchronized Verilator waveforms are not yet implemented.

## Troubleshooting

If `make vscode` fails, check Docker is running and that Docker can download the Node 22 image. If `verilator` is not on the host PATH, set `sharphdl.verilator.path` to its executable; for Docker-only toolchains, use `make benchmark-tinyrisc` until a container-backed VS Code task runner is available. If a project source is missing, run the appropriate SharpHDL generation target first. If no waveforms appear, verify that the testbench uses `$dumpfile` and `$dumpvars`. On Windows, WSL2 is the tested-oriented development path; native Windows Verilator compilation is not claimed supported without a compatible C++ toolchain.

## Verification status

Node parser and extension command checks run in `.github/workflows/vscode-sharphdl.yml`. The new multi-project runner and VSIX packaging also require real VS Code host testing before issue #25 can be closed. See [the extension README](../vscode/sharphdl/README.md) and [issue #25](https://github.com/jeffadickson/SharpHDL/issues/25).
