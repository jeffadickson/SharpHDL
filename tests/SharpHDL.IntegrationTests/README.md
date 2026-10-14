# Real HDL integration tests

Run with a working Docker daemon:

```sh
dotnet test tests/SharpHDL.IntegrationTests/SharpHDL.IntegrationTests.csproj -c Release
```

The initial Testcontainers fixture starts an Ubuntu container, installs actual Icarus Verilog tools, compiles SharpHDL-generated RTL and runs a forty-edge self-checking counter test. Docker failures fail the test rather than being marked as success. Package installation presently requires network access; this bootstrap fixture is not hermetic. A digest-pinned prebuilt image, broader seeded traces, Verilator and Yosys test fixtures and CI Docker gating are tracked in issue #17.

This suite is intentionally separate from the fast xUnit suite. No test execution has been verified during the initial GitHub-only commit.
