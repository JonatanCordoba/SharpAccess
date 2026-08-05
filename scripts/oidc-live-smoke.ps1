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

# Reads the current commit without making Git availability a smoke-test dependency.
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

# Writes only bounded redacted evidence; no credentials, codes, tokens, nonce, account data, or endpoints are serialized.
function Write-Evidence([string]$Path, [string]$Status, [string]$ConfigurationName, [string]$Commit) {
    $record = [ordered]@{
        schemaVersion = 1
        control = "oidc-real-provider-smoke"
        provider = "protected-environment-provider"
        mode = "manual-protected-authorization-code-pkce"
        evidence = "redacted-no-credentials-codes-tokens-nonce-account-data-or-endpoints"
        status = $Status
        configuration = $ConfigurationName
        completedUtc = [DateTimeOffset]::UtcNow.ToString("O")
        commit = $Commit
    }
    $record | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $Path -Encoding utf8
}

$required = @(
    "SHARPACCESS_OIDC_LIVE_PROVIDER",
    "SHARPACCESS_OIDC_LIVE_CLIENT_ID",
    "SHARPACCESS_OIDC_LIVE_CLIENT_SECRET",
    "SHARPACCESS_OIDC_LIVE_CLIENT_AUTHENTICATION_METHOD",
    "SHARPACCESS_OIDC_LIVE_BASE_URI",
    "SHARPACCESS_OIDC_LIVE_CALLBACK_PATH",
    "SHARPACCESS_OIDC_LIVE_AUTHORIZATION_ENDPOINT",
    "SHARPACCESS_OIDC_LIVE_TOKEN_ENDPOINT",
    "SHARPACCESS_OIDC_LIVE_JWKS_ENDPOINT",
    "SHARPACCESS_OIDC_LIVE_VALID_ISSUERS",
    "SHARPACCESS_OIDC_LIVE_SIGNING_ALGORITHMS",
    "SHARPACCESS_OIDC_LIVE_ALLOWED_HOSTS",
    "SHARPACCESS_OIDC_LIVE_AUTHORIZATION_CODE",
    "SHARPACCESS_OIDC_LIVE_CODE_VERIFIER",
    "SHARPACCESS_OIDC_LIVE_NONCE"
)

$root = Resolve-RepositoryRoot $RepositoryRoot
$project = Join-Path $root "tests/SharpAccess.IntegrationTests/SharpAccess.IntegrationTests.csproj"
$artifacts = Join-Path $root "artifacts/operations/oidc-live-smoke"
$evidencePath = Join-Path $artifacts "oidc-live-smoke.json"
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
$commit = Get-CurrentCommit $root

try {
    $missing = @(
        $required | Where-Object { [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) }
    )
    if ($missing.Count -gt 0) {
        throw "Protected live OIDC settings are incomplete. Missing variable names: $($missing -join ', ')."
    }

    if (-not $NoRestore) {
        Invoke-DotNet @("restore", $project, "--locked-mode") "Live OIDC smoke restore failed."
    }

    $arguments = @(
        "test",
        $project,
        "--configuration",
        $Configuration,
        "--filter",
        "Category=OidcLive",
        "--logger",
        "console;verbosity=minimal",
        "--no-restore"
    )
    if ($NoBuild) {
        $arguments += "--no-build"
    }

    Invoke-DotNet $arguments "Live OIDC smoke failed."
    Write-Evidence $evidencePath "passed" $Configuration $commit
    Write-Host "Live OIDC smoke passed; redacted evidence was written to artifacts/operations/oidc-live-smoke."
}
catch {
    Write-Evidence $evidencePath "failed" $Configuration $commit
    throw
}
finally {
    $global:LASTEXITCODE = 0
}
