# Exercise a real failed process task in an isolated VS Code workspace.
param([Parameter(Mandatory)][string] $EvidenceRoot)
$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath $EvidenceRoot -PathType Container)) { throw 'Missing evidence root.' }
$run = Join-Path (Resolve-Path -LiteralPath $EvidenceRoot).Path ("editor-host-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$workspace = Join-Path $run 'workspace'
New-Item -ItemType Directory -Path $workspace | Out-Null
$settings = Join-Path $workspace '.vscode'
New-Item -ItemType Directory -Path $settings | Out-Null
'{"version":1,"projects":[{"id":"RailwayFixture","top":"fixture","sources":["fixture.v"],"outputDir":"out"}]}' |
    Set-Content -LiteralPath (Join-Path $workspace 'sharphdl.project.json')
'module fixture; endmodule' | Set-Content -LiteralPath (Join-Path $workspace 'fixture.v')
@{ 'sharphdl.verilator.runner' = 'host'; 'sharphdl.verilator.path' = (Get-Command dotnet).Source } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $settings 'settings.json')

$extension = Join-Path (Split-Path -Parent $PSScriptRoot) 'vscode/sharphdl'
if (!(Test-Path -LiteralPath $extension -PathType Container)) { throw 'Missing extension source.' }
$code = Join-Path (Split-Path -Parent (Split-Path -Parent (Get-Command code).Source)) 'Code.exe'
$launch = @(
    '--disable-extensions', '--skip-welcome', '--disable-workspace-trust'
    "--user-data-dir=`"$(Join-Path $run 'user-data')`""
    "--extensions-dir=`"$(Join-Path $run 'extensions')`""
    "--extensionDevelopmentPath=`"$extension`""
    "--extensionTestsPath=`"$(Join-Path $extension 'test/designCommandsHost.test.js')`""
    "`"$workspace`""
)
Write-Host "Editor host evidence at $run"
$hostProcess = Start-Process -FilePath $code -ArgumentList $launch -Wait -PassThru -WindowStyle Hidden
if ($hostProcess.ExitCode -ne 0) { throw "VS Code test host exited with $($hostProcess.ExitCode)." }
