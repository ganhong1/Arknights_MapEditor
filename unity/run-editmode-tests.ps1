param(
    [Parameter(Mandatory=$true)][string]$EditorPath,
    [Parameter(Mandatory=$true)][string]$ProjectPath,
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [switch]$NoGraphics,
    [switch]$Coverage
)
$ErrorActionPreference = 'Stop'
$outputRoot = [IO.Path]::GetFullPath($OutputPath)
$projectRoot = [IO.Path]::GetFullPath($ProjectPath)
$lockPath = Join-Path $projectRoot 'Temp/UnityLockfile'
if (Test-Path -LiteralPath $lockPath) {
    try { $lockCheck = [IO.File]::Open($lockPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None); $lockCheck.Dispose() }
    catch { throw 'Project lock is held or inaccessible; do not start a second owner.' }
}
New-Item -ItemType Directory -Force $outputRoot | Out-Null
$env:TEMP = Join-Path $outputRoot 'Temp'
$env:TMP = $env:TEMP
$env:UPM_CACHE_ROOT = Join-Path $outputRoot 'UPM'
New-Item -ItemType Directory -Force $env:TEMP,$env:UPM_CACHE_ROOT | Out-Null
$results = Join-Path $outputRoot 'results.xml'
if (Test-Path -LiteralPath $results) { throw 'Use a new output directory so old results cannot be mistaken for this run.' }
$arguments = @('-batchmode','-projectPath',('"' + $projectRoot + '"'),'-buildTarget','Win64',
    '-runTests','-testPlatform','EditMode','-testResults',('"' + $results + '"'),'-logFile',('"' + (Join-Path $outputRoot 'editor.log') + '"'))
if ($NoGraphics) { $arguments += '-nographics' }
if ($Coverage) {
    $coverageRoot = Join-Path $outputRoot 'Coverage'
    $arguments += @('-enableCodeCoverage','-debugCodeOptimization','-coverageResultsPath',('"' + $coverageRoot + '"'),
        '-coverageOptions','"generateHtmlReport;generateAdditionalReports;assemblyFilters:+MapEditor.Core,+MapEditor.Storage,+MapEditor.Editor,-MapEditor.Editor.Tests"')
}
$process = Start-Process -FilePath $EditorPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output "Isolated test editor PID: $($process.Id)"
while (-not $process.WaitForExit(1000)) { }
if ($process.ExitCode -ne 0) { throw "Test editor failed with exit code $($process.ExitCode); see editor.log" }
if (-not (Test-Path -LiteralPath $results)) { throw 'Editor exited without test results.' }
[xml]$report = Get-Content -LiteralPath $results
$run = $report.'test-run'
$passed = [int]$run.passed
$skipped = [int]$run.skipped
if ($run.result -notin @('Passed', 'Skipped:Ignored') -or $passed -lt 1 -or
    [int]$run.failed -ne 0 -or [int]$run.inconclusive -ne 0 -or [int]$run.total -ne ($passed + $skipped)) {
    throw 'Tests failed, were incomplete, or did not run.'
}
$run | Select-Object result,total,passed,failed,skipped
