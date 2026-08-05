#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$OutputPath,
    [switch]$NoRestore
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

    if (-not (Test-Path -LiteralPath (Join-Path $resolved ".config/dotnet-tools.json") -PathType Leaf)) {
        throw "The pinned .NET tool manifest is missing."
    }

    return $resolved
}

$root = Resolve-RepositoryRoot $RepositoryRoot
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $root "artifacts/sast/devskim.sarif"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath = Join-Path $root $OutputPath
}

$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
Remove-Item -LiteralPath $OutputPath -Force -ErrorAction SilentlyContinue

Push-Location $root
try {
    if (-not $NoRestore) {
        & dotnet tool restore --tool-manifest (Join-Path $root ".config/dotnet-tools.json")
        if ($LASTEXITCODE -ne 0) {
            throw "Pinned DevSkim tool restore failed."
        }
    }

    # Reviewed immutable fixtures and vendored schemas contain identifiers and URI vocabulary, not executable secrets or network calls.
    & dotnet tool run devskim analyze `
        --source-code $root `
        --base-path $root `
        --output-file $OutputPath `
        --file-format sarif `
        --severity "Critical,Important,Moderate" `
        --confidence "High,Medium" `
        --ignore-globs "**/.git/**,**/bin/**,**/obj/**,**/artifacts/**,**/tests/fixtures/migrations/sqlite/catalog-lock.json,**/eng/schemas/**" `
        --skip-git-ignored-files `
        --skip-excerpts `
        -E
    $scanExitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}

if ($scanExitCode -ne 0) {
    throw "DevSkim SAST failed or found blocking findings. Exit code: $scanExitCode. Review $OutputPath."
}
if (-not (Test-Path -LiteralPath $OutputPath -PathType Leaf)) {
    throw "DevSkim completed without producing the expected SARIF evidence: $OutputPath"
}

Write-Host "DevSkim SAST passed. Evidence: $OutputPath"
$global:LASTEXITCODE = 0
