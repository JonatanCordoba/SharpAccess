#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$Database,
    [string]$TestEmail = $(if ($env:AUTH_TEST_ADMIN_EMAIL) { $env:AUTH_TEST_ADMIN_EMAIL } else { "admin@test.local" }),
    [string]$TestPassword = $(if ($env:AUTH_TEST_ADMIN_PASSWORD) { $env:AUTH_TEST_ADMIN_PASSWORD } else { "Admin123!Sample" })
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Resolves and validates the repository root independently of the caller's working directory.
function Resolve-RepositoryRoot([string]$Candidate) {
    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        $Candidate = $PSScriptRoot
        if (-not (Test-Path -LiteralPath (Join-Path $Candidate "SharpAccess.sln") -PathType Leaf)) {
            $Candidate = Join-Path $PSScriptRoot ".."
        }
    }
    $resolved = (Resolve-Path -LiteralPath $Candidate).Path
    if (-not (Test-Path -LiteralPath (Join-Path $resolved "SharpAccess.sln") -PathType Leaf) -or
        -not (Test-Path -LiteralPath (Join-Path $resolved "tools/SharpAccess.TestBootstrap/SharpAccess.TestBootstrap.csproj") -PathType Leaf)) {
        throw "Repository root is invalid."
    }
    return $resolved
}

$root = Resolve-RepositoryRoot $RepositoryRoot
if ([string]::IsNullOrWhiteSpace($Database)) { $Database = Join-Path $root "artifacts/test-auth.db" }
if ([string]::IsNullOrWhiteSpace($TestEmail) -or [string]::IsNullOrWhiteSpace($TestPassword)) {
    throw "Test credentials must be non-empty."
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Database) | Out-Null
@($Database, "$Database-shm", "$Database-wal") | ForEach-Object {
    Remove-Item -LiteralPath $_ -Force -ErrorAction SilentlyContinue
}
$bootstrapProject = Join-Path $root "tools/SharpAccess.TestBootstrap/SharpAccess.TestBootstrap.csproj"
$dotnetArguments = @(
    "run",
    "--project", $bootstrapProject,
    "--configuration", "Release",
    "--",
    "--database", $Database,
    "--email", $TestEmail,
    "--password", $TestPassword
)
& dotnet @dotnetArguments
if ($LASTEXITCODE -ne 0) { throw "Database bootstrap failed with exit code $LASTEXITCODE." }

