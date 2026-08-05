#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepositoryRoot
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$invariant = [Globalization.CultureInfo]::InvariantCulture

function Resolve-RepositoryRoot([string]$Candidate) {
    if ([string]::IsNullOrWhiteSpace($Candidate)) { $Candidate = Join-Path $PSScriptRoot ".." }
    $resolved = (Resolve-Path -LiteralPath $Candidate).Path
    if (-not (Test-Path -LiteralPath (Join-Path $resolved "SharpAccess.sln") -PathType Leaf)) {
        throw "Repository root is invalid: $resolved"
    }
    return $resolved
}

function Invoke-DotNet([string[]]$Arguments, [string]$Failure) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw $Failure }
}

function Get-CoverageGate([string]$Root, [string]$Name) {
    [xml]$policy = Get-Content -LiteralPath (Join-Path $Root "eng/CoveragePolicy.props") -Raw
    $entry = @($policy.Project.ItemGroup.CoverageGate) | Where-Object Include -CEQ $Name | Select-Object -First 1
    if ($null -eq $entry) { throw "Coverage gate is missing: $Name" }
    return [pscustomobject]@{
        Line = [decimal]::Parse([string]$entry.Line, $invariant)
        Branch = [decimal]::Parse([string]$entry.Branch, $invariant)
    }
}

function Invoke-Report([string]$Reports, [string]$Target, [string]$AssemblyFilter, [string]$Root) {
    Remove-Item -LiteralPath $Target -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $Target | Out-Null
    Invoke-DotNet @(
        "reportgenerator",
        "-reports:$Reports",
        "-targetdir:$Target",
        "-sourcedirs:$Root",
        "-assemblyfilters:$AssemblyFilter",
        "-reporttypes:XmlSummary;HtmlSummary;Html;Cobertura"
    ) "Coverage report generation failed for $Target."
    Move-Item -LiteralPath (Join-Path $Target "Summary.xml") -Destination (Join-Path $Target "coverage.xml") -Force
    & (Join-Path $Root "scripts/normalize-coverage-file-names.ps1") -RepositoryRoot $Root -SearchRoot $Target
}

function Get-RequiredCoverageCount([System.Xml.XmlElement]$Element, [string]$Name, [string]$Path) {
    $value = $Element.GetAttribute($Name)
    [long]$parsed = 0
    if ([string]::IsNullOrWhiteSpace($value) -or
        -not [long]::TryParse($value, [Globalization.NumberStyles]::Integer, $invariant, [ref]$parsed)) {
        throw "Coverage report '$Path' has no valid '$Name' count."
    }
    return $parsed
}

function Format-CoverageRate([long]$Covered, [long]$Total) {
    if ($Total -eq 0) { return "0" }
    return ([decimal]$Covered / [decimal]$Total).ToString("0.######", $invariant)
}

function Write-CanonicalCobertura([System.Collections.IDictionary]$AssemblyReports, [string]$Destination) {
    $combined = [System.Xml.XmlDocument]::new()
    [void]$combined.AppendChild($combined.CreateXmlDeclaration("1.0", "utf-8", $null))
    $coverage = $combined.CreateElement("coverage")
    [void]$combined.AppendChild($coverage)
    $sources = $combined.CreateElement("sources")
    [void]$coverage.AppendChild($sources)
    $packages = $combined.CreateElement("packages")
    [void]$coverage.AppendChild($packages)

    $sourceValues = [System.Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    [long]$linesCovered = 0
    [long]$linesValid = 0
    [long]$branchesCovered = 0
    [long]$branchesValid = 0

    foreach ($entry in @($AssemblyReports.GetEnumerator() | Sort-Object Key)) {
        $assembly = [string]$entry.Key
        $path = [string]$entry.Value
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Assembly-filtered Cobertura report is missing for '$assembly': $path"
        }

        [xml]$document = Get-Content -LiteralPath $path -Raw
        $sourceCoverage = $document.DocumentElement
        if ($null -eq $sourceCoverage -or $sourceCoverage.Name -cne "coverage") {
            throw "Assembly-filtered Cobertura report has an invalid root for '$assembly': $path"
        }

        $linesCovered += Get-RequiredCoverageCount $sourceCoverage "lines-covered" $path
        $linesValid += Get-RequiredCoverageCount $sourceCoverage "lines-valid" $path
        $branchesCovered += Get-RequiredCoverageCount $sourceCoverage "branches-covered" $path
        $branchesValid += Get-RequiredCoverageCount $sourceCoverage "branches-valid" $path

        foreach ($source in @($document.SelectNodes("/coverage/sources/source"))) {
            $value = [string]$source.InnerText
            if (-not [string]::IsNullOrWhiteSpace($value)) { [void]$sourceValues.Add($value.Trim()) }
        }

        $assemblyPackages = @($document.SelectNodes("/coverage/packages/package"))
        if ($assemblyPackages.Count -eq 0) {
            throw "Assembly-filtered Cobertura report contains no package evidence for '$assembly': $path"
        }
        $executableLines = @($assemblyPackages | ForEach-Object { $_.SelectNodes("classes/class/lines/line") }).Count
        if ($executableLines -eq 0) {
            throw "Assembly-filtered Cobertura report contains no executable lines for '$assembly': $path"
        }

        foreach ($package in $assemblyPackages) {
            $imported = [System.Xml.XmlElement]$combined.ImportNode($package, $true)
            $imported.SetAttribute("name", $assembly)
            [void]$packages.AppendChild($imported)
        }
    }

    foreach ($value in $sourceValues) {
        $source = $combined.CreateElement("source")
        $source.InnerText = $value
        [void]$sources.AppendChild($source)
    }

    $coverage.SetAttribute("line-rate", (Format-CoverageRate $linesCovered $linesValid))
    $coverage.SetAttribute("branch-rate", (Format-CoverageRate $branchesCovered $branchesValid))
    $coverage.SetAttribute("lines-covered", $linesCovered.ToString($invariant))
    $coverage.SetAttribute("lines-valid", $linesValid.ToString($invariant))
    $coverage.SetAttribute("branches-covered", $branchesCovered.ToString($invariant))
    $coverage.SetAttribute("branches-valid", $branchesValid.ToString($invariant))
    $coverage.SetAttribute("complexity", "0")
    $coverage.SetAttribute("version", "SharpAccess-canonical-1")
    $coverage.SetAttribute("timestamp", "0")

    $destinationDirectory = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
    $settings = [System.Xml.XmlWriterSettings]::new()
    $settings.Encoding = [System.Text.UTF8Encoding]::new($false)
    $settings.Indent = $true
    $settings.NewLineChars = "`n"
    $settings.NewLineHandling = [System.Xml.NewLineHandling]::Replace
    $settings.OmitXmlDeclaration = $false
    $writer = [System.Xml.XmlWriter]::Create($Destination, $settings)
    try { $combined.Save($writer) }
    finally { $writer.Dispose() }
}

if (-not [OperatingSystem]::IsWindows()) { throw "SharpAccess verification is supported on Windows only." }
$root = Resolve-RepositoryRoot $RepositoryRoot
Set-Location -LiteralPath $root

if ([string]::IsNullOrWhiteSpace($env:SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING)) {
    throw "SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING is required for PostgreSQL quality coverage."
}
if (-not [string]::Equals($env:SHARPACCESS_PROVIDER_TEST_ALLOW_RESET, "true", [StringComparison]::OrdinalIgnoreCase)) {
    throw "SHARPACCESS_PROVIDER_TEST_ALLOW_RESET=true is required because PostgreSQL quality coverage resets auth tables in a dedicated scratch database."
}

$project = "tests/SharpAccess.ProviderContractTests/SharpAccess.ProviderContractTests.csproj"
$postgresResults = "artifacts/test-results/combined/SharpAccess.ProviderContractTests.Postgres"
Remove-Item -LiteralPath $postgresResults -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $postgresResults | Out-Null
Invoke-DotNet @(
    "test", $project,
    "--configuration", "Release",
    "--no-build",
    "--settings", "coverlet.runsettings",
    "--collect:XPlat Code Coverage",
    "--filter", "Provider=Postgres",
    "--logger", "trx;LogFileName=postgres-coverage.trx",
    "--results-directory", $postgresResults
) "PostgreSQL provider-contract coverage validation failed."
& (Join-Path $root "scripts/normalize-coverage-file-names.ps1") -RepositoryRoot $root -SearchRoot (Join-Path $root $postgresResults)

$postgresRawCoverage = @(Get-ChildItem -LiteralPath (Join-Path $root $postgresResults) -Filter "coverage-report.xml" -File -Recurse -ErrorAction SilentlyContinue)
if ($postgresRawCoverage.Count -eq 0) {
    throw "PostgreSQL provider-contract validation passed but produced no Coverlet coverage evidence."
}

$reports = "artifacts/test-results/unit/**/coverage-report.xml;artifacts/test-results/combined/**/coverage-report.xml"
Invoke-Report $reports "artifacts/coverage/core" "+SharpAccess.Core" $root
Invoke-Report $reports "artifacts/coverage/sqlite" "+SharpAccess.Sqlite" $root
Invoke-Report $reports "artifacts/coverage/postgres" "+SharpAccess.Postgres" $root

$assemblyReports = [ordered]@{
    "SharpAccess.Core" = Join-Path $root "artifacts/coverage/core/coverage-report.xml"
    "SharpAccess.Sqlite" = Join-Path $root "artifacts/coverage/sqlite/coverage-report.xml"
    "SharpAccess.Postgres" = Join-Path $root "artifacts/coverage/postgres/coverage-report.xml"
}
$canonicalCoverage = Join-Path $root "artifacts/coverage/canonical/coverage-report.xml"
Write-CanonicalCobertura -AssemblyReports $assemblyReports -Destination $canonicalCoverage
Invoke-Report $canonicalCoverage "artifacts/coverage/combined" "+SharpAccess.Core;+SharpAccess.Sqlite;+SharpAccess.Postgres" $root
Copy-Item -LiteralPath $canonicalCoverage -Destination (Join-Path $root "artifacts/coverage/combined/coverage-report.xml") -Force

foreach ($name in "CombinedSupported","Core","Sqlite","Postgres") {
    $gate = Get-CoverageGate $root $name
    $directory = switch ($name) {
        "CombinedSupported" { "combined" }
        "Core" { "core" }
        "Sqlite" { "sqlite" }
        "Postgres" { "postgres" }
    }
    & (Join-Path $root "scripts/verify-coverage.ps1") -RepositoryRoot $root `
        -Path "artifacts/coverage/$directory/coverage.xml" -Label $name `
        -MinimumRate $gate.Line -MinimumBranchRate $gate.Branch
}

$changedGate = Get-CoverageGate $root "ChangedHandwrittenProduction"
& (Join-Path $root "scripts/changed-line-coverage.ps1") -RepositoryRoot $root `
    -Path "artifacts/coverage/combined/coverage-report.xml" -Scope "Supported" `
    -MinimumRate $changedGate.Line -MinimumBranchRate $changedGate.Branch

& (Join-Path $root "scripts/complexity-report.ps1") -RepositoryRoot $root `
    -CoveragePath "artifacts/coverage/combined/coverage-report.xml"

Write-Host "Complete supported-provider coverage evidence includes Core, SQLite, and PostgreSQL."
