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

# -- Step 4: Generate docs content from the repository's documents -------------------------------------------------
Write-Host "`n[4/10] Generating docs content from the repository's documents..." -ForegroundColor Cyan
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$docsSourceDir = Join-Path $repoRoot "docs"
$docsContentDir = Join-Path $siteDir "content" "Docs"
$docsTaxonomyDir = Join-Path $siteDir "taxonomy" "docs"
$descriptorsDir = Join-Path $here "doc-descriptors"
Get-ChildItem $docsContentDir -Filter "*.md" -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem $docsTaxonomyDir -Filter "*.yml" -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne "index.yml" } | Remove-Item -Force

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
        $target = "/docs/$($docLinkMap[$srcFile])"
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
  Url: /docs/$slug.html
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
      Path: ../../content/Docs/$baseName.md
"@
    [System.IO.File]::WriteAllText((Join-Path $docsTaxonomyDir "$slug.yml"), $docTaxonomyYml, [System.Text.Encoding]::UTF8)
    $docCount++
    Write-Host "  $docTitle -> $slug" -ForegroundColor Gray
}
Write-StepDuration "Docs generation ($docCount pages)" $sw

# The languages (languages.json): each engine repository's getting-started page, fetched at its latest release and
# published as /getting-started/<id>.html, and the language picker of the home and getting-started pages
# (views/Shared/_Languages.cshtml). A language whose page cannot be fetched (none yet, or no network) gets a card that
# links to its README, so the site always builds. .NET's guide is this site's own (taxonomy/getting-started/dotnet.yml).
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$languages = (Get-Content (Join-Path $here "languages.json") -Raw -Encoding utf8 | ConvertFrom-Json).languages
$langContentDir = Join-Path $siteDir "content" "GettingStarted" "Languages"
$gsTaxonomyDir = Join-Path $siteDir "taxonomy" "getting-started"
New-Item -ItemType Directory -Force -Path $langContentDir | Out-Null
Get-ChildItem $langContentDir -Filter "*.md" -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem $gsTaxonomyDir -Filter "*.yml" -ErrorAction SilentlyContinue | Where-Object { $_.Name -notin @("index.yml", "dotnet.yml") } | Remove-Item -Force
$langHref = @{}
$rank = 1
foreach ($lang in $languages) {
    if ($lang.local) { $langHref[$lang.id] = "/getting-started/$($lang.id).html"; continue }
    $readme = "https://github.com/$($lang.repo)"
    $langHref[$lang.id] = $readme
    $tag = $null
    $raw = $null
    try {
        $tag = (& gh api "repos/$($lang.repo)/releases/latest" --jq .tag_name 2>$null)
        if ($LASTEXITCODE -eq 0 -and $tag) {
            $raw = (& gh api "repos/$($lang.repo)/contents/$($lang.page)?ref=$tag" -H "Accept: application/vnd.github.raw" 2>$null) -join "`n"
            if ($LASTEXITCODE -ne 0) { $raw = $null }
        }
    } catch { $raw = $null }
    if (!$raw) { Write-Host "  $($lang.name): no page at its latest release - the card links its README" -ForegroundColor DarkYellow; continue }
    if ($raw -match '^# (.+?)[\r\n]') { $title = $Matches[1].Trim() -replace '`', '' } else { $title = "Getting started with $($lang.name)" }
    $body = ($raw -replace '^#[^\n]+\n\s*', '').TrimStart()
    # A link relative to the page in its repository goes to the file in that repository at the same release.
    $pageDir = [System.IO.Path]::GetDirectoryName($lang.page) -replace '\\', '/'
    $base = "https://github.com/$($lang.repo)/blob/$tag/"
    $body = [regex]::Replace($body, '\]\((?!https?://|#|mailto:)([^)]+)\)', {
        param($m)
        $target = [System.Uri]::new([System.Uri]::new("https://x/$pageDir/"), $m.Groups[1].Value).AbsolutePath.TrimStart('/')
        "](${base}$target)"
    })
    $titleYaml = $title -replace '"', '\"'
    $summary = $lang.summary -replace '"', '\"'
    $frontmatter = "---`nContentType: `"application/vnd.endjin.ssg.content+md`"`nPublicationStatus: Published`nDate: 2026-10-29T00:00:00.0+00:00`nTitle: `"$titleYaml`"`n---`n"
    [System.IO.File]::WriteAllText((Join-Path $langContentDir "$($lang.id).md"), ($frontmatter + $body), [System.Text.Encoding]::UTF8)
    $yml = @"
ContentType: application/vnd.endjin.ssg.page+yaml
Title: "$titleYaml"
Template: getting-started/language
Navigation:
  Title: "$($lang.name)"
  Description: "$summary"
  Parent: /getting-started/index.html
  Url: /getting-started/$($lang.id).html
  Rank: $rank
  Header:
    Visible: False
    Link: False
  Footer:
    Visible: False
    Link: False
MetaData:
  Title: "$titleYaml - Corvus.Regex.Ecma262"
  Description: "$summary"
  Keywords: [getting started, regex, ECMA-262, $($lang.name)]
OpenGraph:
  Title: "$titleYaml - Corvus.Regex.Ecma262"
  Description: "$summary"
  Image:
ContentBlocks:
  - ContentType: application/vnd.endjin.ssg.content+md
    Id: $($lang.id)
    Spec:
      Path: ../../content/GettingStarted/Languages/$($lang.id).md
"@
    [System.IO.File]::WriteAllText((Join-Path $gsTaxonomyDir "$($lang.id).yml"), $yml, [System.Text.Encoding]::UTF8)
    $langHref[$lang.id] = "/getting-started/$($lang.id).html"
    $rank++
    Write-Host "  $($lang.name) -> /getting-started/$($lang.id).html (at $tag)" -ForegroundColor Gray
}
# The picker: a card for each language, its package and how to install it. Razor reads '@' as code, so it is doubled.
$enc = { param($t) ([System.Net.WebUtility]::HtmlEncode([string]$t)) -replace '@', '@@' }
$cards = foreach ($lang in $languages) {
    $href = $langHref[$lang.id]
    $external = if ($href.StartsWith("https://")) { ' target="_blank" rel="noopener noreferrer"' } else { '' }
    $note = if ($href.StartsWith("https://")) { '<span class="lang-card__note">README &#8599;</span>' } else { '' }
    @"
    <a class="lang-card" href="$(& $enc $href)"$external>
        <span class="lang-card__name">$(& $enc $lang.name)$note</span>
        <span class="lang-card__summary">$(& $enc $lang.summary)</span>
        <code class="lang-card__install">$(& $enc $lang.install)</code>
    </a>
"@
}
$picker = "@* Generated by build.ps1 from languages.json. *@`n<div class=`"lang-grid`">`n$($cards -join "`n")`n</div>`n"
[System.IO.File]::WriteAllText((Join-Path $siteDir "theme" "corvus" "views" "Shared" "_Languages.cshtml"), $picker, [System.Text.Encoding]::UTF8)
Write-StepDuration "Languages ($($rank - 1) pages fetched)" $sw

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
