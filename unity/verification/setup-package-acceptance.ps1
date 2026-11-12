param(
    [Parameter(Mandatory=$true)][string]$ProjectPath,
    [Parameter(Mandatory=$true)][string]$BaselineManifest
)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath($ProjectPath)
$baseline = [IO.Path]::GetFullPath($BaselineManifest)
$manifestPath = Join-Path $project 'Packages/manifest.json'
$markerPath = Join-Path $project 'ProjectSettings/MapEditorAcceptance.json'
if (-not (Test-Path -LiteralPath (Join-Path $project 'ProjectSettings/ProjectVersion.txt'))) { throw 'Prepare a separate empty Unity project first.' }
if (Test-Path -LiteralPath (Join-Path $project 'Temp/UnityLockfile')) { throw 'Setup requires a project which has not been opened in Unity.' }
if (Test-Path -LiteralPath $markerPath) { throw 'Acceptance setup already exists; refusing to overwrite.' }
if ((Get-ChildItem -LiteralPath (Join-Path $project 'Assets') -Force | Measure-Object).Count -ne 0) { throw 'Acceptance setup requires empty Assets; do not use a working development project.' }
foreach ($path in @($project, (Join-Path $project 'Assets'), (Join-Path $project 'ProjectSettings'))) {
    if ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Acceptance directories must not be links.' }
}
$baselineHash = (Get-FileHash -LiteralPath $baseline -Algorithm SHA256).Hash
if ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -ne $baselineHash) { throw 'Baseline must exactly match this prepared project manifest.' }
$manifest = Get-Content -LiteralPath $baseline -Raw | ConvertFrom-Json
if (-not $manifest.dependencies.'io.github.lingrs-llm-researchteam.mapeditor') { throw 'Baseline is missing the package under test.' }
$editor = Join-Path $project 'Assets/MapEditorAcceptance/Editor'
New-Item -ItemType Directory -Path $editor -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'MapEditorPackageAcceptance.cs.txt') -Destination (Join-Path $editor 'MapEditorPackageAcceptance.cs')
[ordered]@{ version=1; baselineSha256=$baselineHash } | ConvertTo-Json | Set-Content -LiteralPath $markerPath -Encoding utf8
Write-Output "Prepared acceptance harness only: $project. Unity was not started."
