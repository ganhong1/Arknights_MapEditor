param(
    [Parameter(Mandatory=$true)][string]$EditorPath,
    [Parameter(Mandatory=$true)][string]$ProjectPath,
    [Parameter(Mandatory=$true)][string]$BaselineManifest,
    [Parameter(Mandatory=$true)][ValidateSet('installed','uninstall','reinstall')][string]$Phase,
    [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath($ProjectPath)
$baseline = [IO.Path]::GetFullPath($BaselineManifest)
$output = [IO.Path]::GetFullPath($OutputPath)
$manifestPath = Join-Path $project 'Packages/manifest.json'
$marker = Get-Content -LiteralPath (Join-Path $project 'ProjectSettings/MapEditorAcceptance.json') -Raw | ConvertFrom-Json
if ($marker.version -ne 1 -or $marker.baselineSha256 -ne (Get-FileHash -LiteralPath $baseline -Algorithm SHA256).Hash) { throw 'Acceptance baseline identity mismatch.' }
if (-not (Test-Path -LiteralPath (Join-Path $project 'Assets/MapEditorAcceptance/Editor/MapEditorPackageAcceptance.cs'))) { throw 'Acceptance harness is missing.' }
if (-not (Test-Path -LiteralPath $EditorPath -PathType Leaf)) { throw 'Editor executable is missing.' }
if (Test-Path -LiteralPath $output) { throw 'Use a new output directory.' }
$lock = Join-Path $project 'Temp/UnityLockfile'
if (Test-Path -LiteralPath $lock) {
    try { $handle = [IO.File]::Open($lock, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None); $handle.Dispose() }
    catch { throw 'Acceptance project is open; close its owner before changing its manifest.' }
}
$manifest = Get-Content -LiteralPath $baseline -Raw | ConvertFrom-Json
$package = 'io.github.lingrs-llm-researchteam.mapeditor'
if (-not $manifest.dependencies.$package) { throw 'Baseline is missing the package under test.' }
if ($Phase -eq 'installed' -and (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -ne $marker.baselineSha256) { throw 'Installed manifest differs from the frozen acceptance baseline.' }
$expected = 'installed'
if ($Phase -eq 'uninstall') {
    $manifest.dependencies.PSObject.Properties.Remove($package)
    if ($manifest.PSObject.Properties.Name -contains 'testables') { $manifest.testables = @($manifest.testables | Where-Object { $_ -ne $package }) }
    $expected = 'absent'
}
if ($Phase -ne 'installed') {
    $manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $manifestPath -Encoding utf8
}
New-Item -ItemType Directory -Path $output | Out-Null
$previousTemp = $env:TEMP; $previousTmp = $env:TMP; $previousCache = $env:UPM_CACHE_ROOT
try {
    $env:TEMP = Join-Path $output 'Temp'; $env:TMP = $env:TEMP; $env:UPM_CACHE_ROOT = Join-Path $output 'UPM'
    New-Item -ItemType Directory -Path $env:TEMP,$env:UPM_CACHE_ROOT | Out-Null
    $arguments = @('-batchmode','-quit','-projectPath',('"'+$project+'"'),'-buildTarget','Win64',
        '-executeMethod','MapEditorPackageAcceptance.VerifyAndBuild','-mapEditorExpected',$expected,
        '-mapEditorAcceptanceOutput',('"'+$output+'"'),'-logFile',('"'+(Join-Path $output 'editor.log')+'"'))
    $process = Start-Process -FilePath $EditorPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    Write-Output "Acceptance editor PID: $($process.Id)"
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "Acceptance failed: exit $($process.ExitCode). See editor.log." }
    $result = Get-Content -LiteralPath (Join-Path $output 'acceptance.json') -Raw | ConvertFrom-Json
    if ($result.expected -ne $expected -or $result.buildResult -ne 'Succeeded') { throw 'Acceptance report mismatch.' }
    $result
} finally { $env:TEMP = $previousTemp; $env:TMP = $previousTmp; $env:UPM_CACHE_ROOT = $previousCache }
