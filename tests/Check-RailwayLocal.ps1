# Run the local railway checks from Windows with Linux CLI artifacts in WSL.
param([Parameter(Mandatory)][string] $LinuxArtifacts)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$repo = Split-Path -Parent $PSScriptRoot
if (!(Test-Path -LiteralPath $repo -PathType Container)) { throw 'Missing SharpHDL checkout.' }
Push-Location -LiteralPath $repo
try {
    & node --experimental-test-coverage --test 'vscode/sharphdl/*.test.js'
    & wsl -- bash tests/check-railway-shell.sh
    & wsl -- bash tests/check-railway-campaigns.sh $LinuxArtifacts
} finally {
    Pop-Location
}
