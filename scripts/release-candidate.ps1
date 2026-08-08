#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$RepositoryUrl = "https://github.com/JonatanCordoba/SharpAccess",
    [string]$Version,
    [string]$ReferenceEnvironment = "controlled-windows-runner-01",
    [switch]$RequirePostgres,
    [switch]$RequireOidcLiveEvidence,
    [switch]$UsePrevalidatedOidcLiveEvidence,
    [switch]$RequireApprovedPerformanceBaseline,
    [switch]$ValidateApprovedPerformanceBaselineOnly,
    [switch]$SkipFullLocalGate
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Import-Module (Join-Path $PSScriptRoot "SharpAccess.Build/SharpAccess.Build.psd1") -Force

function Invoke-ReleaseCandidateStage([string]$Name,[string]$Classification,[scriptblock]$Script,[Collections.Generic.List[object]]$Stages) {
    Write-Host ""
    Write-Host "==> $Name"
    & $Script
    $Stages.Add([ordered]@{ name = $Name; classification = $Classification; status = "passed" })
}

function Write-EvidenceIndex(
    [string]$Path,
    [string]$Status,
    [string]$Commit,
    [string]$PackageVersion,
    [string]$EnvironmentName,
    [object[]]$Stages,
    [bool]$PostgresRequired,
    [bool]$OidcRequired,
    [bool]$BaselineRequired) {
    $payload = [ordered]@{
        schemaVersion = 3
        status = $Status
        sourceRevision = $Commit
        packageVersion = $PackageVersion
        repository = "JonatanCordoba/SharpAccess"
        artifactType = "windows-prerelease-release-candidate-evidence"
        referenceEnvironment = $EnvironmentName
        supportedPlatform = "Windows"
        activePackageCohort = @("SharpAccess.Core", "SharpAccess.Sqlite", "SharpAccess.Postgres")
        postgresEvidenceRequired = $PostgresRequired
        oidcLiveEvidenceRequired = $OidcRequired
        approvedPerformanceBaselineRequired = $BaselineRequired
        stages = $Stages
        completedUtc = [DateTimeOffset]::UtcNow.ToString("O")
    }
    Write-SharpAccessUtf8NoBom -Path $Path -Content (($payload | ConvertTo-Json -Depth 8) + "`n")
}

function Assert-ApprovedPerformanceRequest(
    [string]$Root,
    [string]$CurrentRevision,
    [string]$EnvironmentName) {
    $baselinePath = Join-Path $Root "eng/PerformanceBaseline.json"
    $baseline = Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json
    if ([string]$baseline.status -cne "approved") {
        throw "The tracked performance baseline is not approved for release use. Capture and approve a controlled candidate on reachable protected history."
    }
    if ([string]::IsNullOrWhiteSpace([string]$baseline.approvedRevision)) {
        throw "The approved performance baseline does not identify an exact revision."
    }
    if ([string]$baseline.referenceEnvironment -cne $EnvironmentName) {
        throw "The requested controlled environment does not match the approved performance baseline."
    }

    & git -C $Root merge-base --is-ancestor ([string]$baseline.approvedRevision) $CurrentRevision
    if ($LASTEXITCODE -ne 0) {
        throw "The approved performance revision is not an ancestor of the selected release-candidate revision."
    }
}

function Clear-EphemeralOidcEnvironment {
    foreach ($name in @(
        "SHARPACCESS_OIDC_LIVE_AUTHORIZATION_CODE",
        "SHARPACCESS_OIDC_LIVE_CODE_VERIFIER",
        "SHARPACCESS_OIDC_LIVE_NONCE"
    )) {
        Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue
    }
}

function Assert-PrevalidatedOidcLiveEvidence(
    [string]$Root,
    [string]$CurrentRevision) {
    $path = Join-Path $Root "artifacts/operations/oidc-live-smoke/oidc-live-smoke.json"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Prevalidated OIDC live evidence is missing: $path"
    }

    $record = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    $invalid =
        [int]$record.schemaVersion -ne 1 -or
        [string]$record.control -cne "oidc-real-provider-smoke" -or
        [string]$record.provider -cne "protected-environment-provider" -or
        [string]$record.mode -cne "manual-protected-authorization-code-pkce" -or
        [string]$record.status -cne "passed" -or
        [string]$record.configuration -cne "Release" -or
        [string]$record.commit -cne $CurrentRevision

    if ($invalid) {
        throw "Prevalidated OIDC live evidence does not belong to the selected exact Release revision."
    }

    Write-Host "Prevalidated OIDC live evidence accepted for $CurrentRevision."
}

if ($UsePrevalidatedOidcLiveEvidence -and -not $RequireOidcLiveEvidence) {
    throw "-UsePrevalidatedOidcLiveEvidence requires -RequireOidcLiveEvidence."
}

if ($ValidateApprovedPerformanceBaselineOnly -and -not $RequireApprovedPerformanceBaseline) {
    throw "-ValidateApprovedPerformanceBaselineOnly requires -RequireApprovedPerformanceBaseline."
}

if (-not [OperatingSystem]::IsWindows()) { throw "SharpAccess release-candidate evidence is supported on Windows only." }
$root = Resolve-SharpAccessRepositoryRoot $RepositoryRoot
$authoritativeVersion = Get-SharpAccessVersion -RepositoryRoot $root
$requestedVersion = if ([string]::IsNullOrWhiteSpace($Version)) { $authoritativeVersion } else { $Version.Trim() }
if ($requestedVersion -ne $authoritativeVersion) {
    throw "Requested release-candidate version $requestedVersion does not match authoritative version $authoritativeVersion."
}

$requiredFiles = @(
    "eng/Version.props", "eng/ReleaseCandidate.props", "eng/PerformanceBaseline.json", "docs/CAPACITY-PLANNING.md",
    "docs/RELEASE-CANDIDATE.md", "docs/RELEASE-EVIDENCE-MATRIX.md",
    "scripts/performance-evidence.ps1", "scripts/export-dry-run.ps1", "scripts/checksums.ps1")
foreach ($relativePath in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $relativePath) -PathType Leaf)) { throw "Release-candidate control is missing: $relativePath" }
}
$commit = Get-SharpAccessRevision -RepositoryRoot $root
if ($RequireApprovedPerformanceBaseline) {
    Assert-ApprovedPerformanceRequest $root $commit $ReferenceEnvironment
}
$artifacts = Join-Path $root "artifacts/release-candidate"
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
$evidencePath = Join-Path $artifacts "evidence-index.json"
$stages = [Collections.Generic.List[object]]::new()
$postgresRequired = $true
$completeEvidenceRequested =
    -not [bool]$SkipFullLocalGate -and
    [bool]$RequireOidcLiveEvidence -and
    [bool]$RequireApprovedPerformanceBaseline
try {
    if ($RequireOidcLiveEvidence) {
        if ($UsePrevalidatedOidcLiveEvidence) {
            Invoke-ReleaseCandidateStage "Protected OIDC live smoke" "release-candidate-required" {
                Assert-PrevalidatedOidcLiveEvidence $root $commit
            } $stages
        }
        else {
            try {
                Invoke-ReleaseCandidateStage "Protected OIDC live smoke" "release-candidate-required" {
                    & (Join-Path $root "scripts/oidc-live-smoke.ps1") `
                        -RepositoryRoot $root `
                        -Configuration "Release"
                } $stages
            }
            finally {
                Clear-EphemeralOidcEnvironment
            }
        }
    }
    else { $stages.Add([ordered]@{ name = "Protected OIDC live smoke"; classification = "release-candidate-required"; status = "not-run-by-request" }) }

    if (-not $SkipFullLocalGate) {
        Invoke-ReleaseCandidateStage "Complete Windows clean-tree local gate" "release-candidate-required" {
            & (Join-Path $root "scripts/release-dry-run.ps1") `
                -RepositoryRoot $root `
                -RepositoryUrl $RepositoryUrl `
                -Version $requestedVersion
        } $stages
    }
    else { $stages.Add([ordered]@{ name = "Complete Windows clean-tree local gate"; classification = "release-candidate-required"; status = "not-run-by-request" }) }

    Invoke-ReleaseCandidateStage "Performance and capacity evidence" "release-candidate-required" {
        $arguments = @{ RepositoryRoot = $root; Configuration = "Release"; ReferenceEnvironment = $ReferenceEnvironment }
        if (-not $SkipFullLocalGate) { $arguments.NoRestore = $true; $arguments.NoBuild = $true }
        if ($RequireApprovedPerformanceBaseline) { $arguments.RequireApprovedBaseline = $true }
        if ($ValidateApprovedPerformanceBaselineOnly) { $arguments.ValidateApprovedBaselineOnly = $true }
        & (Join-Path $root "scripts/performance-evidence.ps1") @arguments
    } $stages

    Invoke-ReleaseCandidateStage "PostgreSQL real-engine provider contracts" "release-candidate-required" {
        $arguments = @{ RepositoryRoot = $root; Configuration = "Release"; RequireConfigured = $true }
        if (-not $SkipFullLocalGate) { $arguments.NoBuild = $true }
        & (Join-Path $root "scripts/provider-contracts.ps1") @arguments
    } $stages

    Invoke-ReleaseCandidateStage "PostgreSQL supported-provider coverage" "release-candidate-required" {
        $arguments = @{ RepositoryRoot = $root; Provider = "Postgres"; Configuration = "Release"; PromotionGate = $true }
        if (-not $SkipFullLocalGate) { $arguments.NoBuild = $true }
        & (Join-Path $root "scripts/provider-coverage.ps1") @arguments
    } $stages

    Invoke-ReleaseCandidateStage "PostgreSQL native recovery drill" "release-candidate-required" {
        $arguments = @{ RepositoryRoot = $root; Configuration = "Release" }
        if (-not $SkipFullLocalGate) { $arguments.NoRestore = $true; $arguments.NoBuild = $true }
        & (Join-Path $root "scripts/postgres-recovery-drill.ps1") @arguments
    } $stages


    Invoke-ReleaseCandidateStage "Deterministic tracked-file export dry run" "release-candidate-required" {
        & (Join-Path $root "scripts/export-dry-run.ps1") -RepositoryRoot $root -Revision $commit
    } $stages

    $evidenceStatus = if ($completeEvidenceRequested) { "passed" } else { "incomplete" }
    Write-EvidenceIndex $evidencePath $evidenceStatus $commit $requestedVersion $ReferenceEnvironment $stages.ToArray() $postgresRequired ([bool]$RequireOidcLiveEvidence) ([bool]$RequireApprovedPerformanceBaseline)
    & (Join-Path $root "scripts/checksums.ps1") -RepositoryRoot $root
    Write-Host ""
    if ($completeEvidenceRequested) {
        Write-Host "Integrated Windows release-candidate evidence completed for $commit at version $requestedVersion."
    }
    else {
        Write-Warning "Exploratory release-candidate orchestration completed with incomplete protected evidence for $commit."
    }
}
catch {
    $stages.Add([ordered]@{ name = "release-candidate-orchestration"; classification = "release-candidate-required"; status = "failed"; errorType = $_.Exception.GetType().Name })
    Write-EvidenceIndex $evidencePath "failed" $commit $requestedVersion $ReferenceEnvironment $stages.ToArray() $postgresRequired ([bool]$RequireOidcLiveEvidence) ([bool]$RequireApprovedPerformanceBaseline)
    throw
}
finally { $global:LASTEXITCODE = 0 }
