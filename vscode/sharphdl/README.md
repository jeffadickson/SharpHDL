# SharpHDL VS Code adapter — prototype

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
