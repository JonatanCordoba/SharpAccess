#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$NoRestore,
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Resolves and validates the repository root independently of the caller's working directory.
function Resolve-RepositoryRoot([string]$Candidate) {
    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        $Candidate = Join-Path $PSScriptRoot ".."
    }

    $resolved = (Resolve-Path -LiteralPath $Candidate).Path
    if (-not (Test-Path -LiteralPath (Join-Path $resolved "SharpAccess.sln") -PathType Leaf)) {
        throw "Repository root is invalid: $resolved"
    }

    return $resolved
}

# Runs one dotnet command and converts its exit code into a terminating failure.
function Invoke-DotNet([string[]]$Arguments, [string]$FailureMessage) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
}

# Requires one repository-relative operational control file.
function Assert-ControlFile([string]$Root, [string]$RelativePath) {
    if (-not (Test-Path -LiteralPath (Join-Path $Root $RelativePath) -PathType Leaf)) {
        throw "Operational readiness file is missing: $RelativePath"
    }
}

# Reads the current commit without making Git availability an evidence-generation dependency.
function Get-CurrentCommit([string]$Root) {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        return "unknown"
    }

    $commit = (& git -C $Root rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commit)) {
        return "unknown"
    }

    return $commit.Trim()
}

$root = Resolve-RepositoryRoot $RepositoryRoot
$requiredFiles = @(
    "eng/OperationalReadiness.props",
    "docs/OPERATIONS.md",
    "docs/OBSERVABILITY.md",
    "docs/INCIDENT-RESPONSE.md",
    "docs/BUSINESS-CONTINUITY.md",
    "docs/PRIVACY.md",
    "docs/CHANGE-MANAGEMENT.md",
    "docs/QUALITY-OBJECTIVES.md",
    "docs/RELEASE-CHECKLIST.md",
    "docs/templates/POSTMORTEM.md",
    "docs/templates/RISK-ACCEPTANCE.md",
    "docs/templates/CHANGE-RECORD.md",
    "docs/templates/RECOVERY-DRILL.md",
    ".github/workflows/operational-readiness.yml",
    "scripts/recovery-drill.ps1"
)
foreach ($file in $requiredFiles) {
    Assert-ControlFile $root $file
}

& (Join-Path $root "scripts/verify-action-pins.ps1") -RepositoryRoot $root

if (-not $NoRestore) {
    Invoke-DotNet @("restore", (Join-Path $root "SharpAccess.sln"), "--locked-mode") `
        "Operational readiness restore failed."
}

$artifacts = Join-Path $root "artifacts/operations"
$diagnosticsArtifacts = Join-Path $artifacts "diagnostics"
New-Item -ItemType Directory -Force -Path $diagnosticsArtifacts | Out-Null

$unitArguments = @(
    "test",
    (Join-Path $root "tests/SharpAccess.UnitTests/SharpAccess.UnitTests.csproj"),
    "--configuration",
    $Configuration,
    "--filter",
    "FullyQualifiedName~SharpAccess.UnitTests.DiagnosticsTests",
    "--logger",
    "trx;LogFileName=diagnostics.trx",
    "--results-directory",
    $diagnosticsArtifacts,
    "--no-restore"
)
if ($NoBuild) {
    $unitArguments += "--no-build"
}
Invoke-DotNet $unitArguments "Diagnostics verification failed."

$recoveryArguments = @{
    RepositoryRoot = $root
    Configuration = $Configuration
    NoRestore = $true
}
if ($NoBuild) {
    $recoveryArguments["NoBuild"] = $true
}
& (Join-Path $root "scripts/recovery-drill.ps1") @recoveryArguments

[xml]$targets = Get-Content -LiteralPath (Join-Path $root "eng/OperationalReadiness.props") -Raw
$properties = $targets.Project.PropertyGroup
$dotnetVersion = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "Unable to read the .NET SDK version."
}

$record = [ordered]@{
    schemaVersion = 1
    controlSet = "SharpAccess Phase 4 operational readiness"
    status = "passed"
    configuration = $Configuration
    completedUtc = [DateTimeOffset]::UtcNow.ToString("O")
    commit = Get-CurrentCommit $root
    dotnetVersion = $dotnetVersion
    recoveryDrillFrequencyDays = [int]$properties.RecoveryDrillFrequencyDays
    incidentExerciseFrequencyDays = [int]$properties.IncidentExerciseFrequencyDays
    evidenceRetentionDays = [int]$properties.OperationalEvidenceRetentionDays
    riskExceptionMaximumDays = [int]$properties.RiskExceptionMaximumDays
    controls = @(
        "immutable-action-pins",
        "safe-authentication-telemetry",
        "diagnostics-tests",
        "sqlite-offline-recovery-drill",
        "incident-response-procedure",
        "continuity-procedure",
        "privacy-responsibility-matrix",
        "change-and-risk-records"
    )
}
$record |
    ConvertTo-Json -Depth 6 |
    Set-Content -LiteralPath (Join-Path $artifacts "operational-readiness.json") -Encoding utf8

Write-Host "Operational readiness verification passed and evidence was written to artifacts/operations."
$global:LASTEXITCODE = 0
