# Local tool execution through MCP

The stdio server exposes three shared typed command registrations.
`sharphdl_tool_run` invokes `sharphdl.tool.run`, also used by ToolCli.
It executes the caller's trusted local program, can modify files, and has no
general undo operation. Run it with the same permissions as the local CLI.

Build from the SharpHDL root using an existing artifact directory.

```powershell
if (!(Test-Path -LiteralPath tools/SharpHDL.Mcp -PathType Container)) { throw 'Missing MCP project' }
if (!(Test-Path -LiteralPath $artifacts -PathType Container)) { throw 'Missing artifact root' }
dotnet build tools/SharpHDL.Mcp -c Release --artifacts-path $artifacts
if ($LASTEXITCODE) { throw 'MCP build failed' }
```

Configure an MCP client to launch `dotnet` with arguments
`["<artifacts>/bin/SharpHDL.Mcp/release/SharpHDL.Mcp.dll"]`.
Use the absolute path on the host. Logs go to stderr and stdout carries the
MCP protocol. The SDK handles initialization, discovery and cancellation.

`tools/list` advertises the tool runner with the following request shape.

```json
{
  "name": "sharphdl_tool_run",
  "arguments": {
    "request": {
      "executable": "yosys",
      "arguments": ["-V"],
      "workingDirectory": "/existing/workspace",
      "evidenceDirectory": "/existing/artifacts/new-run",
      "timeoutMilliseconds": 10000
    }
  }
}
```

The working directory and evidence parent must exist. The evidence directory
must be new. Executable names resolve through PATH and explicit absolute
executables retain the runner's missing-executable outcome. Relative paths
resolve against the server process working directory, as they do in ToolCli.

Results contain operation, typed status, exitCode, diagnostic and available
evidence and manifest. Failed operations set MCP `isError`. Schema errors
are handled by the SDK. A client can cancel the protocol request. Once the
runner finishes cancellation cleanup, its retained manifest records Cancelled.
The client can already have observed cancellation before the file is published.
Ordinary process trees are covered by the runner. Detached processes remain
outside its cleanup contract.

ToolCli retains its positional syntax and supports registry discovery with
`list`. This example uses the same handler.

```powershell
if (!(Test-Path -LiteralPath tools/SharpHDL.ToolCli -PathType Container)) { throw 'Missing tool CLI' }
dotnet run --project tools/SharpHDL.ToolCli -- list
```

The adapter pins ModelContextProtocol 2.4.0 and Microsoft.Extensions.Hosting
10.0.14. The SDK package declares Apache-2.0. Keeping it in this project
preserves the dependency boundary of the existing runner. HTTP hosting,
authentication and the remaining application command adapters are open work.

Two additional tools invoke the same typed registrations as the campaign CLIs.

| MCP tool | Request fields |
| --- | --- |
| `sharphdl_documentation_build` | `repository`, `outputDirectory`, `sphinxPython`, `strictDocPython` |
| `sharphdl_equivalence_qualify` | `evidenceDirectory`, optional `solverOnly` |

Wrap these fields in the `request` argument, as for the runner. Campaign
results contain operation, status, stage, diagnostic and evidence locations.
Failures set `isError`. The campaign CLIs retain their positional arguments,
Ctrl+C handling and terminal JSON/exit contract. Case progress is sent through
Trace with a console listener installed only by the qualification CLI.
The qualification result describes the existing bounded case suite. It does
not accept arbitrary designs. Z3-only execution requires `solverOnly: true`.
Independent RTL checks additionally require Yosys.

The MCP deployment references the existing documentation and qualification
assemblies and therefore carries their dependencies, including Z3. It does
not start their console entry points. Native solver setup and the explicitly
selected Python environments follow the campaign CLI instructions.

Run `dotnet test tests/SharpHDL.CommandTests -c Release` for actual SDK
client/server integration, with the repository's memory watchdog and artifact
policies applied. The tests launch real processes and retain their evidence.
[Coverage inventory and reuse decision](../../docs/COMMAND_COVERAGE.md)
