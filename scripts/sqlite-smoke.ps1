#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$Configuration = "Release",
    [switch]$NoBuild
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Join-Path $PSScriptRoot ".."
}

$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$project = Join-Path $root "tests/SharpAccess.ProviderContractTests/SharpAccess.ProviderContractTests.csproj"
if (-not (Test-Path -LiteralPath (Join-Path $root "SharpAccess.sln") -PathType Leaf) -or
    -not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "Repository root is invalid: $root"
}

$arguments = @(
    "test",
    $project,
    "--configuration",
    $Configuration,
    "--filter",
    "Sqlite"
)

if ($NoBuild) {
    $arguments += "--no-build"
}

Write-Host "Running SQLite provider validation."
& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "SQLite provider validation failed."
}
