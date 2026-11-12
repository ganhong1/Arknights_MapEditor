param(
    [Parameter(Mandatory=$true)][string]$ProjectPath,
    [Parameter(Mandatory=$true)][string]$SharedPackages,
    [switch]$Coverage,
    [string]$PackageArchive
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath($ProjectPath)
if (Test-Path -LiteralPath (Join-Path $projectRoot 'Packages/manifest.json')) {
    throw 'Development project already exists; refusing to overwrite its manifest.'
}
New-Item -ItemType Directory -Force (Join-Path $projectRoot 'Assets'), (Join-Path $projectRoot 'Packages'), (Join-Path $projectRoot 'ProjectSettings') | Out-Null
$packageRoot = Join-Path $PSScriptRoot 'Packages/io.github.lingrs-llm-researchteam.mapeditor'
function LocalPackage([string]$path) { return 'file:' + ([IO.Path]::GetFullPath($path) -replace '\\','/') }
$dependencies = [ordered]@{
    'io.github.lingrs-llm-researchteam.mapeditor' = (LocalPackage $packageRoot)
    'com.unity.nuget.newtonsoft-json' = (LocalPackage (Join-Path $SharedPackages 'com.unity.nuget.newtonsoft-json/3.2.2'))
    'com.unity.test-framework' = (LocalPackage (Join-Path $SharedPackages 'com.unity.test-framework/1.1.33'))
    'com.unity.ext.nunit' = (LocalPackage (Join-Path $SharedPackages 'com.unity.ext.nunit/1.0.6'))
    'com.unity.modules.uielements' = '1.0.0'
    'com.unity.modules.imgui' = '1.0.0'
    'com.unity.modules.jsonserialize' = '1.0.0'
}
if ($PackageArchive) {
    if (-not (Test-Path -LiteralPath $PackageArchive -PathType Leaf) -or -not $PackageArchive.EndsWith('.tgz')) {
        throw 'PackageArchive must be an existing UPM .tgz.'
    }
    $dependencies['io.github.lingrs-llm-researchteam.mapeditor'] = LocalPackage $PackageArchive
}
if ($Coverage) {
    $dependencies['com.unity.testtools.codecoverage'] = LocalPackage (Join-Path $SharedPackages 'com.unity.testtools.codecoverage/1.2.5')
    $dependencies['com.unity.settings-manager'] = LocalPackage (Join-Path $SharedPackages 'com.unity.settings-manager/1.0.1')
}
foreach ($value in $dependencies.Values) {
    if ($value.StartsWith('file:')) {
        $localPath = $value.Substring(5)
        if ($localPath.EndsWith('.tgz')) {
            if (-not (Test-Path -LiteralPath $localPath -PathType Leaf)) { throw "Missing package archive: $value" }
        } elseif (-not (Test-Path -LiteralPath ($localPath + '/package.json'))) { throw "Missing local package: $value" }
    }
}
[ordered]@{ dependencies=$dependencies; testables=@('io.github.lingrs-llm-researchteam.mapeditor') } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $projectRoot 'Packages/manifest.json') -Encoding utf8
"m_EditorVersion: 2021.3.39f1`nm_EditorVersionWithRevision: 2021.3.39f1 (fb3b7b32f191)" |
    Set-Content -LiteralPath (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') -Encoding utf8
Write-Output "Prepared isolated development project: $projectRoot"
