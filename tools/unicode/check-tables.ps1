<#
.SYNOPSIS
    Makes the library's Unicode data again from the Unicode Character Database and fails unless it is the checked-in
    data, byte for byte.

.DESCRIPTION
    The library's tables of each version of Unicode (Unicode/EcmaRegexUnicodeData<major>.cs), its properties of strings
    (Unicode/EcmaRegexUnicodeStrings<major>.cs) and the property names of ECMAScript 2020 (Unicode/Es2020Properties.cs)
    are generated. This script runs
    the whole chain from its sources, so that a table cannot be edited by hand or go stale:

    - the Unicode Character Database of each version (UCD.zip) and its two emoji sequence files, from unicode.org,
      each checked against its SHA-256 below;
    - regress's gen-unicode, at the commit of regress 0.12.0, which writes unicodetables.rs from the database;
    - gen-ecma-unicode-data.ps1, which writes EcmaRegexUnicodeData<major>.cs from that, and
      gen-ecma-unicode-strings.ps1, which writes EcmaRegexUnicodeStrings<major>.cs from the emoji sequence files;
    - gen-es2020-properties.ps1, which writes Es2020Properties.cs from es2020-properties.json of the test data.

    gen-unicode reads the database from /tmp/ucd-17.0.0 whatever its version, so the script points that path at each
    version in turn, as a symbolic link. It runs on Linux and macOS. gen-unicode formats its output with cargo fmt, and
    the generators read that text, so the script runs it under one Rust toolchain ($toolchain, installed with rustfmt),
    the Rust crate's. regress keeps no Cargo.lock, so gen-unicode is built --locked with regress-0.12.0.Cargo.lock
    beside this script.

.PARAMETER Properties
    The path of es2020-properties.json, in a checkout of the test data.

.PARAMETER WorkDirectory
    Where the sources are fetched and the data is made. The default is a new directory under the system's temporary
    directory.

.EXAMPLE
    pwsh tools/unicode/check-tables.ps1 -Properties ../ecma262-regex-test-data/es2020-properties.json
#>
param(
    [Parameter(Mandatory = $true)][string]$Properties,
    [string]$WorkDirectory = ''
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$regressCommit = 'aae35dee1596cc9a9caaad2425b088081c09e5d5'
$toolchain = '1.98.0'
$env:RUSTUP_TOOLCHAIN = $toolchain
$versions = @(
    @{
        Version = '13.0.0'
        Ucd = 'https://www.unicode.org/Public/13.0.0/ucd/UCD.zip'
        UcdHash = '2f76973b4d36ae45584f5a45ec65b47138932d777dd23a5669c89535ef3da951'
        Emoji = 'https://www.unicode.org/Public/emoji/13.0'
        SequencesHash = '5a864aab19aa703d019647f01a5b6fe127b7dbedff3a9569242224b4a4e9341a'
        ZwjSequencesHash = '1d8f06c1dca8ebecb0ca7b753bc52efacdf44c60a921f2ab04f02b42ce1389bf'
    },
    @{
        Version = '17.0.0'
        Ucd = 'https://www.unicode.org/Public/17.0.0/ucd/UCD.zip'
        UcdHash = '2066d1909b2ea93916ce092da1c0ee4808ea3ef8407c94b4f14f5b7eb263d28e'
        Emoji = 'https://www.unicode.org/Public/17.0.0/emoji'
        SequencesHash = '12cc8267dc33cbd11ed32bcf6fc5dc2ad9c7a77bae1bdfba2f41b1b9b3ead8dd'
        ZwjSequencesHash = '5b25441daed2322b068c5e70cda522946a4f0274df864445a1965a92e5fc5cad'
    },
    @{
        Version = '18.0.0'
        Ucd = 'https://www.unicode.org/Public/18.0.0/ucd/UCD.zip'
        UcdHash = '7b3e555514060b92290d154f53655c5eb0fa62b16eb04c03434ff72d1a66a0d8'
        Emoji = 'https://www.unicode.org/Public/18.0.0/emoji'
        SequencesHash = '1823dce71f3dd9cb0ad1976797baee4ffbdc1909756f43af8ce4c57f732843a4'
        ZwjSequencesHash = 'f61b5213bdf85a57741a9f903cb38d0f39a3d13be6ff4aca3b39857e859a9191'
    }
)

$root = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$src = Join-Path $root 'Solutions' 'Corvus.Regex.Ecma262' 'Unicode'
$relative = 'Solutions/Corvus.Regex.Ecma262/Unicode'
if (-not $WorkDirectory) {
    $WorkDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "corvus-unicode-check-$([System.IO.Path]::GetRandomFileName())"
}
New-Item -ItemType Directory -Force $WorkDirectory | Out-Null
$WorkDirectory = (Resolve-Path $WorkDirectory).Path

function Get-Source([string]$Uri, [string]$Path, [string]$Hash) {
    Invoke-WebRequest -Uri $Uri -OutFile $Path -UseBasicParsing
    $got = (Get-FileHash -Algorithm SHA256 $Path).Hash.ToLowerInvariant()
    if ($got -ne $Hash) { throw "$Uri has SHA-256 $got, not $Hash." }
}

$failed = @()
function Compare-Made([string]$Made, [string]$Name) {
    $checkedIn = Join-Path $src $Name
    $same = (Test-Path $checkedIn) -and
        ((Get-FileHash -Algorithm SHA256 $Made).Hash -eq (Get-FileHash -Algorithm SHA256 $checkedIn).Hash)
    if ($same) {
        Write-Host "$Name is what its sources make."
    } else {
        Write-Host "::error file=$relative/$Name::$Name is not what its sources make (the file made is $Made)."
        $script:failed += $Name
    }
}

$regress = Join-Path $WorkDirectory 'regress'
if (-not (Test-Path $regress)) {
    git clone --quiet https://github.com/ridiculousfish/regress $regress
}
git -C $regress checkout --quiet $regressCommit
Copy-Item (Join-Path $PSScriptRoot 'regress-0.12.0.Cargo.lock') (Join-Path $regress 'Cargo.lock')

$link = '/tmp/ucd-17.0.0'
foreach ($v in $versions) {
    $version = $v.Version
    $major = $version.Split('.')[0]
    $ucd = Join-Path $WorkDirectory "ucd-$version"
    if (Test-Path $ucd) { Remove-Item -Recurse -Force $ucd }
    New-Item -ItemType Directory $ucd | Out-Null
    $zip = Join-Path $WorkDirectory "UCD-$version.zip"
    Get-Source $v.Ucd $zip $v.UcdHash
    Expand-Archive -Path $zip -DestinationPath $ucd
    $sequences = Join-Path $ucd 'emoji-sequences.txt'
    $zwjSequences = Join-Path $ucd 'emoji-zwj-sequences.txt'
    Get-Source "$($v.Emoji)/emoji-sequences.txt" $sequences $v.SequencesHash
    Get-Source "$($v.Emoji)/emoji-zwj-sequences.txt" $zwjSequences $v.ZwjSequencesHash

    $existing = Get-Item $link -Force -ErrorAction SilentlyContinue
    if ($existing) {
        if (-not $existing.LinkType) { throw "$link is not a symbolic link: move it, as gen-unicode reads the database from it." }
        Remove-Item $link -Force
    }
    New-Item -ItemType SymbolicLink -Path $link -Target $ucd | Out-Null

    Push-Location (Join-Path $regress 'gen-unicode')
    try {
        cargo run --quiet -j 4 --release --locked
    } finally {
        Pop-Location
    }
    $regressTables = Join-Path $WorkDirectory "unicodetables-$version.rs"
    Copy-Item (Join-Path $regress 'src' 'unicodetables.rs') $regressTables

    $tables = Join-Path $WorkDirectory "EcmaRegexUnicodeData$major.cs"
    & (Join-Path $PSScriptRoot 'gen-ecma-unicode-data.ps1') -RegressTables $regressTables -UnicodeVersion $version -Output $tables
    Compare-Made $tables "EcmaRegexUnicodeData$major.cs"

    $strings = Join-Path $WorkDirectory "EcmaRegexUnicodeStrings$major.cs"
    & (Join-Path $PSScriptRoot 'gen-ecma-unicode-strings.ps1') -EmojiSequences $sequences -EmojiZwjSequences $zwjSequences -Output $strings
    Compare-Made $strings "EcmaRegexUnicodeStrings$major.cs"
}
Remove-Item $link -Force

$es2020 = Join-Path $WorkDirectory 'Es2020Properties.cs'
& (Join-Path $PSScriptRoot 'gen-es2020-properties.ps1') -Properties $Properties -Output $es2020
Compare-Made $es2020 'Es2020Properties.cs'

if ($failed.Count -gt 0) {
    Write-Host "$($failed.Count) generated file(s) differ from what their sources make: $($failed -join ', ')."
    exit 1
}
Write-Host 'Every generated Unicode file is what its sources make.'
