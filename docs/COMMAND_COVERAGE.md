# Application command coverage

Inventory from source at c230bb4. A typed boundary is implemented separately
from a shared registry or MCP adapter. Rows marked open require implementation.

| Surface and operations | Current boundary | Shared registry / MCP |
| --- | --- | --- |
| ToolCli run | Validated CLI arguments and HdlToolRunner manifest | Selected first slice |
| DocBuilder build | BuildCampaign typed terminal result | Open |
| EquivalenceCli qualify | BuildCampaign typed terminal result | Open |
| Provenance generation | Console arguments and file generation | Open |
| Live simulation server | Existing HTTP simulation endpoints | Open |
| VS Code buildDesign, runDesign | Shared editor dispatcher, typed outcomes | Cross-process registry / MCP open |
| VS Code selectAndBuildDesign, selectAndRunDesign | Interactive adapters to editor dispatcher | UI adapters |
| VS Code verilatorLint | Task adapter | Typed command / MCP open |
| VS Code verifyTinyRisc, setupTools | Make task adapters | Typed command / MCP open |
| VS Code refreshVerification, refreshProjects | Explorer refresh callbacks | Typed command / MCP open |
| VS Code openVerificationFile | Delegates to vscode.open | Typed command / MCP open |
| VS Code jumpToMapped, showMappedSignals, openWaveforms | Interactive project adapters | Typed command / MCP open |
| VS Code generateProvenance | Make task adapter | Typed command / MCP open |
| VS Code addHardwareBreakpoint, clearHardwareBreakpoints | Extension callbacks | Typed command / MCP open |
| DAP launch, continue, next, stepBack, pause, disconnect | Existing debug protocol handlers | Shared command coverage open |
| Demo, benchmark and fuzz executables | Specialized console entry points | Inventory at individual-operation level open |

The first shared command is `sharphdl.tool.run`. It accepts a typed request
containing executable, distinct arguments, working directory, new evidence
directory and timeout in milliseconds. It returns the existing execution
manifest and a typed boundary outcome with the established CLI exit mapping.
Cancellation propagates to the existing process runner. Execution can change
files through the selected executable and has no general undo operation.
Callers select trusted local executables, as with the existing CLI.

## Reuse decision

Retain the BCL Process backend and HdlToolRunner for bounded logs, cancellation
and retained manifests. Moving boundary validation into SharpHDL.Tooling permits
reuse without adding another process library. CliWrap would require adapting
those existing contracts and is unnecessary for this slice.

Use the official ModelContextProtocol C# SDK for stdio transport, discovery,
JSON schemas and protocol cancellation. Keep the SDK in the MCP adapter so
core tooling and the CLI retain their current dependency boundary. The SDK
uses an MIT license and introduces hosting and JSON dependencies in the adapter.
Its version is pinned in the project file and exercised through a real client.
Writing a JSON-RPC server would duplicate protocol behavior and increase the
maintenance burden. HTTP hosting and remote authentication are outside this
local stdio slice.
[Official SDK](https://github.com/modelcontextprotocol/csharp-sdk)

## Implementation and acceptance

1. Commit this inventory and maintain the partial RAIL-MCP-001 requirement.
2. Add typed tool request/result and a registry in SharpHDL.Tooling. Route the
   existing CLI through it while preserving its syntax, failure exits and logs.
   Exercise malformed input, missing tools, subprocess failure and cancellation.
3. Add the stdio SDK adapter. Generate discovery from the registry entry and
   call the same handler. Exercise discovery, schema, malformed input, tool
   failure and cancellation over the real stdio protocol. Retain measured
   coverage and scope limitations with local commits.

The Dafny exit-code experiment remains applicable to the campaign boundary.
This slice reuses the runner's existing semantics and tests its integration.
