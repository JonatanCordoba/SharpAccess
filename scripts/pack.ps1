#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$RepositoryUrl,
    [switch]$SkipSetupTest
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Import-Module (Join-Path $PSScriptRoot "SharpAccess.Build/SharpAccess.Build.psd1") -Force

# Rejects stable package generation unless the protected canonical-repository override is present.
function Assert-ReleaseSafePackageVersion([string]$PackageVersion) {
    $publicVersion = $PackageVersion.Split('+', 2)[0]
    if ($publicVersion.Contains('-', [StringComparison]::Ordinal)) { return }
    if ($env:SHARPACCESS_STABLE_RELEASE -ne "true") {
        throw "Stable package version $PackageVersion requires SHARPACCESS_STABLE_RELEASE=true. Development packages must use a prerelease version."
    }
    if ($env:GITHUB_REPOSITORY -cne "JonatanCordoba/SharpAccess") {
        throw "Stable package version $PackageVersion is forbidden outside JonatanCordoba/SharpAccess. Current repository identity: $($env:GITHUB_REPOSITORY)"
    }
}

# Resolves the HTTPS repository URL used in package metadata.
function Resolve-RepositoryUrl([string]$Candidate, [string]$Root) {
    if ([string]::IsNullOrWhiteSpace($Candidate)) { $Candidate = $env:SHARPACCESS_REPOSITORY_URL }
    if ([string]::IsNullOrWhiteSpace($Candidate) -and -not [string]::IsNullOrWhiteSpace($env:GITHUB_REPOSITORY)) {
        $server = if ([string]::IsNullOrWhiteSpace($env:GITHUB_SERVER_URL)) { "https://github.com" } else { $env:GITHUB_SERVER_URL.TrimEnd('/') }
        $Candidate = "$server/$($env:GITHUB_REPOSITORY)"
    }
    if ([string]::IsNullOrWhiteSpace($Candidate) -and (Get-Command git -ErrorAction SilentlyContinue)) {
        $Candidate = (& git -C $Root config --get remote.origin.url 2>$null)
        if ($LASTEXITCODE -ne 0) { $Candidate = $null }
    }
    if (-not [string]::IsNullOrWhiteSpace($Candidate)) {
        $Candidate = $Candidate.Trim()
        if ($Candidate.EndsWith('.git', [StringComparison]::OrdinalIgnoreCase)) { $Candidate = $Candidate.Substring(0, $Candidate.Length - 4) }
        if ($Candidate -match '^git@github\.com:(.+)$') { $Candidate = "https://github.com/$($Matches[1])" }
    }
    $parsed = $null
    if ([string]::IsNullOrWhiteSpace($Candidate) -or -not [Uri]::TryCreate($Candidate, [UriKind]::Absolute, [ref]$parsed) -or $parsed.Scheme -ne [Uri]::UriSchemeHttps) {
        throw "A real HTTPS repository URL is required through -RepositoryUrl, SHARPACCESS_REPOSITORY_URL, GitHub Actions, or the Git origin remote."
    }
    return $parsed.AbsoluteUri.TrimEnd('/')
}

# Verifies one exact runtime package and its symbol package.
function Test-Package([string]$PackagesPath, [string]$PackageId, [string]$PackageVersion) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $runtimePath = Join-Path $PackagesPath "$PackageId.$PackageVersion.nupkg"
    $symbolsPath = Join-Path $PackagesPath "$PackageId.$PackageVersion.snupkg"
    if (-not (Test-Path -LiteralPath $runtimePath -PathType Leaf) -or -not (Test-Path -LiteralPath $symbolsPath -PathType Leaf)) {
        throw "Missing exact package artifacts for $PackageId $PackageVersion."
    }

    $runtime = [IO.Compression.ZipFile]::OpenRead($runtimePath)
    try {
        $entries = @($runtime.Entries | ForEach-Object FullName)
        $hasNuspec = $entries -contains "$PackageId.nuspec"
        $hasReadme = $entries -contains "README.md"
        $hasAssembly = @(
            $entries |
                Where-Object { $_ -match '^lib/net10\.0/[^/]+\.dll$' }
        ).Count -gt 0
        $hasDocumentation = @(
            $entries |
                Where-Object { $_ -match '^lib/net10\.0/[^/]+\.xml$' }
        ).Count -gt 0

        if (-not ($hasNuspec -and $hasReadme -and $hasAssembly -and $hasDocumentation)) {
            throw "Runtime package content validation failed for $PackageId."
        }
    }
    finally { $runtime.Dispose() }

    $symbols = [IO.Compression.ZipFile]::OpenRead($symbolsPath)
    try {
        if (-not ($symbols.Entries | Where-Object FullName -Match '\.pdb$')) {
            throw "Symbol package content validation failed for $PackageId."
        }
    }
    finally { $symbols.Dispose() }
}

$root = Resolve-SharpAccessRepositoryRoot $RepositoryRoot
. (Join-Path $root "scripts/provider-status.ps1")
$resolvedRepositoryUrl = Resolve-RepositoryUrl $RepositoryUrl $root
$supportedPackages = @(Get-SharpAccessSupportedPackageCatalog -RepositoryRoot $root)
if ($supportedPackages.Count -eq 0) { throw "The provider status manifest contains no supported packages." }
$packageVersion = Get-SharpAccessVersion -RepositoryRoot $root
Assert-ReleaseSafePackageVersion $packageVersion
$releaseProperties = @()
if ($env:SHARPACCESS_STABLE_RELEASE -eq "true") {
    $releaseProperties += "-p:SharpAccessStableRelease=true"
}

Set-Location -LiteralPath $root
if (-not $SkipSetupTest) {
    & (Join-Path $root "scripts/setup-test.ps1") -RepositoryRoot $root
}
$packages = Join-Path $root "artifacts/packages"
Remove-Item -LiteralPath $packages -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $packages | Out-Null
$metadata = @("-p:RepositoryUrl=$resolvedRepositoryUrl", "-p:PackageProjectUrl=$resolvedRepositoryUrl")

foreach ($package in $supportedPackages) {
    Invoke-SharpAccessDotNet -Arguments (@("pack", $package.ProjectPath, "--configuration", "Release", "--no-build", "--output", $packages) + $metadata + $releaseProperties) `
        -FailureMessage "Package creation failed for $($package.PackageId)."
}

foreach ($package in $supportedPackages) {
    Test-Package -PackagesPath $packages -PackageId $package.PackageId -PackageVersion $packageVersion
}
