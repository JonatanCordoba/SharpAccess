#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$RepositoryUrl,
    [string]$Revision,
    [string]$CreatedUtc,
    [string]$OutputDirectory,
    [string]$PackagesDirectory,
    [string[]]$RequiredPackageArchive = @(),
    [switch]$RequireAllPackageArchives,
    [switch]$StablePublication,
    [switch]$ReplaceExistingOutput
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-RepositoryRoot([string]$Candidate) {
    if ([string]::IsNullOrWhiteSpace($Candidate)) { $Candidate = Join-Path $PSScriptRoot ".." }
    $resolved = (Resolve-Path -LiteralPath $Candidate).Path
    if (-not (Test-Path -LiteralPath (Join-Path $resolved "SharpAccess.sln") -PathType Leaf)) { throw "Repository root is invalid." }
    return $resolved
}
function Resolve-RepositoryUrl([string]$Candidate, [string]$Root) {
    if ([string]::IsNullOrWhiteSpace($Candidate)) { $Candidate = $env:SHARPACCESS_REPOSITORY_URL }
    if ([string]::IsNullOrWhiteSpace($Candidate) -and -not [string]::IsNullOrWhiteSpace($env:GITHUB_REPOSITORY)) { $Candidate = "https://github.com/$($env:GITHUB_REPOSITORY)" }
    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        $Candidate = (& git -C $Root config --get remote.origin.url 2>$null)
        if ($LASTEXITCODE -ne 0) { throw "A repository URL is required." }
    }
    $Candidate = $Candidate.Trim()
    if ($Candidate.EndsWith(".git", [StringComparison]::OrdinalIgnoreCase)) { $Candidate = $Candidate.Substring(0, $Candidate.Length - 4) }
    if ($Candidate -match "^git@github\.com:(.+)$") { $Candidate = "https://github.com/$($Matches[1])" }
    $allowed = @("https://github.com/JonatanCordoba/SharpAccess")
    $normalized = $Candidate.TrimEnd("/")
    if ($allowed -notcontains $normalized) { throw "The repository URL must be an approved SharpAccess lifecycle identity." }
    return $normalized
}
function Resolve-SafeDirectory([string]$Candidate, [string]$Root, [string]$DefaultChild) {
    $artifacts = [IO.Path]::GetFullPath((Join-Path $Root "artifacts"))
    $resolved = if ([string]::IsNullOrWhiteSpace($Candidate)) { [IO.Path]::GetFullPath((Join-Path $artifacts $DefaultChild)) } else { [IO.Path]::GetFullPath($Candidate, $Root) }
    $prefix = $artifacts.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "SBOM paths must be strict children of the repository artifacts directory." }
    return $resolved
}
function Get-TreeHashes([string]$Directory) {
    return @(Get-ChildItem -LiteralPath $Directory -File | Sort-Object Name | ForEach-Object { "$($_.Name)`t$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())" })
}
function Invoke-Generator([string]$Target) {
    $arguments = @(
        "run", "--project", (Join-Path $root "tools/SharpAccess.Sbom/SharpAccess.Sbom.csproj"),
        "--configuration", "Release", "--no-restore", "--",
        "--repository-root", $root,
        "--output-directory", $Target,
        "--packages-directory", $packages,
        "--repository-url", $resolvedRepositoryUrl,
        "--revision", $resolvedRevision)
    foreach ($packageId in $RequiredPackageArchive) { $arguments += @("--require-package-archive", $packageId) }
    if ($RequireAllPackageArchives) { $arguments += "--require-all-package-archives" }
    if ($StablePublication) { $arguments += "--stable-publication" }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Formal SBOM generation failed." }
}

if (-not [OperatingSystem]::IsWindows()) { throw "SharpAccess SBOM generation is supported on Windows only." }
if (-not [string]::IsNullOrWhiteSpace($CreatedUtc)) { throw "CreatedUtc is derived from Git and must not be supplied." }
$root = Resolve-RepositoryRoot $RepositoryRoot
$resolvedRepositoryUrl = Resolve-RepositoryUrl $RepositoryUrl $root
$head = (& git -C $root rev-parse HEAD).Trim().ToLowerInvariant()
if ($LASTEXITCODE -ne 0 -or $head -notmatch "^[0-9a-f]{40}$") { throw "The checked-out Git revision could not be resolved." }
$resolvedRevision = if ([string]::IsNullOrWhiteSpace($Revision)) { $head } else { $Revision.Trim().ToLowerInvariant() }
if ($resolvedRevision -cne $head) { throw "Revision must equal checked-out HEAD $head." }
$output = Resolve-SafeDirectory $OutputDirectory $root "sbom"
$packages = Resolve-SafeDirectory $PackagesDirectory $root "packages"
if ($output -eq $packages) { throw "SBOM output and package directories must differ." }
$expectedNames = @(
    "SharpAccess.Core.cyclonedx.json", "SharpAccess.Core.spdx.json",
    "SharpAccess.Sqlite.cyclonedx.json", "SharpAccess.Sqlite.spdx.json",
    "SharpAccess.Postgres.cyclonedx.json", "SharpAccess.Postgres.spdx.json",
    "sbom-evidence.json")
if (Test-Path -LiteralPath $output) {
    $unexpected = @(Get-ChildItem -LiteralPath $output -Force | Where-Object { $_.PSIsContainer -or $expectedNames -notcontains $_.Name })
    if ($unexpected.Count -ne 0) { throw "SBOM output contains unexpected entries." }
    if (-not $ReplaceExistingOutput -and @(Get-ChildItem -LiteralPath $output -Force).Count -ne 0) { throw "SBOM output already exists; use -ReplaceExistingOutput after review." }
    Remove-Item -LiteralPath $output -Recurse -Force
}
$temp = Join-Path (Split-Path -Parent $output) "sbom-repeat-$([Guid]::NewGuid().ToString('N'))"
try {
    New-Item -ItemType Directory -Path $output,$temp -Force | Out-Null
    Invoke-Generator $output
    Invoke-Generator $temp
    $first = Get-TreeHashes $output
    $second = Get-TreeHashes $temp
    if (($first -join "`n") -cne ($second -join "`n")) { throw "SBOM generation is not byte reproducible." }
    $actualNames = @(Get-ChildItem -LiteralPath $output -File | Select-Object -ExpandProperty Name | Sort-Object)
    if (($actualNames -join "|") -cne (($expectedNames | Sort-Object) -join "|")) { throw "SBOM output does not contain the exact active package evidence set." }
}
finally { Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue }
Write-Host "SBOM evidence generated for Core, SQLite, and PostgreSQL: $output"
