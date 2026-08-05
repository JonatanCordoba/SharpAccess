#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet("migrate", "validate", "status", "script")][string]$Command,
    [Parameter(Mandatory)][string]$ConnectionString,
    [string]$OutputPath,
    [string]$RepositoryRoot
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Resolves the repository root independently of the caller working directory.
function Resolve-RepositoryRoot([string]$Candidate) {
    if ([string]::IsNullOrWhiteSpace($Candidate)) { $Candidate = Join-Path $PSScriptRoot ".." }
    $resolved = (Resolve-Path -LiteralPath $Candidate).Path
    if (-not (Test-Path -LiteralPath (Join-Path $resolved "SharpAccess.sln") -PathType Leaf)) { throw "Repository root is invalid." }
    return $resolved
}

$root = Resolve-RepositoryRoot $RepositoryRoot
$arguments = @("run", "--project", (Join-Path $root "tools/SharpAccess.MigrationTool/SharpAccess.MigrationTool.csproj"), "--", $Command, "--provider", "sqlite", "--connection", $ConnectionString)
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) { $arguments += @("--output", $OutputPath) }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "SharpAccess migration command failed with exit code $LASTEXITCODE." }
