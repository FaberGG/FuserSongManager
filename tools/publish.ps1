<#
.SYNOPSIS
    Builds the portable release: one self-contained FuserSongManager.exe for win-x64, zipped with a README.

.DESCRIPTION
    ReadyToRun and no single-file compression are deliberate: measured on a 250-song library, the list
    appears in ~0.7 s, against ~1.0 s with compression and ~1.7 s without ReadyToRun (see docs/PLAN_MVP.md).
    The zip is only ~5% bigger than with compression, because the zip compresses the exe anyway.

.EXAMPLE
    ./tools/publish.ps1             # tests, publish, zip into ./artifacts
    ./tools/publish.ps1 -SkipTests
#>
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\artifacts'),
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'src\FuserSongManager.UI\FuserSongManager.UI.csproj'
$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

if (-not $SkipTests) {
    dotnet test (Join-Path $root 'FuserSongManager.slnx') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed; nothing was published.' }
}

# Intermediate files go to %TEMP%: the repository may live in a synced folder (OneDrive) and the exe is ~190 MB.
$work = Join-Path ([IO.Path]::GetTempPath()) 'FuserSongManager-publish'
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
$publish = Join-Path $work 'publish'
dotnet publish $project -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=true `
    -p:EnableCompressionInSingleFile=false `
    -p:DebugType=none `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$staging = Join-Path $work "FuserSongManager-$version"
New-Item -ItemType Directory $staging | Out-Null
Copy-Item (Join-Path $publish 'FuserSongManager.exe') $staging
Copy-Item (Join-Path $PSScriptRoot 'README-portable.txt') (Join-Path $staging 'README.txt')
Copy-Item (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.txt') $staging

New-Item -ItemType Directory -Force $Output | Out-Null
$zip = Join-Path (Resolve-Path $Output) "FuserSongManager-$version-win-x64.zip"
Remove-Item -Force $zip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
Remove-Item -Recurse -Force $work
Write-Host ("Created {0} ({1:N0} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
