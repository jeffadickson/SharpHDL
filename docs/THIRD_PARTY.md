# Third-party components added by the work-package continuation

This records the newly adopted runtime component; it is not a complete historical
SBOM of SharpHDL's test, development and external HDL toolchain dependencies.
SharpHDL's own source retains the repository MIT license. Third-party components
retain their respective licenses.

| Component | Version / upstream | License / use |
|---|---|---|
| QuikGraph | NuGet 2.5.0; upstream commit `9cd6b49292e09041258708a37bed99c56177b0ef` | [MS-PL](https://licenses.nuget.org/MS-PL); unchanged managed graph library referenced by SharpHDL.HLS for BFS/SCC. |
| SingleStepTests/z80 | Pinned revision recorded in `examples/Z80/*provenance.json` | MIT; sampled test data with the existing `examples/Z80/SingleStepTests.LICENSE`, not production CPU code. |

QuikGraph author metadata: Alexandre Rabérin; package copyright: Copyright © 2019.
Upstream: <https://github.com/KeRNeLith/QuikGraph>. The restored package selects
`lib/netstandard2.0/QuikGraph.dll` under .NET 10 and declares no dependencies for
that target. Its NuGet package SHA-512 from the restore manifest is:

```text
sG+mrPpXwxlXknRK5VqWUGiOmDACa9X+3ftlkQIMgOZUqxVOQSe0+HIU9PTjwqazy0pqSf8MPDXYFGl0GYWcKw==
```

There is no vendored/relicensed QuikGraph source. Consult upstream license and
package metadata when redistributing binaries. See `INSTRUCTION_CONTROL_FLOW.md`
for the technical fit, alternatives, limits and older release-cadence tradeoff.
