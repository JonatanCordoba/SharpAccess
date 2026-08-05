#Requires -Version 7.0
[CmdletBinding()]
param([string]$RepositoryRoot, [Parameter(Mandatory)][string]$SearchRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Resolves a coverage directory inside the repository and rejects path escape.
function Resolve-CoverageRoot([string]$Root, [string]$Candidate) {
    $repository = (Resolve-Path -LiteralPath $Root).Path
    $path = if ([IO.Path]::IsPathRooted($Candidate)) { $Candidate } else { Join-Path $repository $Candidate }
    $resolved = (Resolve-Path -LiteralPath $path).Path
    if (-not $resolved.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Coverage path must remain inside the repository." }
    return $resolved
}

$root = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { (Resolve-Path (Join-Path $PSScriptRoot "..")).Path } else { (Resolve-Path $RepositoryRoot).Path }
$search = Resolve-CoverageRoot $root $SearchRoot
$legacyNames = @('coverage.cobertura.xml', 'Cobertura.xml')
foreach ($file in Get-ChildItem -LiteralPath $search -Recurse -File | Where-Object { $legacyNames -ccontains $_.Name }) {
    $destination = Join-Path $file.DirectoryName 'coverage-report.xml'
    if (Test-Path -LiteralPath $destination) { throw "Coverage filename normalization would overwrite an existing file: $destination" }
    Move-Item -LiteralPath $file.FullName -Destination $destination
}
$remaining = @(Get-ChildItem -LiteralPath $search -Recurse -File | Where-Object { $legacyNames -ccontains $_.Name })
if ($remaining.Count -gt 0) { throw "A prohibited coverage filename remains under $search." }
$global:LASTEXITCODE = 0
