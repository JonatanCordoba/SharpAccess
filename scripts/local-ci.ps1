#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$RepositoryUrl = "https://github.com/JonatanCordoba/SharpAccess",
    [ValidateRange(0, 65535)][int]$Port = 0,
    [string]$TestEmail = "admin@test.local",
    [string]$TestPassword = "Admin123!Sample",
    [switch]$SkipSbom,
    [switch]$RequirePostgres
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-RepositoryRoot([string]$Candidate) {
    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        $Candidate = $PSScriptRoot
        if (-not (Test-Path -LiteralPath (Join-Path $Candidate "SharpAccess.sln") -PathType Leaf)) { $Candidate = Join-Path $PSScriptRoot ".." }
    }
    $resolved = (Resolve-Path -LiteralPath $Candidate).Path
    if (-not (Test-Path -LiteralPath (Join-Path $resolved "SharpAccess.sln") -PathType Leaf)) { throw "Repository root is invalid: $resolved" }
    return $resolved
}

function Invoke-LocalCiStage([string]$Name, [scriptblock]$Script) {
    Write-Host ""
    Write-Host "==> $Name"
    & $Script
}

# Selects an isolated loopback port when the caller does not require a fixed one.
function Get-AvailableLoopbackPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

if (-not [OperatingSystem]::IsWindows()) { throw "SharpAccess verification is supported on Windows only." }
$root = Resolve-RepositoryRoot $RepositoryRoot
$env:SHARPACCESS_REPOSITORY_URL = $RepositoryUrl
$selectedPort = if ($Port -eq 0) { Get-AvailableLoopbackPort } else { $Port }
$baseUrl = "http://127.0.0.1:$selectedPort"
$providerContractTests = Join-Path $root "tests/SharpAccess.ProviderContractTests/SharpAccess.ProviderContractTests.csproj"
Invoke-LocalCiStage "Repository structure and provider-status validation" { & (Join-Path $root "scripts/verify-structure.ps1") -RepositoryRoot $root }
Invoke-LocalCiStage "Static application security testing" { & (Join-Path $root "scripts/sast.ps1") -RepositoryRoot $root }
Invoke-LocalCiStage "Build, tests, warnings-as-errors, coverage, and complexity" { & (Join-Path $root "scripts/setup-test.ps1") -RepositoryRoot $root }
if ($RequirePostgres) {
    Invoke-LocalCiStage "PostgreSQL validation and complete supported-provider coverage" {
        & (Join-Path $root "scripts/postgres-quality-coverage.ps1") -RepositoryRoot $root
    }
}
Invoke-LocalCiStage "Operational readiness, diagnostics, and SQLite recovery" {
    & (Join-Path $root "scripts/operational-readiness.ps1") -RepositoryRoot $root -Configuration "Release" -NoRestore -NoBuild
}
Invoke-LocalCiStage "Supported SQLite provider validation" {
    & dotnet test $providerContractTests --configuration "Release" --filter "Provider=Sqlite" --no-build
    if ($LASTEXITCODE -ne 0) { throw "SQLite provider validation failed." }
}
if (-not $RequirePostgres) {
    Invoke-LocalCiStage "PostgreSQL provider validation" {
        & (Join-Path $root "scripts/postgres-smoke.ps1") -RepositoryRoot $root -Configuration "Release" -NoBuild
    }
}
Invoke-LocalCiStage "Critical release mutation evidence" { & (Join-Path $root "scripts/mutation-test.ps1") -RepositoryRoot $root -Tier "Release" }
Invoke-LocalCiStage "Endpoint/API smoke validation" {
    & (Join-Path $root "scripts/check-api.ps1") -RepositoryRoot $root -Port $selectedPort -BaseUrl $baseUrl -TestEmail $TestEmail -TestPassword $TestPassword -StartApi -StopApi
}
Invoke-LocalCiStage "Pack supported NuGet artifacts" {
    & (Join-Path $root "scripts/pack.ps1") -RepositoryRoot $root -RepositoryUrl $RepositoryUrl -SkipSetupTest
}
Invoke-LocalCiStage "Supported package consumer smoke validation" { & (Join-Path $root "scripts/package-smoke.ps1") -RepositoryRoot $root }
if (-not $SkipSbom) {
    Invoke-LocalCiStage "Package inventory and SBOM" { & (Join-Path $root "scripts/sbom.ps1") -RepositoryRoot $root -ReplaceExistingOutput }
}
Write-Host ""
Write-Host "Local CI completed successfully on Windows."
