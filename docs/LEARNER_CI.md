# Automated learner browser checks

The `Learner browser checks` GitHub Actions workflow runs the actual Clock Lab
controls in Chromium on a GitHub-hosted Ubuntu runner. It runs on relevant pushes
to main, pull requests, or a manual workflow dispatch. No board or classroom
hardware is needed.

## What a green run establishes

- The actual page opens and executes without browser JavaScript errors.
- Reset preserves all eight possible input checkbox combinations and stops
  automatic playback. Explicit teacher startup works from every combination.
- Changing checkboxes alone does not clock registers. Enabled and disabled edges
  count and hold correctly; completion follows Done/4, Ready/4, Ready/0.
- The combinational input exercise updates immediately; displayed fixed-point
  examples give the documented values.
- The existing graph references, prepared waveform fixtures and generated-document
  checks pass on the committed files.

The workflow installs Playwright 1.62.1 and its matching Chromium build, uses
read-only repository permissions, and uploads logs for 14 days. Its ten-minute
limit bounds the run. A missing browser or failed assertion fails the job; it
cannot pass by silently substituting graph-only checks.

## What it does not establish

This is not the complete compiler/synthesis regression. The waveform fixture
check reads saved results; this workflow does not perform fresh VeriSim WASM
runs. A green job is not evidence that a physical FPGA, school network, learner
device, or class has been tested. Use the teacher station checklist before a
learner session.

Check the [workflow runs](https://github.com/jeffadickson/SharpHDL/actions/workflows/learner-browser.yml)
for the exact commit being used. Presence of the workflow file is not a passing
run. The five-round review's local browser failure remains a historical result;
a successful CI run provides separate evidence for its own commit.

## Run it locally

Install Node.js, Playwright 1.62.1 and its Chromium browser in your development
environment, then run from the repository root:

```sh
node tests/check-learner-controls.js --browser
```

`CHROME_BIN` may select an existing Chromium executable. Without `--browser`,
only parsing and graph assertions run and the browser stage explicitly reports
SKIP. The full `tests/run-all.sh` also requires the browser stage.

CI installation follows the [Playwright CI guide](https://playwright.dev/docs/ci).

| Requirement | Evidence |
|---|---|
| EDU-CI-01: Run actual learner controls on relevant changes and fail on errors. | Workflow calls the mandatory `--browser` path; inspect its run result. |
| EDU-CI-02: Preserve logs and associate results with the tested commit. | GitHub run metadata and uploaded logs. |
| EDU-CI-03: Keep browser, fixture, hardware and learner acceptance distinct. | Scope above and separate station/pilot records. |
