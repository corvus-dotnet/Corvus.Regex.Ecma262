<#
The build process of Corvus.Regex.Ecma262, on ZeroFailed with its .NET and GitHub extensions, as Corvus.JsonSchema
builds.
#>

$zerofailedExtensions = @(
    @{
        Name = "ZeroFailed.Build.DotNet"
        GitRepository = "https://github.com/zerofailed/ZeroFailed.Build.DotNet"
        GitRef = "main"
    }
    @{
        Name = "ZeroFailed.Build.GitHub"
        GitRepository = "https://github.com/zerofailed/ZeroFailed.Build.GitHub"
        GitRef = "main"
    }
)

# Load the tasks and process
. ZeroFailed.tasks -ZfPath $here/.zf

#
# Build process control options
#
$SkipInit = $false
$SkipVersion = $false
$SkipBuild = $false
$CleanBuild = $Clean
$SkipTest = $false
$SkipTestReport = $false
$SkipAnalysis = $false
$SkipPackage = $false
$SkipPublish = $false

$SolutionToBuild = (Resolve-Path (Join-Path $here "Solutions/Corvus.Regex.Ecma262.slnx")).Path
$ProjectsToPublish = @()
$NugetPublishSource = property ZF_NUGET_PUBLISH_SOURCE "$here/_local-nuget-feed"
# Each test process writes its diagnostic log to its own project's TestResults directory (see Corvus.JsonSchema's
# configuration for why the shared default is not used).
$DotNetTestFileLoggerProps_MTP = @('--diagnostic', '--diagnostic-verbosity', 'Warning')

$IncludeAssembliesInCodeCoverage = @()
$ExcludeAssembliesInCodeCoverage = @()
$ExcludeFilesInCodeCoverage = @('*.g.cs')
$DotNetCoverageSettingsFile = (Resolve-Path (Join-Path $here "dotnet-coverage.settings.xml")).Path

# When running GHA honour the TFM that the matrix build passes via an environment variable
$TargetFrameworkMoniker = property BUILDVAR_TargetFrameworkMoniker ''

# The 'outerloop' category is the full run of the ECMA-262 test data and the differential fuzzer, which have their
# own workflow.
$AdditionalTestArgs = @(
    "--filter", 'TestCategory!=outerloop'
)
$StripOutputFromLargeTrxFiles = $true
$TruncateOversizedCoverageReport = $true
$UseGitHubFlavour = $true

# When running in GHA create a GitHub Release when running a release build
$CreateGitHubRelease = $env:GITHUB_ACTIONS ? $true : $false
$PublishNuGetPackagesAsGitHubReleaseArtefacts = $true

# The documentation website (docs/website), built after the .NET build when asked for, as Corvus.JsonSchema's.
$BuildWebsite = [Convert]::ToBoolean((property BUILDVAR_BuildWebsite $Website.ToBool()))
$IsPreviewDeployment = [Convert]::ToBoolean((property BUILDVAR_IsPreviewDeployment $false))
$BasePathPrefix = property BUILDVAR_BasePathPrefix $BasePathPrefix
$VellumDownloadToken = property VELLUM_DOWNLOAD_TOKEN ''

task . FullBuild

#
# Build Process Extensibility Points
#
task PreTest {
    $script:LogLevelBackup = $LogLevel
    $script:LogLevel = "quiet"
}
task PostTest {
    $script:LogLevel = $LogLevelBackup
}
task PostBuild BuildWebSiteLocal

# Synopsis: Builds the documentation static site (the CI post-compile phase runs this task on its own).
task BuildWebsite {
    $websiteDir = Join-Path $here "docs/website"
    $websiteBuildArgs = @{ SkipDotNetBuild = $true }
    if ($VellumDownloadToken) {
        $websiteBuildArgs += @{ VellumDownloadToken = (ConvertTo-SecureString $VellumDownloadToken -AsPlainText) }
    }

    $basePathPrefix = $env:BUILDVAR_BasePathPrefix
    if (!$basePathPrefix) { $basePathPrefix = $BasePathPrefix }
    if ($basePathPrefix) {
        $websiteBuildArgs += @{ BasePathPrefix = $basePathPrefix }
    }

    if ($env:BUILDVAR_IsPreviewDeployment -ieq "true") {
        $websiteBuildArgs += @{ IsPreviewDeployment = $true }
    }

    Write-Host "Building documentation website..."
    Write-Host "  BasePathPrefix: $basePathPrefix"
    Write-Host "  IsPreviewDeployment: $($env:BUILDVAR_IsPreviewDeployment)"
    & (Join-Path $websiteDir "build.ps1") @websiteBuildArgs
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

# Synopsis: Wrapper task to enable running the standalone BuildWebsite task for local builds
task BuildWebSiteLocal -If { $BuildWebsite } BuildWebsite

# The publish phase takes the packages from the package phase through the Actions cache, which can be evicted
# between the two. A publish with nothing to publish fails here rather than "publishing" nothing.
task PrePublish -If { !$SkipPublish } {
    $packagesPath = if ([IO.Path]::IsPathRooted($PackagesDir)) { $PackagesDir } else { Join-Path $here $PackagesDir }
    $packages = @(Get-ChildItem -Path $packagesPath -Filter *.nupkg -ErrorAction SilentlyContinue)
    if ($packages.Count -eq 0) {
        throw "There are no packages in '$packagesPath' to publish. The package phase's output was not restored (its Actions cache entry is missing). Run the whole workflow again when no other build is running."
    }

    Write-Host "Publishing $($packages.Count) packages from '$packagesPath'"
}
