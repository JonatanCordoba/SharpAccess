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

# Reads the current commit without making Git availability a recovery-test dependency.
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
$project = Join-Path $root "tests/SharpAccess.IntegrationTests/SharpAccess.IntegrationTests.csproj"
$artifacts = Join-Path $root "artifacts/operations/recovery-drill"
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

if (-not $NoRestore) {
    Invoke-DotNet @("restore", $project, "--locked-mode") "Recovery drill restore failed."
}

$arguments = @(
    "test",
    $project,
    "--configuration",
    $Configuration,
    "--filter",
    "FullyQualifiedName=SharpAccess.IntegrationTests.SqliteRecoveryDrillTests.OfflineFileBackupRestoresVerifiedAccountAndLogin",
    "--logger",
    "trx;LogFileName=recovery-drill.trx",
    "--results-directory",
    $artifacts
)
$arguments += "--no-restore"
if ($NoBuild) {
    $arguments += "--no-build"
}

Invoke-DotNet $arguments "SQLite recovery drill failed."

$record = [ordered]@{
    schemaVersion = 2
    control = "sqlite-offline-backup-restore"
    provider = "SharpAccess.Sqlite"
    mode = "checkpointed-controlled-offline-file-copy"
    journalMode = "wal"
    checkpoint = "truncate-before-copy"
    integrityCheck = "pre-copy-and-post-restore"
    pooling = "disabled-for-drill-and-cleared-before-cleanup"
    status = "passed"
    configuration = $Configuration
    completedUtc = [DateTimeOffset]::UtcNow.ToString("O")
    commit = Get-CurrentCommit $root
}
$record |
    ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath (Join-Path $artifacts "recovery-drill.json") -Encoding utf8

Write-Host "SQLite recovery drill passed and evidence was written to artifacts/operations/recovery-drill."
$global:LASTEXITCODE = 0
