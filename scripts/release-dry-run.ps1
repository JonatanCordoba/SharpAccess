#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$RepositoryUrl = "https://github.com/JonatanCordoba/SharpAccess",
    [string]$Version
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Import-Module (Join-Path $PSScriptRoot "SharpAccess.Build/SharpAccess.Build.psd1") -Force

# Rejects stable package evidence unless it runs under the protected canonical repository identity.
function Assert-ReleaseSafePackageVersion([string]$PackageVersion) {
    $publicVersion = $PackageVersion.Split('+', 2)[0]
    if ($publicVersion.Contains('-', [StringComparison]::Ordinal)) { return }
    if ($env:SHARPACCESS_STABLE_RELEASE -ne "true") {
        throw "Stable package version $PackageVersion requires SHARPACCESS_STABLE_RELEASE=true. Release-candidate evidence must remain prerelease."
    }
    if ($env:GITHUB_REPOSITORY -cne "JonatanCordoba/SharpAccess") {
        throw "Stable package version $PackageVersion is forbidden outside JonatanCordoba/SharpAccess. Current repository identity: $($env:GITHUB_REPOSITORY)"
    }
}

# Fails release validation unless the complete work tree and every relevant ignored input are clean and Git is idle.
function Assert-CleanSupplyChainGitState([string]$Root) {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw "Git is required for revision-bound release evidence."
    }
    $topLevel = (& git -C $Root rev-parse --show-toplevel 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($topLevel)) {
        throw "Release dry run requires a Git work tree."
    }
    $resolvedTopLevel = (Resolve-Path -LiteralPath $topLevel.Trim()).Path
    $comparison = if ([OperatingSystem]::IsWindows()) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if (-not [string]::Equals($resolvedTopLevel, $Root, $comparison)) {
        throw "Release dry run requires the selected repository root to be the exact Git work-tree root."
    }

    foreach ($stateName in @("rebase-merge", "rebase-apply", "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "BISECT_LOG", "sequencer")) {
        $statePath = (& git -C $Root rev-parse --git-path $stateName 2>$null)
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($statePath)) { throw "The Git operation state could not be inspected." }
        $statePath = $statePath.Trim()
        if (-not [IO.Path]::IsPathRooted($statePath)) { $statePath = [IO.Path]::GetFullPath((Join-Path $Root $statePath)) }
        if (Test-Path -LiteralPath $statePath) { throw "Release dry run refuses an in-progress Git operation: $stateName." }
    }

    $unmerged = @(& git -C $Root ls-files --unmerged)
    if ($LASTEXITCODE -ne 0) { throw "The Git index conflict state could not be inspected." }
    if ($unmerged.Count -ne 0) { throw "Release dry run refuses an unmerged Git index:`n$($unmerged -join "`n")" }

    $worktreeStatus = @(& git -C $Root status --porcelain=v1 --untracked-files=all --ignore-submodules=none)
    if ($LASTEXITCODE -ne 0) { throw "The complete Git work-tree state could not be inspected." }
    if ($worktreeStatus.Count -ne 0) {
        $summary = ($worktreeStatus | Select-Object -First 40) -join "`n"
        throw "Release dry run requires the complete nonignored Git work tree, index, and submodules to be clean. Current state:`n$summary"
    }

    [xml]$policy = Get-Content -LiteralPath (Join-Path $Root "eng/SupplyChain.props") -Raw
    $inputPaths = @($policy.SelectNodes("//SupplyChainInput") | ForEach-Object { $_.Include.Trim() } | Sort-Object -Unique)
    if ($inputPaths.Count -eq 0) { throw "The central supply-chain input-path policy is missing." }
    $gitArguments = @("-C", $Root, "status", "--porcelain=v1", "--untracked-files=all", "--ignored=matching", "--ignore-submodules=none", "--") + $inputPaths
    $statusLines = @(& git @gitArguments)
    if ($LASTEXITCODE -ne 0) {
        throw "The relevant Git input state could not be inspected."
    }
    $dirtyInputs = @($statusLines | Where-Object {
        $path = if ($_.Length -gt 3) { $_.Substring(3) } else { $_ }
        $normalized = $path.Replace('\', '/')
        -not ($normalized -match '(?i)(^|/)(bin|obj)(/|$)')
    })
    if ($dirtyInputs.Count -ne 0) {
        $summary = ($dirtyInputs | Select-Object -First 20) -join "`n"
        throw "Release dry run requires relevant ignored supply-chain inputs to be clean. Remove or restore the listed inputs before running verify-local. Current state:`n$summary"
    }
}

# Fails when a generated root package props file would shadow the canonical props file.
function Assert-NoGeneratedRootPackageProps([string]$Root) {
    $unexpected = Get-ChildItem -LiteralPath $Root -File |
        Where-Object { $_.Name -ceq "Directory.Packages.Props" } |
        Select-Object -First 1
    if ($null -ne $unexpected) {
        throw "Unexpected generated package props file at repository root: $($unexpected.FullName). Delete this file; the canonical file is Directory.Packages.props."
    }
}

# Verifies that a runtime package and symbol package exist for a supported package.
function Assert-PackageArtifact([string]$PackagesPath, [string]$PackageId, [string]$PackageVersion) {
    $runtime = Join-Path $PackagesPath "$PackageId.$PackageVersion.nupkg"
    $symbols = Join-Path $PackagesPath "$PackageId.$PackageVersion.snupkg"
    if (-not (Test-Path -LiteralPath $runtime -PathType Leaf)) { throw "Missing runtime package: $runtime" }
    if (-not (Test-Path -LiteralPath $symbols -PathType Leaf)) { throw "Missing symbol package: $symbols" }
}

# Verifies that a non-supported package was not emitted by the supported pack path.
function Assert-NoPackageArtifact([string]$PackagesPath, [string]$PackageId) {
    $escapedPackageId = [Regex]::Escape($PackageId)
    $artifacts = @(Get-ChildItem -LiteralPath $PackagesPath -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match "^$escapedPackageId\.[0-9].*\.(nupkg|snupkg)$" })
    if ($artifacts.Count -ne 0) {
        throw "Release output contains a non-supported package: $PackageId"
    }
}

$root = Resolve-SharpAccessRepositoryRoot $RepositoryRoot
. (Join-Path $root "scripts/provider-status.ps1")
Set-Location -LiteralPath $root
$catalog = @(Get-SharpAccessPackageCatalog -RepositoryRoot $root)
$supportedPackages = @($catalog | Where-Object { $_.Status -eq "Supported" })
if ($supportedPackages.Count -eq 0) { throw "The provider status manifest contains no supported packages." }
$supportedPackageIds = @($supportedPackages.PackageId | Sort-Object)

$coreVersion = Get-SharpAccessVersion -RepositoryRoot $root
Assert-ReleaseSafePackageVersion $coreVersion
if (-not [string]::IsNullOrWhiteSpace($Version) -and $coreVersion -ne $Version.Trim()) {
    throw "Authoritative version $coreVersion does not match requested dry-run version $($Version.Trim())."
}

Assert-CleanSupplyChainGitState $root
Assert-NoGeneratedRootPackageProps $root

Write-Host "Release dry run package status:"
foreach ($package in $catalog) {
    Write-Host "  $($package.PackageId) $coreVersion - $($package.Status)"
}

& (Join-Path $root "scripts/local-ci.ps1") -RepositoryRoot $root -RepositoryUrl $RepositoryUrl -SkipSbom -RequirePostgres
if ($LASTEXITCODE -ne 0) { throw "local-ci.ps1 failed with exit code $LASTEXITCODE." }

$publicVersion = $coreVersion.Split('+', 2)[0]
if ($publicVersion.Contains('-', [StringComparison]::Ordinal)) {
    & (Join-Path $root "scripts/sbom.ps1") -RepositoryRoot $root -RepositoryUrl $RepositoryUrl `
        -RequiredPackageArchive $supportedPackageIds -ReplaceExistingOutput
}
else {
    & (Join-Path $root "scripts/sbom.ps1") -RepositoryRoot $root -RepositoryUrl $RepositoryUrl `
        -RequireAllPackageArchives -StablePublication -ReplaceExistingOutput
}
if ($LASTEXITCODE -ne 0) { throw "Formal package-root SBOM generation failed with exit code $LASTEXITCODE." }

Assert-CleanSupplyChainGitState $root
Assert-NoGeneratedRootPackageProps $root
$packages = Join-Path $root "artifacts/packages"
foreach ($package in $catalog) {
    if ($package.Status -eq "Supported") {
        Assert-PackageArtifact $packages $package.PackageId $coreVersion
    }
    else {
        Assert-NoPackageArtifact $packages $package.PackageId
    }
}

Write-Host ""
Write-Host "==> Exact-revision engineering-quality report"
& (Join-Path $root "scripts/quality-report.ps1") -RepositoryRoot $root -RepositoryUrl $RepositoryUrl
if ($LASTEXITCODE -ne 0) { throw "quality-report.ps1 failed with exit code $LASTEXITCODE." }

Write-Host ""
Write-Host "Release dry run completed successfully for supported packages at version $coreVersion."
