param(
    [Parameter(Mandatory)][string] $Dafny,
    [Parameter(Mandatory)][string] $EvidenceRoot
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
if (!(Test-Path -LiteralPath $EvidenceRoot -PathType Container)) { throw 'Missing evidence root.' }
$run = Join-Path (Resolve-Path -LiteralPath $EvidenceRoot).Path ("campaign-exit-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
Write-Host "Proof evidence at $run"
$proof = Join-Path $PSScriptRoot 'CampaignExit.dfy'
$generated = Join-Path $run 'CampaignExit.cs'
& $Dafny --version
& $Dafny verify $proof
& $Dafny translate cs --include-runtime --output $generated $proof
& dotnet run --project (Join-Path $PSScriptRoot 'CampaignExitCheck.csproj') --configuration Release --artifacts-path (Join-Path $run 'managed') "-p:GeneratedCampaignExit=$generated" -- $run
