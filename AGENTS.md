# SharpHDL repository instructions

GitHub Actions was explicitly disabled by the user on 2026-10-27. Keep repository
Actions disabled. Do not enable, dispatch or introduce automatically running CI
unless the user explicitly changes that instruction. Retained workflow definitions
are dormant documentation; verify changes locally. Pushes are permitted only when
the active task authorizes them.

At build, CLI and editor command boundaries, return typed outcomes for expected
failures and cancellation. Validate before creating outputs, stop dependent
stages on failure, preserve diagnostics, and publish completion only after
success. Keep input prompts in interactive adapters. Use explicit control flow
for recovery and cleanup. See `docs/RAILWAY_BOUNDARIES.md` for the command
contracts, local checks and opt-in Dafny proof. When brainstorming, consider a
bounded Dafny proof for a precise invariant that can be checked at its .NET
boundary.
