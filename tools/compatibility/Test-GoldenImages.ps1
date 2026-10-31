<#
.SYNOPSIS
    The golden check: the current library reads what every released version wrote (docs/plan/image-compatibility.md).
.DESCRIPTION
    For each version in Solutions/Corvus.Regex.Ecma262.GoldenWriter/ReleasedVersions.txt, builds the GoldenWriter
    against that version's Corvus.Regex.Ecma262.CodeGeneration.Sources package, writes that release's images and
    holders for the fixed sample of the ECMA-262 test data, and runs the data-set runner over the data with them
    (--golden). Fails if any image or holder of any release is not read when it should be, if a holder does not
    compile against the current library, if an answer is wrong, or if an image of the current compiler revision holds
    other than what the current compiler writes. Run it from anywhere; the runner must be built (Release) first.
.PARAMETER Data
    The data directory of a checkout of github.com/corvus-dotnet/ecma262-regex-test-data.
.PARAMETER WorkDirectory
    Where each release's output is written.
.EXAMPLE
    ./tools/compatibility/Test-GoldenImages.ps1 -Data ../ecma262-regex-test-data/data
#>
param(
    [Parameter(Mandatory)][string]$Data,
    [string]$WorkDirectory = (Join-Path ([System.IO.Path]::GetTempPath()) 'corvus-regex-golden')
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '../..')
$writer = Join-Path $root 'Solutions/Corvus.Regex.Ecma262.GoldenWriter'
$runner = Join-Path $root 'Solutions/Corvus.Regex.Ecma262.DatasetRunner/bin/Release/net10.0/Corvus.Regex.Ecma262.DatasetRunner'
if ($IsWindows) { $runner += '.exe' }
if (-not (Test-Path $runner)) {
    throw "Build the runner first: dotnet build Solutions/Corvus.Regex.Ecma262.DatasetRunner -c Release"
}

$versions = @(Get-Content (Join-Path $writer 'ReleasedVersions.txt') | Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object { $_.Trim() })
$failed = @()
foreach ($version in $versions) {
    Write-Host "== $version"
    $out = Join-Path $WorkDirectory $version
    Remove-Item -Recurse -Force $out -ErrorAction SilentlyContinue
    & dotnet build (Join-Path $writer 'Corvus.Regex.Ecma262.GoldenWriter.csproj') -c Release "-p:GoldenVersion=$version" '-m:4' '-nr:false' -v q
    if ($LASTEXITCODE -ne 0) { throw "The golden writer did not build against $version." }

    & dotnet (Join-Path $writer "bin/$version/net10.0/Corvus.Regex.Ecma262.GoldenWriter.dll") $Data $out
    if ($LASTEXITCODE -ne 0) { throw "The golden writer for $version failed." }

    & $runner $Data (Join-Path $WorkDirectory "wrong-cases-golden-$version.tsv") '*.jsonl' both --golden $out
    if ($LASTEXITCODE -ne 0) { $failed += $version }
}

if ($failed.Count -gt 0) {
    throw "The golden check failed for $($failed -join ', '); see the wrong cases in $WorkDirectory."
}
