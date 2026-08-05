#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string[]]$InputPath = @(
        "artifacts/packages",
        "artifacts/sbom",
        "artifacts/performance",
        "artifacts/operations",
        "artifacts/provider-coverage",
        "artifacts/release-export",
        "artifacts/release-candidate"
    ),
    [string]$OutputDirectory = "artifacts/release-candidate"
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

# Converts one path to a repository-relative path with forward slashes.
function Get-RelativePath([string]$Root, [string]$Path) {
    return [IO.Path]::GetRelativePath($Root, $Path).Replace('\', '/')
}

$root = Resolve-RepositoryRoot $RepositoryRoot
$output = Join-Path $root $OutputDirectory
New-Item -ItemType Directory -Force -Path $output | Out-Null
$textPath = Join-Path $output "SHA256SUMS"
$jsonPath = Join-Path $output "checksums.json"

$files = foreach ($relativeInput in $InputPath) {
    $candidate = Join-Path $root $relativeInput
    if (-not (Test-Path -LiteralPath $candidate)) {
        continue
    }

    Get-ChildItem -LiteralPath $candidate -File -Recurse |
        Where-Object {
            $isTextChecksum = $_.FullName -eq $textPath
            $isJsonChecksum = $_.FullName -eq $jsonPath
            $isTemporaryFile = $_.Name -match '(?i)\.tmp$'
            -not ($isTextChecksum -or $isJsonChecksum -or $isTemporaryFile)
        }
}

$files = @($files | Sort-Object { Get-RelativePath $root $_.FullName } -Unique)
if ($files.Count -eq 0) {
    throw "No release-candidate evidence files were found for checksum generation."
}

$entries = foreach ($file in $files) {
    $relative = Get-RelativePath $root $file.FullName
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    [ordered]@{
        path = $relative
        sha256 = $hash
        length = $file.Length
    }
}

$entries |
    ForEach-Object { "$($_.sha256)  $($_.path)" } |
    Set-Content -LiteralPath $textPath -Encoding utf8NoBOM

[ordered]@{
    schemaVersion = 1
    algorithm = "SHA-256"
    generatedUtc = [DateTimeOffset]::UtcNow.ToString("O")
    fileCount = $entries.Count
    files = $entries
} |
    ConvertTo-Json -Depth 6 |
    Set-Content -LiteralPath $jsonPath -Encoding utf8NoBOM

Write-Host "Release-candidate checksums were written to artifacts/release-candidate."
$global:LASTEXITCODE = 0
