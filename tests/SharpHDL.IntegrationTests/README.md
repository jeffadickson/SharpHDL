# Real HDL integration tests

Run with a working Docker daemon:

```sh
dotnet test tests/SharpHDL.IntegrationTests/SharpHDL.IntegrationTests.csproj -c Release
```

The initial Testcontainers fixture starts an Ubuntu container, installs actual Icarus Verilog tools, compiles SharpHDL-generated RTL and runs a forty-edge self-checking counter test. Docker failures fail the test rather than being marked as success. Package installation presently requires network access; this bootstrap fixture is not hermetic. A digest-pinned prebuilt image, broader seeded traces, Verilator and Yosys test fixtures and CI Docker gating are tracked in issue #17.

This suite is intentionally separate from the fast xUnit suite. No test execution has been verified during the initial GitHub-only commit.

## Verified local continuation (2026-10-27)

The optional fixture retains Testcontainers 4.8.1 and explicitly pins patched
SSH.NET 2026.0.0 for two transitive SCP advisories. Windows warning-sensitive build
and transitive NuGet audit pass; Linux Docker 29.1.3 actually runs the Icarus
forty-cycle counter test (one passed, zero failed/skipped). This does not qualify
remote Docker or SCP behavior. The Ubuntu tag/apt bootstrap remains nonhermetic.
See docs/WORK_PACKAGES_7_10.md for exact evidence and limitations.
