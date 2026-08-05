#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet("Sqlite", "Postgres")][string]$Provider,
    [string]$RepositoryRoot,
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [string]$ChangedCodeBaseRef,
    [switch]$NoBuild,
    [switch]$PromotionGate
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$root = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { (Resolve-Path (Join-Path $PSScriptRoot "..")).Path } else { (Resolve-Path $RepositoryRoot).Path }
$Provider = if ($Provider.Equals("sqlite", [StringComparison]::OrdinalIgnoreCase)) { "Sqlite" } else { "Postgres" }
[xml]$thresholds = Get-Content -LiteralPath (Join-Path $root "eng/ProviderCoverage.props") -Raw
$entry = @($thresholds.Project.ItemGroup.ProviderCoverageThreshold) | Where-Object Include -CEQ $Provider | Select-Object -First 1
if ($null -eq $entry) { throw "Threshold missing for $Provider." }
$line = if ($PromotionGate) { [decimal]::Parse([string]$entry.PromotionLine, [Globalization.CultureInfo]::InvariantCulture) } else { [decimal]::Parse([string]$entry.Line, [Globalization.CultureInfo]::InvariantCulture) }
$branch = if ($PromotionGate) { [decimal]::Parse([string]$entry.PromotionBranch, [Globalization.CultureInfo]::InvariantCulture) } else { [decimal]::Parse([string]$entry.Branch, [Globalization.CultureInfo]::InvariantCulture) }
$out = Join-Path $root "artifacts/provider-coverage/$($Provider.ToLowerInvariant())"
$results = Join-Path $root "artifacts/test-results/provider-coverage/$($Provider.ToLowerInvariant())"
Remove-Item $out,$results -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $out,$results | Out-Null
Set-Location -LiteralPath $root
& dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw "Tool restore failed." }
$projects = @("SharpAccess.UnitTests", "SharpAccess.IntegrationTests", "SharpAccess.EndpointTests", "SharpAccess.ProviderContractTests", "SharpAccess.PackageTests")
foreach ($project in $projects) {
    $resultPath = Join-Path $results $project
    $arguments = @(
        "test", "tests/$project/$project.csproj",
        "--configuration", $Configuration,
        "--settings", "coverlet.runsettings",
        "--collect:XPlat Code Coverage",
        "--logger", "trx;LogFileName=provider-coverage.trx",
        "--results-directory", $resultPath)
    if ($project -eq "SharpAccess.ProviderContractTests") { $arguments += @("--filter", "Provider=$Provider") }
    if ($NoBuild) { $arguments += "--no-build" }
    & dotnet @arguments
    $testExitCode = $LASTEXITCODE
    & (Join-Path $root "scripts/normalize-coverage-file-names.ps1") -RepositoryRoot $root -SearchRoot $resultPath
    if ($testExitCode -ne 0) { throw "$Provider provider coverage tests failed for $project." }
}
& (Join-Path $root "scripts/normalize-coverage-file-names.ps1") -RepositoryRoot $root -SearchRoot $results
& dotnet reportgenerator "-reports:$results/**/coverage-report.xml" "-targetdir:$out" "-sourcedirs:$root" "-assemblyfilters:+SharpAccess.$Provider" "-reporttypes:XmlSummary;HtmlSummary;Cobertura"
if ($LASTEXITCODE -ne 0) { throw "Coverage report failed." }
Move-Item (Join-Path $out "Summary.xml") (Join-Path $out "coverage.xml") -Force
& (Join-Path $root "scripts/normalize-coverage-file-names.ps1") -RepositoryRoot $root -SearchRoot $out
& (Join-Path $root "scripts/verify-coverage.ps1") -RepositoryRoot $root -Path (Join-Path $out "coverage.xml") -Label "$Provider provider" -MinimumRate $line -MinimumBranchRate $branch
[xml]$coveragePolicy = Get-Content -LiteralPath (Join-Path $root "eng/CoveragePolicy.props") -Raw
$changedEntry = @($coveragePolicy.Project.ItemGroup.CoverageGate) | Where-Object Include -CEQ "ChangedHandwrittenProduction" | Select-Object -First 1
if ($null -eq $changedEntry) { throw "Changed-code coverage thresholds are missing." }
$changedArguments = @{
    RepositoryRoot = $root
    Path = Join-Path $out "coverage-report.xml"
    Scope = $Provider
    EvidencePath = Join-Path $out "changed-code.json"
    MinimumRate = [decimal]::Parse([string]$changedEntry.Line, [Globalization.CultureInfo]::InvariantCulture)
    MinimumBranchRate = [decimal]::Parse([string]$changedEntry.Branch, [Globalization.CultureInfo]::InvariantCulture)
}
if (-not [string]::IsNullOrWhiteSpace($ChangedCodeBaseRef)) { $changedArguments.BaseRef = $ChangedCodeBaseRef }
& (Join-Path $root "scripts/changed-line-coverage.ps1") @changedArguments
$revision = (& git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw "Unable to resolve the provider coverage revision." }
$dirty = @(& git -C $root status --porcelain=v1 --untracked-files=all).Count -gt 0
[pscustomobject]@{
    revision = $revision
    workingTreeDirty = $dirty
    provider = $Provider
    promotionGate = [bool]$PromotionGate
    minimumLine = $line
    minimumBranch = $branch
    coverageSha256 = (Get-FileHash -LiteralPath (Join-Path $out "coverage.xml") -Algorithm SHA256).Hash.ToLowerInvariant()
    changedCodeSha256 = (Get-FileHash -LiteralPath (Join-Path $out "changed-code.json") -Algorithm SHA256).Hash.ToLowerInvariant()
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $out "evidence.json") -Encoding utf8
