#Requires -Version 7.0
[CmdletBinding()]
param([string]$RepositoryRoot)
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

# Returns every present tracked or untracked C# source file in the working tree.
function Get-WorktreeCSharpFiles([string]$Root) {
    $candidates = @(
        & git -C $Root -c core.quotepath=false ls-files `
            --cached `
            --others `
            --exclude-standard `
            -- `
            "*.cs"
    )
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to enumerate working-tree C# files."
    }

    return @(
        $candidates |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace($_) -and
                (Test-Path -LiteralPath (Join-Path $Root $_) -PathType Leaf)
            } |
            Sort-Object -Unique
    )
}

$root = Resolve-RepositoryRoot $RepositoryRoot
$violations = [System.Collections.Generic.List[string]]::new()
$convertedMetadataPattern = '^\s*//\s*(?:Parameter\s+\S+\s*:|Returns\s*:|Type\s+parameter\s+\S+\s*:|Value\s*:|Exception\s+\S+\s*:|Remarks\s*:)'
foreach ($relativePath in Get-WorktreeCSharpFiles $root) {
    $path = Join-Path $root $relativePath
    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $path) {
        $lineNumber++
        if ($line -cmatch '^\s*///(?!/)' `
            -or $line -match '<\s*/?\s*summ?ary\b' `
            -or $line -match $convertedMetadataPattern) {
            $violations.Add("${relativePath}:${lineNumber}: $line")
        }
    }
}

if ($violations.Count -gt 0) {
    $details = $violations -join [Environment]::NewLine
    throw "C# comment style violations were found. Use concise ordinary // comments and remove XML documentation syntax or converted XML metadata.$([Environment]::NewLine)$details"
}

Write-Host "C# comment style validation passed."
$global:LASTEXITCODE = 0
