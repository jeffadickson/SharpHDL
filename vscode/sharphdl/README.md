# SharpHDL VS Code hardware and verification extension (v0.3)

This is a real VS Code Debug Adapter Protocol *inline adapter* implemented through the VS Code extension API. It connects to the SharpHDL localhost server and supports launch, hardware clock step, threads, scopes and state variables. It does not yet provide source-line breakpoints, DAP continuous execution, token-specific stepping, or source-mapped stack frames. The server currently loads one fixed structural pipeline. It is **not a complete VS Code hardware debugger**.

1. Run `dotnet run --project tools/SharpHDL.Live/SharpHDL.Live.csproj` from the SharpHDL checkout.
2. In VS Code open the `vscode/sharphdl` extension folder. Press F5 in an Extension Development Host (requires a suitable extension-host launch configuration), or package/install the extension using `vsce`.
3. In a workspace `.vscode/launch.json`, add:

```json
{"version":"0.2.0","configurations":[{"type":"sharphdl","request":"launch","name":"SharpHDL hardware (localhost)"}]}
```

4. Launch that configuration. Use Step Over (F10), then inspect `Hardware state` variables.

Source mapping: `PortablePdbSourceMap` can attach file/line/column to the CIL pipeline graph when a matching portable PDB exists. Mapping is not yet connected to the live structural-only example or to DAP stack/source breakpoints. This limitation is intentional and visible.

Security: the API binds to localhost only; do not expose to public networks. The adapter is an experimental developer preview requiring .NET and VS Code build verification.

## Verilator integration

Use an existing HDL language extension such as [mshr-h.veriloghdl](https://marketplace.visualstudio.com/items?itemName=mshr-h.veriloghdl) for syntax highlighting, editor navigation and ordinary on-save linting. SharpHDL deliberately does not reproduce those features. The SharpHDL extension adds the command **SharpHDL: Lint Active Verilog with Verilator** in the VS Code Command Palette. It spawns the installed `verilator` binary directly, sends diagnostics to the VS Code Problems panel, and records stdout/stderr in the **SharpHDL Verilator** output channel.

Settings:

```json
{
  "sharphdl.verilator.path": "verilator",
  "sharphdl.verilator.arguments": ["-Irtl/include"]
}
```

Run `node --test vscode/sharphdl/verilator.test.js` from the repository root for diagnostic-parser tests. The lint command works on **saved** Verilog/SystemVerilog source files; it does not yet resolve a complete multi-file project, compile a generated testbench, attach waveforms to source spans or run Verilator as a simulator through DAP. Multi-file Verilator elaboration and SharpHDL-generated RTL build/test integration are the next extension milestone. Current debug DAP uses the SharpHDL localhost reference simulator, not Verilator.

## Verification Explorer (v0.2)

Open the **SharpHDL repository root** as a VS Code workspace. The **SharpHDL Verification** view appears in the Explorer sidebar. It has two explicit task actions:

- **Provision development tools** invokes the existing `make setup`, which builds the Docker development image and restores tool dependencies.
- **Run TinyRisc verification** invokes `make benchmark-tinyrisc`. This runs the CPU/RTL/UART correctness suite and the interpreter/.NET/Verilator matched benchmark. Its task terminal displays the live output and exit status.

The view lists **existing** verification summaries, logs, generated Verilog and any known VCD file. Files are discovered relative to the opened repository root; no success state is inferred from an old artifact. The task completion refreshes the tree, and the Refresh action can be used after external builds. Clicking an artifact opens it in VS Code. A waveform viewer extension may provide a specialized view for VCD files; SharpHDL does not yet embed one.

The first version intentionally uses the project's Make/Docker toolchain rather than silently downloading compilers on every editor launch. If Docker or `make` is unavailable, use the setup instructions in the root README and inspect the task terminal.

### Next integration boundaries

The Explorer currently recognizes the TinyRisc example only. General project discovery, configurable verification manifests, source↔generated-RTL navigation, waveform cross-probing, and interactive Verilator simulation are still future work. The existing DAP debugger remains backed by the SharpHDL localhost simulation service, not Verilator. This extension has not been manually validated in a graphical VS Code Extension Development Host.

## Generalized Verilator Projects (v0.3)

The repository's `sharphdl.project.json` lists multiple design/testbench configurations. The **SharpHDL Projects** Explorer enumerates them, runs `verilator --binary --timing --trace` on all listed source files, executes the resulting testbench binary in a second VS Code task, and opens any existing HDL and waveform files through registered editors. Tasks use direct process arguments rather than interpolated shell strings, and only validated workspace-relative paths and valid top module identifiers are accepted. VS Code task termination cancels execution; failed builds do not launch a stale executable.

**Go to Source / Generated RTL** uses an optional versioned provenance JSON mapping; it is not automatic until SharpHDL's CIL/Verilog compiler exports that mapping. The `sharphdl.verilator.path` setting selects the host executable; a Docker-only Verilator environment should use the existing Make targets for now. VS Code task problem matchers surface Verilator build errors in Problems. This is a multi-file RTL test runner, **not** a Verilator-backed DAP debugger or embedded waveform engine.

Build/install the VSIX with `make vscode` (Node 22 container is pulled automatically). See [Getting started](../../docs/VSCODE_GETTING_STARTED.md) for Windows/WSL2, Linux, macOS, toolchain, first run and troubleshooting. The full GUI extension-host and cross-platform integration acceptance remains tracked in [issue #25](https://github.com/jeffadickson/SharpHDL/issues/25).
