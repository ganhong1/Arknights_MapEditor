#requires -Version 7.0
param(
    [Parameter(Mandatory=$true)][string]$DocfxPath,
    [Parameter(Mandatory=$true)][string]$UnityEditorRoot,
    [Parameter(Mandatory=$true)][string]$UnityProject,
    [Parameter(Mandatory=$true)][string]$OutputRoot,
    [string]$CacheRoot
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output = [IO.Path]::GetFullPath($OutputRoot)
if ($output.Equals($repo, [StringComparison]::OrdinalIgnoreCase) -or $output.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Choose an output directory outside the source repository.'
}
if (Test-Path -LiteralPath $output) {
    if (@(Get-ChildItem -LiteralPath $output -Force).Count -gt 0) { throw 'Use a new empty output directory.' }
}
New-Item -ItemType Directory -Force $output | Out-Null
if ([string]::IsNullOrWhiteSpace($CacheRoot)) { $CacheRoot = Join-Path ([IO.Path]::GetDirectoryName($output)) '.map-editor-doc-cache' }
$cache = [IO.Path]::GetFullPath($CacheRoot)
if ($cache.Equals($repo, [StringComparison]::OrdinalIgnoreCase) -or $cache.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Choose a tool cache outside the source repository.'
}
$env:TEMP = Join-Path $output 'temp'
$env:TMP = $env:TEMP
$env:DOTNET_CLI_HOME = Join-Path $cache 'dotnet-home'
$env:NUGET_PACKAGES = Join-Path $cache 'nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:npm_config_cache = Join-Path $cache 'npm-cache'
New-Item -ItemType Directory -Force $env:TEMP,$env:DOTNET_CLI_HOME,$env:NUGET_PACKAGES | Out-Null
& python (Join-Path $PSScriptRoot 'validate_docs.py')
if ($LASTEXITCODE -ne 0) { throw 'Documentation validation failed.' }
$version = (& $DocfxPath --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $version -notmatch '2\.81\.0') { throw 'DocFX 2.81.0 is required.' }
$source = Join-Path $output 'source'
New-Item -ItemType Directory -Force $source | Out-Null
Copy-Item (Join-Path $repo 'docs/*') $source -Recurse
$assemblyRoot = Join-Path ([IO.Path]::GetFullPath($UnityProject)) 'Library/ScriptAssemblies'
$managedRoot = Join-Path ([IO.Path]::GetFullPath($UnityEditorRoot)) 'Data/Managed'
$assemblyNames = @('MapEditor.Core.dll','MapEditor.Storage.dll','MapEditor.Editor.dll')
$assemblies = @($assemblyNames | ForEach-Object {
    $path = Join-Path $assemblyRoot $_
    if (-not (Test-Path -LiteralPath $path)) { throw "Compile the Unity project first: $_ is missing." }
    $path
})
$references = @($assemblies) + @(Get-ChildItem -LiteralPath $managedRoot -Filter '*.dll' -Recurse | ForEach-Object FullName)
$package = Join-Path $repo 'unity/Packages/io.github.lingrs-llm-researchteam.mapeditor'
$references += @(Get-ChildItem -LiteralPath $package -Filter '*.dll' -Recurse | ForEach-Object FullName)
$references = @($references | Where-Object { [IO.Path]::GetFileName($_) -notin @('MapEditor.Core.dll','MapEditor.Storage.dll','MapEditor.Editor.dll','UnityEngine.dll','UnityEditor.dll','ExCSS.Unity.dll') } | Group-Object { [IO.Path]::GetFileName($_) } | ForEach-Object { $_.Group[0] })
$references = @($references | ForEach-Object { @{src=[IO.Path]::GetDirectoryName($_);files=@([IO.Path]::GetFileName($_))} })
$config = Get-Content (Join-Path $source 'docfx.json') -Raw | ConvertFrom-Json -AsHashtable
$config.metadata = @(
    @{src=@(@{src=$package;files=@('Editor/**/*.cs')});references=$references;dest='api/reference';properties=@{TargetFramework='net10.0';DefineConstants='UNITY_EDITOR;UNITY_2021_3_OR_NEWER'}}
)
$config | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $source 'docfx.json') -Encoding utf8
Push-Location $output
try {
    & npm pack swagger-ui-dist@5.34.1 --silent
    if ($LASTEXITCODE -ne 0) { throw 'Swagger UI download failed.' }
    $swagger = Join-Path $source 'http/swagger-ui'
    New-Item -ItemType Directory -Force $swagger | Out-Null
    & tar -xzf (Join-Path $output 'swagger-ui-dist-5.34.1.tgz') -C $swagger --strip-components=1
    if ($LASTEXITCODE -ne 0) { throw 'Swagger UI extraction failed.' }
    $contract = Get-Content (Join-Path $source 'http/openapi.json') -Raw
    [IO.File]::WriteAllText((Join-Path $source 'http/openapi.js'), 'window.levelDataOpenApi=' + $contract + ';')
    & $DocfxPath (Join-Path $source 'docfx.json') --log (Join-Path $output 'docfx.log')
    if ($LASTEXITCODE -ne 0) { throw 'DocFX build failed.' }
} finally { Pop-Location }
$manifest = @{
    utc=[DateTimeOffset]::UtcNow.ToString('O'); docfx=$version; swaggerUi='5.34.1'; openapi='3.2.1'; httpEndpoints=0;
    swaggerArchiveSha256=(Get-FileHash -LiteralPath (Join-Path $output 'swagger-ui-dist-5.34.1.tgz') -Algorithm SHA256).Hash;
    assemblies=@($assemblies | ForEach-Object { @{name=[IO.Path]::GetFileName($_);sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash} });
    sources=@(Get-ChildItem (Join-Path $package 'Editor') -Recurse -Filter '*.cs' | ForEach-Object { @{path=[IO.Path]::GetRelativePath($package,$_.FullName);sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} })
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'build-manifest.json') -Encoding utf8
$site = Join-Path $source 'site'
foreach ($page in @('index.html','api/reference/Arknights.MapEditor.Core.LevelDocument.html','api/reference/Arknights.MapEditor.Editor.DocumentSession.html','http/index.html','http/swagger-ui/swagger-ui-bundle.js')) {
    if (-not (Test-Path (Join-Path $site $page))) { throw "Expected documentation page was not generated: $page" }
}
Write-Output "Documentation site: $site"
