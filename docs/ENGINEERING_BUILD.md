# Engineering/API documentation build

The opt-in managed driver builds seven existing engineering pages, compiler XML
from seven framework assemblies, and all tracked StrictDoc specifications. It
validates `requirements-trace.json` against the **real** StrictDoc JSON export.
Unmapped, historical and partial requirements remain open. Test references are
checked for file existence and test-name text, not resolved by a C# compiler.

Normal managed builds do not acquire Python dependencies. To build the reference
on Windows from the checked repository directory:

```powershell
if (!(Test-Path -LiteralPath docs -PathType Container)) { throw 'Run from SharpHDL' }
if (!(Test-Path -LiteralPath build -PathType Container)) {
    New-Item -ItemType Directory -Path build | Out-Null
}
$venv = Join-Path (Get-Location) 'build/docs-venv'
if (!(Test-Path -LiteralPath $venv -PathType Container)) { python -m venv $venv }
$docsPython = Join-Path $venv 'Scripts/python.exe'
& $docsPython -m pip install -r docs/requirements-engineering.txt
if ($LASTEXITCODE) { throw 'Documentation dependencies failed' }
$strictPython = (Get-Command python -ErrorAction Stop).Source
# The existing StrictDoc 0.30.4 installation is separate from Sphinx's private venv.
& $strictPython -c 'import strictdoc; print(strictdoc.__file__)'
$parent = Split-Path (Get-Location).Path -Parent
if (!(Test-Path -LiteralPath $parent -PathType Container)) { throw 'Missing output parent' }
$output = Join-Path $parent ('sharphdl-engineering-' + (Get-Date -Format yyyyMMdd-HHmmss))
dotnet run --project tools/SharpHDL.DocBuilder -c Release -- `
    (Get-Location).Path $output $docsPython $strictPython
if ($LASTEXITCODE) { throw 'Engineering/API build failed; inspect output/runs' }
```

StrictDoc 0.30.4 must already be installed in the selected interpreter (or install
that pinned version in a separate explicit documentation environment). On Linux,
the same driver accepts absolute Python executable paths and a new output directory
whose parent exists, outside the repository. Raw project copies inside a checkout
conflict with BenchmarkDotNet recursive project discovery; the driver rejects that
layout. Builds retain bounded stdout/stderr and process manifests;
Sphinx warnings fail the build. The driver only packages Git-tracked source files,
so stage intended new documentation/source before the final build.

The generated site contains raw repository-relative source references and actual
StrictDoc output alongside rendered engineering/API pages. API pages show only
compiler-emitted documented members; legacy missing XML comments are not complete
API coverage. Missing-comment warning 1591 is suppressed only for this XML export.
Mermaid uses a pinned browser CDN; offline rendered diagrams are not qualified.

Run `dotnet test tests/SharpHDL.DocumentationTests -c Release --collect:"XPlat Code Coverage"`
for parser/path/status regressions. Stryker's checked configuration scopes mutation
to requirement admission and repository-path validation. `bash tools/run-documentation-fuzz.sh 60`
uses SharpFuzz 2.2.0 / AFL++, fixed seed 261026, native Linux output and retained
malformed-input corpus findings. These checks do not establish complete document
semantic correctness or automatically close product requirements.
