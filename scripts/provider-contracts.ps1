#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [switch]$NoBuild,
    [switch]$RequireConfigured
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Resolves the repository root and validates the provider-contract project.
function Resolve-RepositoryRoot([string]$Candidate) {
    if ([string]::IsNullOrWhiteSpace($Candidate)) { $Candidate = Join-Path $PSScriptRoot ".." }
    $resolved = (Resolve-Path -LiteralPath $Candidate).Path
    $project = Join-Path $resolved "tests/SharpAccess.ProviderContractTests/SharpAccess.ProviderContractTests.csproj"
    if (-not (Test-Path -LiteralPath (Join-Path $resolved "SharpAccess.sln") -PathType Leaf) -or
        -not (Test-Path -LiteralPath $project -PathType Leaf)) {
        throw "Repository root is invalid: $resolved"
    }
    return $resolved
}

# Invokes dotnet and converts a native nonzero exit code into a terminating failure.
function Invoke-DotNet([string[]]$Arguments, [string]$FailureMessage) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$FailureMessage ExitCode=$LASTEXITCODE" }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "The .NET SDK is required." }
$root = Resolve-RepositoryRoot $RepositoryRoot
$project = Join-Path $root "tests/SharpAccess.ProviderContractTests/SharpAccess.ProviderContractTests.csproj"
$connectionString = $env:SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING
if ([string]::IsNullOrWhiteSpace($connectionString)) {
    if ($RequireConfigured) {
        throw "SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING is required for PostgreSQL provider-contract validation."
    }
    Write-Host "Skipping PostgreSQL provider-contract validation; SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING is not set."
    return
}
if (-not [string]::Equals($env:SHARPACCESS_PROVIDER_TEST_ALLOW_RESET, "true", [StringComparison]::OrdinalIgnoreCase)) {
    throw "SHARPACCESS_PROVIDER_TEST_ALLOW_RESET=true is required because provider tests reset auth tables in a dedicated scratch database."
}
if (-not $NoBuild) {
    Invoke-DotNet @("build", $project, "--configuration", $Configuration, "-warnaserror") `
        "Provider-contract test project build failed."
}
$resultsDirectory = Join-Path $root "artifacts/test-results/provider-contracts/postgres"
New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null
Invoke-DotNet @(
    "test", $project,
    "--configuration", $Configuration,
    "--no-build",
    "--filter", "Provider=Postgres",
    "--logger", "trx;LogFileName=postgres.trx",
    "--results-directory", $resultsDirectory
) "PostgreSQL provider-contract validation failed."
Write-Host "PostgreSQL provider-contract validation passed."
