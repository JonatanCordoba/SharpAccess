#Requires -Version 7.0
[CmdletBinding()]
param([string]$RepositoryRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Resolves and validates the repository root independently of the caller's working directory.
function Resolve-RepositoryRoot([string]$Candidate) {
    if ([string]::IsNullOrWhiteSpace($Candidate)) { $Candidate = Join-Path $PSScriptRoot ".." }
    $resolved = (Resolve-Path -LiteralPath $Candidate).Path
    if (-not (Test-Path -LiteralPath (Join-Path $resolved "SharpAccess.sln") -PathType Leaf)) { throw "Repository root is invalid: $resolved" }
    return $resolved
}

# Converts filename text to lowercase ASCII for deterministic token comparison.
function Convert-ToAsciiTokenText([string]$Value) {
    $normalized = $Value.Normalize([Text.NormalizationForm]::FormD)
    $builder = [Text.StringBuilder]::new()
    foreach ($character in $normalized.ToCharArray()) {
        if ([Globalization.CharUnicodeInfo]::GetUnicodeCategory($character) -ne [Globalization.UnicodeCategory]::NonSpacingMark) {
            [void]$builder.Append([char]::ToLowerInvariant($character))
        }
    }
    return $builder.ToString().Normalize([Text.NormalizationForm]::FormC)
}

$root = Resolve-RepositoryRoot $RepositoryRoot
$termsPath = Join-Path $root "eng/ForbiddenFileNameTerms.txt"
$terms = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
Get-Content -LiteralPath $termsPath | ForEach-Object {
    $value = $_.Trim().ToLowerInvariant()
    if ($value.Length -gt 0 -and -not $value.StartsWith('#')) { [void]$terms.Add($value) }
}
$files = @(& git -C $root -c core.quotepath=false ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) { throw "Unable to enumerate active repository files." }
$violations = [Collections.Generic.List[string]]::new()
foreach ($file in $files) {
    $normalized = Convert-ToAsciiTokenText $file
    $tokens = [regex]::Split($normalized, '[^a-z0-9]+') | Where-Object { $_.Length -gt 0 }
    if ($tokens | Where-Object { $terms.Contains($_) } | Select-Object -First 1) { $violations.Add($file) }
}
if ($violations.Count -gt 0) { throw "Active file names must use English (USA). Prohibited paths: $($violations -join ', ')" }
Write-Host "English filename convention validation passed."
$global:LASTEXITCODE = 0
