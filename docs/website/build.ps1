<#
.SYNOPSIS
    Builds the Corvus.Regex.Ecma262 documentation website.
.DESCRIPTION
    The pipeline:
      0. Install Vellum (the static site generator) if it is not present
      1. Copy the hand-authored source files into the site tree
      2. Build the library with its XML documentation (unless -SkipDotNetBuild)
      3. Generate the API markdown, taxonomy and views from the XML documentation
      4. Generate the docs content from the repository's documents (doc-descriptors/)
      5. Run Vellum to render the site
      6. Compile the SCSS
      7. Copy the API search index and sidebar fragment
      8. Build the site search index
      9. Check for broken links (lychee)
     10. Rewrite root-relative paths for subpath hosting (when -BasePathPrefix is set)
    It is the Corvus.JsonSchema site's build without the second API version, the recipes and the playgrounds.
.PARAMETER Preview
    Launches a local preview server after building.
.PARAMETER ServeOnly
    Skips the build and just starts the preview server on existing output.
.PARAMETER Watch
    Monitors for file changes and regenerates.
.PARAMETER SkipDotNetBuild
    Skips step 2. Use when the solution has already been built in Release.
.PARAMETER IsPreviewDeployment
    When set, the output keeps the noindex meta tag and a robots.txt that disallows indexing.
.PARAMETER BasePathPrefix
    The base path when the site is not hosted at the root ('/Corvus.Regex.Ecma262' on GitHub Pages).
.PARAMETER VellumDownloadToken
    A GitHub token that can read the repository hosting the Vellum tool, when the signed-in CLI cannot.
#>
[CmdletBinding()]
param (
    [Parameter()]
    [switch] $Preview,

    [Parameter()]
    [switch] $ServeOnly,

    [Parameter()]
    [switch] $Watch,

    [Parameter()]
    [string] $BasePathPrefix = "",

    [Parameter()]
    [switch] $SkipDotNetBuild,

    [Parameter()]
    [switch] $IsPreviewDeployment,

    [Parameter()]
    [securestring] $VellumDownloadToken
)

$ErrorActionPreference = 'Stop'
$InformationPreference = 'Continue'

if (!(Get-Command gh -CommandType application -ErrorAction SilentlyContinue)) {
    Write-Error "The GitHub CLI was not found, but is required to build the web site - see https://cli.github.com/ for more details."
}

$here = Split-Path -Parent $PSCommandPath
$repoRoot = Resolve-Path (Join-Path $here ".." "..")
$siteDir = Join-Path $here "site"
$outputDir = Join-Path $here ".output"
$toolProject = Join-Path $here "tools" "XmlDocToMarkdown"
$sharedViewsDir = Join-Path $siteDir "theme" "corvus" "views" "Shared"
$libraryDir = Join-Path $repoRoot "Solutions" "Corvus.Regex.Ecma262"
$libraryProject = Join-Path $libraryDir "Corvus.Regex.Ecma262.csproj"

# The repository URL, from the git remote (SSH normalised to HTTPS, .git stripped).
$canonicalRepoUrl = $null
try {
    $remoteUrl = (git -C $repoRoot remote get-url origin 2>$null)
    if ($remoteUrl) {
        $remoteUrl = $remoteUrl.Trim()
        if ($remoteUrl -match '^git@github\.com:(.+)$') { $remoteUrl = "https://github.com/$($Matches[1])" }
        $canonicalRepoUrl = $remoteUrl -replace '\.git$', ''
    }
} catch { }
if (-not $canonicalRepoUrl) {
    $canonicalRepoUrl = "https://github.com/corvus-dotnet/Corvus.Regex.Ecma262"
    Write-Warning "Could not detect repo URL from git remote - using default: $canonicalRepoUrl"
}

# The ref for GitHub source links: the commit in CI, else the branch.
$canonicalBlobRef = $env:GITHUB_SHA
if (-not $canonicalBlobRef) {
    try { $canonicalBlobRef = (git -C $repoRoot rev-parse --abbrev-ref HEAD 2>$null).Trim() } catch { }
}
if (-not $canonicalBlobRef) { $canonicalBlobRef = "main" }
$canonicalBlobRef = [System.Uri]::EscapeDataString($canonicalBlobRef)

$apiContentDir = Join-Path $siteDir "content" "Api"
$apiTaxonomyDir = Join-Path $siteDir "taxonomy" "api"
$apiViewsDir = Join-Path $siteDir "theme" "corvus" "views" "api"
$nsDescriptionsDir = Join-Path $apiContentDir "namespaces"
$typeExamplesDir = Join-Path $apiContentDir "examples"

function ConvertTo-KebabCase([string]$text) {
    $result = $text -creplace '([a-z0-9])([A-Z])', '$1-$2'
    $result = $result -creplace '([A-Z]+)([A-Z][a-z])', '$1-$2'
    return $result.ToLower()
}

function Write-StepDuration($stepName, $sw) {
    $elapsed = $sw.Elapsed
    if ($elapsed.TotalMinutes -ge 1) {
        Write-Host "  $stepName completed in $([math]::Floor($elapsed.TotalMinutes))m $($elapsed.Seconds)s." -ForegroundColor Green
    } else {
        Write-Host "  $stepName completed in $([math]::Round($elapsed.TotalSeconds, 1))s." -ForegroundColor Green
    }
}

# -- Helper: install Vellum tool (as the Corvus.JsonSchema site does) --------------------------------------------
function Install-Vellum($repoRoot, $vellumVersion, $VellumGitHubRepo, $VellumBasePath, $vellumDownloadToken) {
    $defaultVellumCmd = Join-Path $VellumBasePath 'vellum'
    if ($vellumDownloadToken) {
        Write-Host "Using explicit access token to download Vellum"
        if (Test-Path env:/GH_TOKEN) { $_savedGhToken = $env:GH_TOKEN }
        $env:GH_TOKEN = $vellumDownloadToken
    }

    & gh auth status | Out-Null
    if ($LASTEXITCODE -eq 1) { Write-Error "You are not logged into the GitHub CLI - run 'gh auth login'" }

    # Without access to the Vellum repository the site build is skipped with a warning, not a failure.
    & gh repo view $VellumGitHubRepo --json id | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Skipping web site build - unable to access to the currently closed-source Vellum SSG tooling"
        exit 0
    }

    try {
        $latestVersion = $vellumVersion
        $vellumDownloadPath = Split-Path -Parent $defaultVellumCmd
        if (!(Test-Path $vellumDownloadPath)) { New-Item -ItemType Directory $vellumDownloadPath -Force | Out-Null }
        $dotnetToolBaseArgs = @{ Name = "vellum"; Version = $latestVersion; ToolPath = $vellumDownloadPath }
        # The *-DotNetTool functions come from the ZeroFailed.DevOps.Common extension, in scope when this script
        # runs through the repository's build.ps1.
        if (!(Get-Command Get-DotNetTool -ErrorAction SilentlyContinue)) {
            throw 'Please run this process via the main build script (e.g. ./build.ps1 -Tasks BuildWebsite)'
        }

        $existingTool = Get-DotNetTool @dotnetToolBaseArgs -Verbose
        if (!$existingTool -or $existingTool.Version -ne $latestVersion) {
            $vellumPackageFullName = "vellum.{0}.nupkg" -f $latestVersion
            Write-Build White "Downloading vellum .NET tool package $vellumPackageFullName"
            exec { & gh release download -R $VellumGitHubRepo $latestVersion -p $vellumPackageFullName -D $vellumDownloadPath --clobber }

            # A nuget.config for the install: the repository's, with the local Vellum source and its mapping.
            $repoNugetConfig = Join-Path $repoRoot "nuget.config"
            $tempNugetConfig = Join-Path $VellumBasePath "nuget.config"
            $xml = [xml](Get-Content $repoNugetConfig)
            $sourceEl = $xml.CreateElement("add")
            $sourceEl.SetAttribute("key", "local-vellum")
            $sourceEl.SetAttribute("value", $VellumBasePath)
            $xml.configuration.packageSources.AppendChild($sourceEl) | Out-Null
            $mappingNode = $xml.configuration.packageSourceMapping
            $psElement = $xml.CreateElement("packageSource")
            $psElement.SetAttribute("key", "local-vellum")
            $patternEl = $xml.CreateElement("package")
            $patternEl.SetAttribute("pattern", "vellum")
            $psElement.AppendChild($patternEl) | Out-Null
            $mappingNode.AppendChild($psElement) | Out-Null
            $xml.Save($tempNugetConfig)
            Install-DotNetTool @dotnetToolBaseArgs -AdditionalArgs @("--configfile", $tempNugetConfig) -Verbose
        }
        else {
            Write-Build White "Required version of vellum .NET tool already installed ($latestVersion)"
        }
    }
    finally {
        if (Test-Path variable:/_savedGhToken) { $env:GH_TOKEN = $_savedGhToken }
        elseif (Test-Path env:/GH_TOKEN) { Remove-Item env:/GH_TOKEN }
    }
}

# -- ServeOnly -----------------------------------------------------------------------------------------------------
$serverScript = @"
const http = require('http');
const fs = require('fs');
const path = require('path');
const mimeTypes = {
  '.html': 'text/html', '.css': 'text/css', '.js': 'application/javascript',
  '.json': 'application/json', '.png': 'image/png', '.svg': 'image/svg+xml',
  '.ico': 'image/x-icon', '.woff2': 'font/woff2', '.woff': 'font/woff',
  '.jpg': 'image/jpeg', '.gif': 'image/gif', '.xml': 'application/xml', '.avif': 'image/avif',
};
const root = process.argv[2];
http.createServer((req, res) => {
  let url = req.url.split('?')[0];
  if (url.endsWith('/')) url += 'index.html';
  const fp = path.join(root, url);
  fs.readFile(fp, (err, data) => {
    if (err) { res.writeHead(404); res.end('Not found'); return; }
    const ext = path.extname(fp);
    res.writeHead(200, { 'Content-Type': mimeTypes[ext] || 'application/octet-stream' });
    res.end(data);
  });
}).listen(5000, () => console.log('Serving at http://localhost:5000'));
"@
if ($ServeOnly) {
    if (!(Test-Path $outputDir)) { throw "No output directory found at $outputDir. Run a full build first." }
    Write-Host "Serving $outputDir at http://localhost:5000 (Ctrl+C to stop)" -ForegroundColor Green
    & node -e $serverScript $outputDir
    return
}

# -- Step 0: Install Vellum ----------------------------------------------------------------------------------------
$vellumVersion = "2.0.9"
$vellumDir = Join-Path $here ".endjin"
$vellumCmd = Join-Path $vellumDir "vellum"
if (!(Test-Path $vellumCmd) -and !(Test-Path "$vellumCmd.exe")) {
    Write-Host "`n[0/10] Installing Vellum $vellumVersion..." -ForegroundColor Cyan
    if (!(Test-Path $vellumDir)) { New-Item -ItemType Directory -Path $vellumDir | Out-Null }
    $splat = @($repoRoot, $vellumVersion, 'endjin/Endjin.StaticSiteGen', $vellumDir)
    if ($VellumDownloadToken) { $splat += ConvertFrom-SecureString -AsPlainText $VellumDownloadToken }
    Install-Vellum @splat
    Write-Host "  Vellum installed." -ForegroundColor Green
} else {
    Write-Host "`n[0/10] Vellum already installed." -ForegroundColor DarkGray
}

# -- Step 1: Copy hand-authored source files -----------------------------------------------------------------------
Write-Host "`n[1/10] Copying hand-authored source files..." -ForegroundColor Cyan
$sourceDir = Join-Path $siteDir "source"
foreach ($dir in @("content/Docs", "taxonomy/docs", "taxonomy/api")) {
    $dst = Join-Path $siteDir $dir
    if (!(Test-Path $dst)) { New-Item -ItemType Directory -Path $dst -Force | Out-Null }
}
foreach ($sub in @("docs", "api")) {
    Copy-Item (Join-Path $sourceDir "taxonomy" $sub "index.yml") (Join-Path $siteDir "taxonomy" $sub "index.yml") -Force
}
Write-Host "  Copied source files to site tree." -ForegroundColor Green

# -- Step 2: Build the library -------------------------------------------------------------------------------------
if ($SkipDotNetBuild) {
    Write-Host "`n[2/10] Skipping the library build (-SkipDotNetBuild)." -ForegroundColor DarkGray
} else {
    Write-Host "`n[2/10] Building the library..." -ForegroundColor Cyan
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    & dotnet build $libraryProject -c Release -v q
    if ($LASTEXITCODE -ne 0) { throw "Failed to build the library" }
    Write-StepDuration "Library build" $sw
}

# -- Step 3: Generate API markdown, taxonomy & views ---------------------------------------------------------------
Write-Host "`n[3/10] Generating API markdown, taxonomy & views..." -ForegroundColor Cyan
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$binDir = Join-Path $libraryDir "bin" "Release" "net10.0"
$xmlFile = Join-Path $binDir "Corvus.Regex.Ecma262.xml"
$dllFile = Join-Path $binDir "Corvus.Regex.Ecma262.dll"
$ns20Dll = Join-Path $libraryDir "bin" "Release" "netstandard2.0" "Corvus.Regex.Ecma262.dll"
if (!(Test-Path $xmlFile) -or !(Test-Path $dllFile)) { throw "The library's XML documentation or assembly was not found under ${binDir}: build the solution in Release first." }
$toolArgs = @("--xml", $xmlFile, "--assembly", $dllFile)
if (Test-Path $ns20Dll) { $toolArgs += "--ns20-assembly", $ns20Dll }
# Clean the generated API files (the namespace descriptions and examples are hand-written and stay).
Get-ChildItem $apiContentDir -File -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem $apiTaxonomyDir -Filter "*.yml" -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne "index.yml" } | Remove-Item -Force
& dotnet run --project $toolProject -c Release -- `
    @toolArgs `
    --output $apiContentDir `
    --taxonomy-output $apiTaxonomyDir `
    --api-views-dir $apiViewsDir `
    --shared-views-dir $sharedViewsDir `
    --ns-descriptions $nsDescriptionsDir `
    --type-examples $typeExamplesDir `
    --api-base-url /api `
    --repo-url $canonicalRepoUrl `
    --sidebar-partial-name _ApiSidebar `
    --layout-path "../Shared/_Layout.cshtml"
if ($LASTEXITCODE -ne 0) { throw "API generation failed" }
Write-StepDuration "API generation" $sw

# The API section is every language's: /api/ is a picker of their references (views/Shared/_ApiLanguages.cshtml, written
# by the languages step), and the generator's landing page for .NET's reference becomes /api/dotnet.html.
$apiViewsDir = Join-Path $siteDir "theme" "corvus" "views" "api"
Move-Item (Join-Path $apiViewsDir "index.cshtml") (Join-Path $apiViewsDir "dotnet.cshtml") -Force
$dotnetApiYml = (Get-Content (Join-Path $apiTaxonomyDir "index.yml") -Raw -Encoding utf8) `
    -replace '(?m)^Title: API Reference$', "Title: .NET API Reference`nTemplate: api/dotnet" `
    -replace '(?m)^  Title: API$', '  Title: .NET' `
    -replace '(?m)^  Parent: /$', '  Parent: /api/index.html' `
    -replace '(?m)^  Url: /api/index.html$', '  Url: /api/dotnet.html' `
    -replace '(?m)^  Rank: 3$', '  Rank: 0' `
    -replace '(?m)^(    Visible|    Link): True$', '$1: False'
[System.IO.File]::WriteAllText((Join-Path $apiTaxonomyDir "dotnet.yml"), $dotnetApiYml, [System.Text.Encoding]::UTF8)
$apiIndexYml = @"
ContentType: application/vnd.endjin.ssg.page+yaml
Title: API Reference
Template: api/languages
Navigation:
  Title: API
  Description: "The API reference of each language's package."
  Parent: /
  Url: /api/index.html
  Rank: 3
  Header:
    Visible: True
    Link: True
  Footer:
    Visible: True
    Link: True
MetaData:
  Title: "API Reference - Corvus.Regex.Ecma262"
  Description: "The API reference of the ECMA-262 regular expression package for each language: .NET, Rust, C, C++, Python, Ruby, PHP, Swift, Fortran, R, Java, Go, Julia, Object Pascal and OCaml."
  Keywords: [API, reference, regex, ECMA-262]
OpenGraph:
  Title: "API Reference - Corvus.Regex.Ecma262"
  Description: "The API reference of each language's package."
  Image:
ContentBlocks: []
"@
[System.IO.File]::WriteAllText((Join-Path $apiTaxonomyDir "index.yml"), $apiIndexYml, [System.Text.Encoding]::UTF8)

# -- Step 4: Generate docs content from the repository's documents -------------------------------------------------
Write-Host "`n[4/10] Generating docs content from the repository's documents..." -ForegroundColor Cyan
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$docsSourceDir = Join-Path $repoRoot "docs"
$docsContentDir = Join-Path $siteDir "content" "Docs"
$docsTaxonomyDir = Join-Path $siteDir "taxonomy" "docs"
$descriptorsDir = Join-Path $here "doc-descriptors"
Get-ChildItem $docsContentDir -Filter "*.md" -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem $docsTaxonomyDir -Filter "*.yml" -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne "index.yml" } | Remove-Item -Force
# .NET's documents are a folder of their own under the documentation (/docs/dotnet/).
$dotnetDocsTaxonomyDir = Join-Path $docsTaxonomyDir "dotnet"
if (Test-Path $dotnetDocsTaxonomyDir) { Remove-Item $dotnetDocsTaxonomyDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dotnetDocsTaxonomyDir | Out-Null

# The descriptors in order, and a map from each source document's file name to its page, for the links between them.
$descriptors = @()
$docLinkMap = @{}
foreach ($df in (Get-ChildItem $descriptorsDir -Filter "*.yml" | Sort-Object Name)) {
    $descriptor = @{}
    foreach ($line in ((Get-Content $df.FullName -Raw -Encoding utf8) -split "`n")) {
        $line = $line.Trim()
        if ($line -match '^(\w+):\s*"?(.+?)"?\s*$') { $descriptor[$Matches[1]] = $Matches[2] }
    }
    if (!$descriptor['source']) { Write-Warning "  Descriptor $($df.Name) has no 'source' - skipping"; continue }
    $descriptors += $descriptor
    $docLinkMap[[System.IO.Path]::GetFileName($descriptor['source'])] = (ConvertTo-KebabCase ([System.IO.Path]::GetFileNameWithoutExtension($descriptor['source']))) + ".html"
}

$docCount = 0
foreach ($descriptor in $descriptors) {
    $docFile = $descriptor['source']
    $sourcePath = Join-Path $docsSourceDir $docFile
    if (!(Test-Path $sourcePath)) { Write-Warning "  Source doc not found: $docFile - skipping"; continue }
    $raw = Get-Content $sourcePath -Raw -Encoding utf8
    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($docFile)
    $slug = ConvertTo-KebabCase $baseName
    if ($raw -match '^# (.+?)[\r\n]') { $docTitle = $Matches[1].Trim() -replace '`', '' } else { $docTitle = $baseName }
    $docBody = ($raw -replace '^#[^\n]+\n\s*', '').TrimStart()
    # Links between the documents, written relative to their own directories: (../benchmarks/2026-10-20.md),
    # (2026-10-19.md), (design/design.md#10-phases), each to its page.
    foreach ($srcFile in $docLinkMap.Keys) {
        $escaped = [regex]::Escape($srcFile)
        $target = "/docs/dotnet/$($docLinkMap[$srcFile])"
        $docBody = [regex]::Replace($docBody, "\(((?:\.\./|\./)?[A-Za-z0-9_./-]*?)$escaped(#[^)]+)?\)", "($target`$2)")
    }
    $navTitle = if ($descriptor['navTitle']) { $descriptor['navTitle'] } else { $docTitle }
    $docDescription = if ($descriptor['description']) { $descriptor['description'] } elseif ($docBody -match '^(.+?\.)\s') { $Matches[1] -replace '"', '\"' } else { $docTitle }
    $contentPath = Join-Path $docsContentDir "$baseName.md"
    $titleYaml = $docTitle -replace '"', '\"'
    $frontmatter = "---`nContentType: `"application/vnd.endjin.ssg.content+md`"`nPublicationStatus: Published`nDate: 2026-10-20T00:00:00.0+00:00`nTitle: `"$titleYaml`"`n---`n"
    [System.IO.File]::WriteAllText($contentPath, ($frontmatter + $docBody), [System.Text.Encoding]::UTF8)
    $docRank = $docCount + 1
    $docTaxonomyYml = @"
ContentType: application/vnd.endjin.ssg.page+yaml
Title: "$titleYaml"
Template: docs/doc-page
Navigation:
  Title: "$($navTitle -replace '"', '\"')"
  Description: "$docDescription"
  Parent: /docs/index.html
  Url: /docs/dotnet/$slug.html
  Rank: $docRank
  Header:
    Visible: False
    Link: False
  Footer:
    Visible: False
    Link: False
MetaData:
  Title: "$titleYaml - Corvus.Regex.Ecma262"
  Description: "$docDescription"
  Keywords: [documentation, Corvus.Regex.Ecma262]
OpenGraph:
  Title: "$titleYaml - Corvus.Regex.Ecma262"
  Description: "$docDescription"
  Image:
ContentBlocks:
  - ContentType: application/vnd.endjin.ssg.content+md
    Id: $baseName
    Spec:
      Path: ../../../content/Docs/$baseName.md
"@
    [System.IO.File]::WriteAllText((Join-Path $dotnetDocsTaxonomyDir "$slug.yml"), $docTaxonomyYml, [System.Text.Encoding]::UTF8)
    $docCount++
    Write-Host "  $docTitle -> $slug" -ForegroundColor Gray
}
# This repository's documents are .NET's: they hang under /docs/dotnet/, a page of their own in the documentation's
# navigation, as each language's documents do, so that the site never places one language's documents as every
# language's.
$dotnetDocsYml = @"
ContentType: application/vnd.endjin.ssg.page+yaml
Title: ".NET"
Template: docs/group
Navigation:
  Title: ".NET"
  Description: "The design of the .NET engine, its benchmarks against System.Text.RegularExpressions, and how to contribute."
  Parent: /docs/index.html
  Url: /docs/dotnet/index.html
  Rank: 0
  Header:
    Visible: False
    Link: False
  Footer:
    Visible: False
    Link: False
MetaData:
  Title: ".NET documentation - Corvus.Regex.Ecma262"
  Description: "The design of the .NET engine, its benchmarks against System.Text.RegularExpressions, and how to contribute."
  Keywords: [documentation, .NET, design, benchmarks]
OpenGraph:
  Title: ".NET documentation - Corvus.Regex.Ecma262"
  Description: "The design of the .NET engine, its benchmarks and how to contribute."
  Image:
ContentBlocks:
  - ContentType: application/vnd.endjin.ssg.content+md
    Id: docs-dotnet
    Spec:
      Path: ../../../content/Docs/docs-dotnet.md
"@
[System.IO.File]::WriteAllText((Join-Path $dotnetDocsTaxonomyDir "index.yml"), $dotnetDocsYml, [System.Text.Encoding]::UTF8)
$dotnetGroupBody = "---`nContentType: `"application/vnd.endjin.ssg.content+md`"`nPublicationStatus: Published`nDate: 2026-10-29T00:00:00.0+00:00`nTitle: `".NET`"`n---`n- [Getting started](/getting-started/dotnet.html): install the package and write your first pattern.`n- [API reference](/api/dotnet.html): every name, with its signature.`n- [The repository](https://github.com/corvus-dotnet/Corvus.Regex.Ecma262).`n"
[System.IO.File]::WriteAllText((Join-Path $docsContentDir "docs-dotnet.md"), $dotnetGroupBody, [System.Text.Encoding]::UTF8)
Write-StepDuration "Docs generation ($docCount pages)" $sw

# The languages (languages.json), listed by name wherever they appear. Each engine repository owns its pages, fetched
# here at its latest release: its getting-started page (/getting-started/<id>.html), its API reference (/api/<id>.html
# for a page it generates; or its ecosystem's documentation host, linked only when the host answers), and its other
# documents (/docs/<id>/<slug>.html, under a page that names the language). A page that cannot be fetched (none yet,
# or no network) falls back to the repository, so the site always builds. .NET's pages are this site's own.
$sw = [System.Diagnostics.Stopwatch]::StartNew()
# By name, compared ordinally without case, so that ".NET" comes first and the rest are in alphabetical order.
$languages = [System.Collections.Generic.List[object]]::new()
$languages.AddRange(@((Get-Content (Join-Path $here "languages.json") -Raw -Encoding utf8 | ConvertFrom-Json).languages))
$languages.Sort([System.Comparison[object]] { param($x, $y) [System.StringComparer]::OrdinalIgnoreCase.Compare($x.name, $y.name) })
$langContentDir = Join-Path $siteDir "content" "GettingStarted" "Languages"
$gsTaxonomyDir = Join-Path $siteDir "taxonomy" "getting-started"
$apiLangTaxonomyDir = Join-Path $siteDir "taxonomy" "api-languages"
New-Item -ItemType Directory -Force -Path $langContentDir, $apiLangTaxonomyDir | Out-Null
Get-ChildItem $langContentDir -Filter "*.md" -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem $gsTaxonomyDir -Filter "*.yml" -ErrorAction SilentlyContinue | Where-Object { $_.Name -notin @("index.yml", "dotnet.yml") } | Remove-Item -Force
Get-ChildItem $apiLangTaxonomyDir -Filter "*.yml" -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem $docsTaxonomyDir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne "dotnet" } | Remove-Item -Recurse -Force

function Get-RepoFile($repo, $tag, $path) {
    $text = (& gh api "repos/$repo/contents/${path}?ref=$tag" -H "Accept: application/vnd.github.raw" 2>$null) -join "`n"
    if ($LASTEXITCODE -ne 0 -or !$text) { return $null }
    return $text
}

function Test-Host($url) {
    try {
        $r = Invoke-WebRequest -Uri $url -Method Get -MaximumRedirection 5 -TimeoutSec 30 -UseBasicParsing -ErrorAction Stop
        return $r.StatusCode -eq 200
    } catch { return $false }
}

function Write-SitePage($taxonomyPath, $contentPath, $contentRelative, $template, $title, $navTitle, $summary, $parent, $url, $rank, $body) {
    $titleYaml = $title -replace '"', '\"'
    $summaryYaml = $summary -replace '"', '\"'
    $frontmatter = "---`nContentType: `"application/vnd.endjin.ssg.content+md`"`nPublicationStatus: Published`nDate: 2026-10-29T00:00:00.0+00:00`nTitle: `"$titleYaml`"`n---`n"
    [System.IO.File]::WriteAllText($contentPath, ($frontmatter + $body), [System.Text.Encoding]::UTF8)
    $yml = @"
ContentType: application/vnd.endjin.ssg.page+yaml
Title: "$titleYaml"
Template: $template
Navigation:
  Title: "$($navTitle -replace '"', '\"')"
  Description: "$summaryYaml"
  Parent: $parent
  Url: $url
  Rank: $rank
  Header:
    Visible: False
    Link: False
  Footer:
    Visible: False
    Link: False
MetaData:
  Title: "$titleYaml - Corvus.Regex.Ecma262"
  Description: "$summaryYaml"
  Keywords: [regex, ECMA-262, $($navTitle -replace '[\[\],]', '')]
OpenGraph:
  Title: "$titleYaml - Corvus.Regex.Ecma262"
  Description: "$summaryYaml"
  Image:
ContentBlocks:
  - ContentType: application/vnd.endjin.ssg.content+md
    Id: $([System.IO.Path]::GetFileNameWithoutExtension($contentPath))
    Spec:
      Path: $contentRelative
"@
    New-Item -ItemType Directory -Force -Path ([System.IO.Path]::GetDirectoryName($taxonomyPath)) | Out-Null
    [System.IO.File]::WriteAllText($taxonomyPath, $yml, [System.Text.Encoding]::UTF8)
}

# First every file is fetched, and where the site publishes it is noted (repository and path to site address), so that
# a link from one page to another the site publishes, in any repository, goes to the site's page.
$fetched = @{}
$siteUrlOf = @{}
foreach ($lang in $languages) {
    $lang | Add-Member -NotePropertyName tag -NotePropertyValue $null -Force
    $lang | Add-Member -NotePropertyName pageText -NotePropertyValue $null -Force
    $lang | Add-Member -NotePropertyName apiText -NotePropertyValue $null -Force
    $lang | Add-Member -NotePropertyName apiHref -NotePropertyValue $null -Force
    $lang | Add-Member -NotePropertyName docTexts -NotePropertyValue @() -Force
    if ($lang.local) { continue }
    $tag = (& gh api "repos/$($lang.repo)/releases/latest" --jq .tag_name 2>$null)
    if ($LASTEXITCODE -ne 0 -or !$tag) { Write-Host "  $($lang.name): no release could be read - every card links its repository" -ForegroundColor DarkYellow; continue }
    $lang.tag = $tag
    if ($lang.page) {
        $lang.pageText = Get-RepoFile $lang.repo $tag $lang.page
        if ($lang.pageText) { $siteUrlOf["$($lang.repo)|$($lang.page)"] = "/getting-started/$($lang.id).html" }
    }
    if ($lang.api.page) {
        $lang.apiText = Get-RepoFile $lang.repo $tag $lang.api.page
        if ($lang.apiText) { $siteUrlOf["$($lang.repo)|$($lang.api.page)"] = "/api/$($lang.id).html"; $lang.apiHref = "/api/$($lang.id).html" }
    } elseif ($lang.api.url) {
        if (Test-Host $lang.api.url) { $lang.apiHref = $lang.api.url }
        else { Write-Host "  $($lang.name): $($lang.api.url) does not answer yet - its API card links the repository" -ForegroundColor DarkYellow }
    }
    $docs = @()
    foreach ($doc in @($lang.docs)) {
        if (!$doc) { continue }
        $text = Get-RepoFile $lang.repo $tag $doc.path
        if (!$text) { continue }
        $slug = ConvertTo-KebabCase ([System.IO.Path]::GetFileNameWithoutExtension($doc.path))
        $siteUrlOf["$($lang.repo)|$($doc.path)"] = "/docs/$($lang.id)/$slug.html"
        $docs += [pscustomobject]@{ doc = $doc; text = $text; slug = $slug }
    }
    $lang.docTexts = $docs
}

# A page's body: its title line taken off, and each link made absolute: to the site's page where the site publishes
# the file (this repository's or another's), else to the file in its repository at the same release.
function Convert-Body($lang, $path, $raw) {
    if ($raw -match '^# (.+?)[\r\n]') { $title = $Matches[1].Trim() -replace '`', '' } else { $title = $null }
    $body = ($raw -replace '^#[^\n]+\n\s*', '').TrimStart()
    $dir = [System.IO.Path]::GetDirectoryName($path) -replace '\\', '/'
    $body = [regex]::Replace($body, '\]\(([^)\s]+)\)', {
        param($m)
        $target = $m.Groups[1].Value
        if ($target -match '^(#|mailto:)') { return $m.Value }
        $fragment = ''
        if ($target -match '^([^#]*)(#.*)$') { $target = $Matches[1]; $fragment = $Matches[2] }
        if ($target -match '^https://github\.com/([^/]+/[^/]+)/blob/[^/]+/(.+)$') { $key = "$($Matches[1])|$($Matches[2])" }
        elseif ($target -match '^https?://') { return $m.Value }
        else { $key = "$($lang.repo)|" + [System.Uri]::new([System.Uri]::new("https://x/$dir/"), $target).AbsolutePath.TrimStart('/') }
        if ($siteUrlOf.ContainsKey($key)) { return "]($($siteUrlOf[$key])$fragment)" }
        if ($target -match '^https?://') { return $m.Value }
        return "](https://github.com/$($lang.repo)/blob/$($lang.tag)/$($key.Split('|')[1])$fragment)"
    })
    return @{ title = $title; body = $body }
}

$pages = 0
$rank = 1
foreach ($lang in $languages) {
    if ($lang.local) { continue }
    $repoUrl = "https://github.com/$($lang.repo)"
    if ($lang.pageText) {
        $c = Convert-Body $lang $lang.page $lang.pageText
        $title = if ($c.title) { $c.title } else { "Getting started with $($lang.name)" }
        Write-SitePage (Join-Path $gsTaxonomyDir "$($lang.id).yml") (Join-Path $langContentDir "$($lang.id).md") "../../content/GettingStarted/Languages/$($lang.id).md" `
            "getting-started/language" $title $lang.name $lang.summary "/getting-started/index.html" "/getting-started/$($lang.id).html" $rank $c.body
        $pages++
    }
    if ($lang.apiText) {
        $c = Convert-Body $lang $lang.api.page $lang.apiText
        $title = if ($c.title) { "$($lang.name): $($c.title)" } else { "$($lang.name) API reference" }
        Write-SitePage (Join-Path $apiLangTaxonomyDir "$($lang.id).yml") (Join-Path $langContentDir "api-$($lang.id).md") "../../content/GettingStarted/Languages/api-$($lang.id).md" `
            "getting-started/language" $title $lang.name "The API reference of the $($lang.name) package." "/api/index.html" "/api/$($lang.id).html" $rank $c.body
        $pages++
    }
    # The language's documentation: a page naming the language with its getting-started page and API reference, and
    # each document beside it, one level under /docs/ (Vellum's navigation has two levels; the views group by folder).
    $groupDir = Join-Path $docsTaxonomyDir $lang.id
    $start = if ($lang.pageText) { "/getting-started/$($lang.id).html" } else { $repoUrl }
    $api = if ($lang.apiHref) { $lang.apiHref } else { $repoUrl }
    $groupBody = "- [Getting started]($start): install the package and write your first pattern.`n- [API reference]($api): every name, with its signature.`n- [The repository]($repoUrl), at $($lang.tag).`n"
    Write-SitePage (Join-Path $groupDir "index.yml") (Join-Path $langContentDir "docs-$($lang.id).md") "../../../content/GettingStarted/Languages/docs-$($lang.id).md" `
        "docs/group" $lang.name $lang.name "The documentation of the $($lang.name) package: getting started, the API reference and the repository's documents." "/docs/index.html" "/docs/$($lang.id)/index.html" $rank $groupBody
    $docRank = 1
    foreach ($d in $lang.docTexts) {
        $c = Convert-Body $lang $d.doc.path $d.text
        $title = if ($c.title) { $c.title } else { $d.doc.title }
        Write-SitePage (Join-Path $groupDir "$($d.slug).yml") (Join-Path $langContentDir "doc-$($lang.id)-$($d.slug).md") "../../../content/GettingStarted/Languages/doc-$($lang.id)-$($d.slug).md" `
            "docs/doc-page" $title $d.doc.title $d.doc.summary "/docs/index.html" "/docs/$($lang.id)/$($d.slug).html" ($rank * 100 + $docRank) $c.body
        $docRank++
        $pages++
    }
    $rank++
    Write-Host "  $($lang.name) at $($lang.tag): page $([bool]$lang.pageText), API $(if ($lang.apiHref) { $lang.apiHref } else { 'repository' }), $(@($lang.docTexts).Count) documents" -ForegroundColor Gray
}

# The pickers: a card for each language, by name. Razor reads '@' as code, so it is doubled.
$enc = { param($t) ([System.Net.WebUtility]::HtmlEncode([string]$t)) -replace '@', '@@' }
function New-Picker($cards) { "@* Generated by build.ps1 from languages.json. *@`n<div class=`"lang-grid`">`n$($cards -join "`n")`n</div>`n" }
function New-Card($name, $href, $summary, $codeLine) {
    $external = $href.StartsWith("https://")
    $target = if ($external) { ' target="_blank" rel="noopener noreferrer"' } else { '' }
    $note = if ($external) { '<span class="lang-card__note">&#8599;</span>' } else { '' }
    $code = if ($codeLine) { "`n        <code class=`"lang-card__install`">$(& $enc $codeLine)</code>" } else { '' }
    @"
    <a class="lang-card" href="$(& $enc $href)"$target>
        <span class="lang-card__name">$(& $enc $name)$note</span>
        <span class="lang-card__summary">$(& $enc $summary)</span>$code
    </a>
"@
}
$startCards = foreach ($lang in $languages) {
    $href = if ($lang.local) { "/getting-started/$($lang.id).html" } elseif ($lang.pageText) { "/getting-started/$($lang.id).html" } else { "https://github.com/$($lang.repo)" }
    New-Card $lang.name $href $lang.summary $lang.install
}
$apiCards = foreach ($lang in $languages) {
    $href = if ($lang.local) { "/api/dotnet.html" } elseif ($lang.apiHref) { $lang.apiHref } else { "https://github.com/$($lang.repo)" }
    $where = if ($lang.local -or ($lang.apiHref -and !$lang.apiHref.StartsWith("https://"))) { "Every name, with its signature." } elseif ($lang.apiHref) { "On " + ([System.Uri]$lang.apiHref).Host + "." } else { "Not published yet: the repository." }
    New-Card $lang.name $href $where $lang.package
}
$viewsShared = Join-Path $siteDir "theme" "corvus" "views" "Shared"
[System.IO.File]::WriteAllText((Join-Path $viewsShared "_Languages.cshtml"), (New-Picker $startCards), [System.Text.Encoding]::UTF8)
[System.IO.File]::WriteAllText((Join-Path $viewsShared "_ApiLanguages.cshtml"), (New-Picker $apiCards), [System.Text.Encoding]::UTF8)
# The documentation index: for each language, by name, its getting-started page, its API reference and its documents.
$docSections = foreach ($lang in $languages) {
    $repoUrl = "https://github.com/$($lang.repo)"
    $cards = @()
    if ($lang.local) {
        $cards += New-Card "Getting started" "/getting-started/$($lang.id).html" "Install the package and write your first pattern." $null
        $cards += New-Card "API reference" "/api/dotnet.html" "Every name, with its signature." $null
        foreach ($descriptor in $descriptors) {
            $slug = ConvertTo-KebabCase ([System.IO.Path]::GetFileNameWithoutExtension($descriptor['source']))
            $cards += New-Card $descriptor['navTitle'] "/docs/dotnet/$slug.html" $descriptor['description'] $null
        }
    } else {
        $start = if ($lang.pageText) { "/getting-started/$($lang.id).html" } else { $repoUrl }
        $api = if ($lang.apiHref) { $lang.apiHref } else { $repoUrl }
        $cards += New-Card "Getting started" $start $(if ($lang.pageText) { "Install the package and write your first pattern." } else { "Not published yet: the repository." }) $null
        $cards += New-Card "API reference" $api $(if ($lang.apiHref -and $lang.apiHref.StartsWith("https://")) { "On " + ([System.Uri]$lang.apiHref).Host + "." } elseif ($lang.apiHref) { "Every name, with its signature." } else { "Not published yet: the repository." }) $null
        foreach ($d in $lang.docTexts) { $cards += New-Card $d.doc.title "/docs/$($lang.id)/$($d.slug).html" $d.doc.summary $null }
    }
    "<h2 id=`"$($lang.id)`"><a href=`"/docs/$($lang.id)/index.html`">$(& $enc $lang.name)</a></h2>`n<div class=`"lang-grid`">`n$($cards -join "`n")`n</div>"
}
[System.IO.File]::WriteAllText((Join-Path $viewsShared "_DocsLanguages.cshtml"), "@* Generated by build.ps1 from languages.json. *@`n$($docSections -join "`n")`n", [System.Text.Encoding]::UTF8)
Write-StepDuration "Languages ($pages pages fetched)" $sw

# -- Step 5: Run Vellum --------------------------------------------------------------------------------------------
Write-Host "`n[5/10] Running Vellum..." -ForegroundColor Cyan
$sw = [System.Diagnostics.Stopwatch]::StartNew()
if (Test-Path $outputDir) {
    try { Remove-Item $outputDir -Recurse -Force }
    catch { Get-ChildItem $outputDir -Force | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue }
}
$assetsSource = Join-Path $siteDir "theme" "corvus" "assets"
Copy-Item -Path $assetsSource -Destination (Join-Path $outputDir "assets") -Recurse -Force
Push-Location $siteDir
try {
    $vellumArgs = @("content", "generate", "-t", (Join-Path $siteDir "site.yml"), "-o", $outputDir)
    if ($Watch) { $vellumArgs += "--watch" }
    & $vellumCmd $vellumArgs
    if ($LASTEXITCODE -ne 0) { throw "Vellum generation failed" }
} finally {
    Pop-Location
}
foreach ($dir in @("taxonomy", "content", "theme", "source")) {
    $spurious = Join-Path $outputDir $dir
    if (Test-Path $spurious) { Remove-Item $spurious -Recurse -Force }
}
$spurious = Join-Path $outputDir "site.yml"
if (Test-Path $spurious) { Remove-Item $spurious -Force }
Write-StepDuration "Vellum site generation" $sw

# -- Step 5b: the benchmark pages that used to live here send a reader to the central benchmark site, the one page --
Write-Host "`n[5b/10] Writing the benchmarks redirects..." -ForegroundColor Cyan
$central = "https://corvus-oss.org/corvus-regex-ecma262-benchmarks/"
$redirect = "<!doctype html><html lang=`"en`"><head><meta charset=`"utf-8`"><meta http-equiv=`"refresh`" content=`"0; url=$central`"><link rel=`"canonical`" href=`"$central`"><title>Benchmarks</title></head><body><p>The benchmarks are at <a href=`"$central`">$central</a>.</p></body></html>"
foreach ($dir in @("benchmarks", "benchmarks/ci", "benchmarks/ci/runs")) {
    New-Item -ItemType Directory -Force (Join-Path $outputDir $dir) | Out-Null
    [IO.File]::WriteAllText((Join-Path (Join-Path $outputDir $dir) "index.html"), $redirect)
}
if ($LASTEXITCODE -ne 0) { throw "The benchmark results page failed" }
# .NET's documents moved under /docs/dotnet/: each old address sends a reader to the new one (relative, so the path
# rewriting of a sub-path does not touch it).
foreach ($descriptor in $descriptors) {
    $slug = ConvertTo-KebabCase ([System.IO.Path]::GetFileNameWithoutExtension($descriptor['source']))
    $to = "dotnet/$slug.html"
    $moved = "<!doctype html><html lang=`"en`"><head><meta charset=`"utf-8`"><meta http-equiv=`"refresh`" content=`"0; url=$to`"><title>Moved</title></head><body><p>This page is now at <a href=`"$to`">$to</a>.</p></body></html>"
    [IO.File]::WriteAllText((Join-Path $outputDir "docs" "$slug.html"), $moved)
}

# -- Step 6: Compile SCSS ------------------------------------------------------------------------------------------
Write-Host "`n[6/10] Compiling SCSS..." -ForegroundColor Cyan
$sw = [System.Diagnostics.Stopwatch]::StartNew()
Push-Location $here
try { & npm ci --ignore-scripts --no-audit --no-fund 2>&1 | Out-Null } finally { Pop-Location }
& npx --prefix $here sass (Join-Path $assetsSource "css" "scss" "main.scss") (Join-Path $outputDir "main.css") --style=compressed --no-source-map
if ($LASTEXITCODE -ne 0) { throw "SCSS compilation failed" }
Write-StepDuration "SCSS compilation" $sw

# -- Step 7: Copy the API search index and sidebar fragment --------------------------------------------------------
Write-Host "`n[7/10] Copying API search index and sidebar fragment..." -ForegroundColor Cyan
foreach ($name in @("search-index.json", "sidebar.html")) {
    $src = Join-Path $apiContentDir $name
    $dst = Join-Path $outputDir "api" $name
    if (Test-Path $src) {
        Copy-Item $src $dst -Force
        Write-Host "  $name`: $([math]::Round((Get-Item $dst).Length/1024,1)) KB"
    } else { Write-Warning "  $name not found at $src" }
}

# -- Step 8: Build search index ------------------------------------------------------------------------------------
Write-Host "`n[8/10] Building search index..." -ForegroundColor Cyan
$sw = [System.Diagnostics.Stopwatch]::StartNew()
& node (Join-Path $here "tools" "build-search-index.js") --output (Join-Path $outputDir "search-index.json")
if ($LASTEXITCODE -ne 0) { Write-Warning "Search index generation failed - site will build without search." } else { Write-StepDuration "Search index" $sw }

# -- Step 9: Check for broken links (lychee), before the paths are rewritten ---------------------------------------
Write-Host "`n[9/10] Checking for broken links..." -ForegroundColor Cyan
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$lycheeVersion = "0.23.0"
$onWindows = $IsWindows -or $env:OS -eq "Windows_NT"
$lycheeCmd = if ($onWindows) { Join-Path $vellumDir "lychee.exe" } else { Join-Path $vellumDir "lychee" }
if (!(Test-Path $lycheeCmd)) {
    Write-Host "  Installing lychee $lycheeVersion..." -ForegroundColor Gray
    if ($onWindows) {
        Invoke-WebRequest -Uri "https://github.com/lycheeverse/lychee/releases/download/lychee-v$lycheeVersion/lychee-x86_64-windows.exe" -OutFile $lycheeCmd -UseBasicParsing
    } else {
        $lycheeAsset = "lychee-x86_64-unknown-linux-gnu.tar.gz"
        $lycheeTar = Join-Path $vellumDir $lycheeAsset
        Invoke-WebRequest -Uri "https://github.com/lycheeverse/lychee/releases/download/lychee-v$lycheeVersion/$lycheeAsset" -OutFile $lycheeTar -UseBasicParsing
        tar -xzf $lycheeTar -C $vellumDir "lychee"
        Remove-Item $lycheeTar -Force
        chmod +x $lycheeCmd
    }
    Write-Host "  lychee installed." -ForegroundColor Green
}
$absOutputDir = (Resolve-Path $outputDir).Path
$lycheeArgs = @(
    "--root-dir", $absOutputDir
    "--include-fragments"
    "--no-progress"
    "--max-retries", "5"
    "--retry-wait-time", "5"
    "--accept", "100..=103,200..=299,429,502,503,504"
    "."
)
Push-Location $outputDir
try {
    $lycheeIgnore = Join-Path $here ".lycheeignore"
    if (Test-Path $lycheeIgnore) { Copy-Item $lycheeIgnore -Destination ".lycheeignore" }
    & $lycheeCmd @lycheeArgs
} finally {
    Pop-Location
}
if ($LASTEXITCODE -ne 0) { throw "Broken links found - see output above" }
Write-StepDuration "Link check" $sw

# -- Step 10: Rewrite root-relative paths for subpath hosting ------------------------------------------------------
if ($BasePathPrefix) {
    Write-Host "`n[10/10] Rewriting paths for base prefix '$BasePathPrefix'..." -ForegroundColor Cyan
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $rewriteCount = 0
    foreach ($htmlFile in (Get-ChildItem $outputDir -Filter "*.html" -Recurse -File)) {
        $content = [System.IO.File]::ReadAllText($htmlFile.FullName)
        $original = $content
        $content = $content -replace '(href|src|content|data-search-index)="(/(?!/))', "`$1=`"$BasePathPrefix`$2"
        if ($content -ne $original) { [System.IO.File]::WriteAllText($htmlFile.FullName, $content); $rewriteCount++ }
    }
    Write-Host "  Rewrote paths in $rewriteCount HTML files." -ForegroundColor Gray
    foreach ($jsFile in (Get-ChildItem (Join-Path $outputDir "assets" "js") -Filter "*.js" -File -ErrorAction SilentlyContinue)) {
        $content = [System.IO.File]::ReadAllText($jsFile.FullName)
        $original = $content
        $content = $content -replace "(['""])(/(?!/)(?:api|search|docs|getting-started|assets))", "`$1$BasePathPrefix`$2"
        if ($content -ne $original) { [System.IO.File]::WriteAllText($jsFile.FullName, $content) }
    }
    foreach ($jsonFile in (Get-ChildItem $outputDir -Filter "search-index.json" -Recurse -File -ErrorAction SilentlyContinue)) {
        $content = [System.IO.File]::ReadAllText($jsonFile.FullName)
        $original = $content
        $content = $content -replace '("Url"\s*:\s*")(/(?!/)(?:api|docs|getting-started))', "`$1$BasePathPrefix`$2"
        if ($content -ne $original) { [System.IO.File]::WriteAllText($jsonFile.FullName, $content) }
    }
    Write-StepDuration "Path rewriting" $sw
} else {
    Write-Host "`n[10/10] No base path prefix - skipping path rewriting." -ForegroundColor DarkGray
}

# robots.txt: indexing for the production build only.
if ($env:GITHUB_ACTIONS -and -not $Preview -and -not $IsPreviewDeployment) {
    [System.IO.File]::WriteAllText((Join-Path $outputDir "robots.txt"), "User-agent: *`nAllow: /`n")
    Get-ChildItem -Path $outputDir -Filter "*.html" -Recurse | ForEach-Object {
        $content = [System.IO.File]::ReadAllText($_.FullName)
        $newContent = $content -replace '\s*<meta name="robots" content="noindex, nofollow"\s*/>', ''
        if ($content -ne $newContent) { [System.IO.File]::WriteAllText($_.FullName, $newContent) }
    }
    Write-Host "  robots.txt allows indexing; the noindex tag is stripped." -ForegroundColor Gray
} else {
    [System.IO.File]::WriteAllText((Join-Path $outputDir "robots.txt"), "User-agent: *`nDisallow: /`n")
    Write-Host "  robots.txt disallows indexing (local or preview build)." -ForegroundColor Gray
}

Write-Host "`nBuild complete! Output: $outputDir" -ForegroundColor Green
if ($Preview) {
    Write-Host "`nStarting preview server..." -ForegroundColor Cyan
    & node -e $serverScript $outputDir
}
