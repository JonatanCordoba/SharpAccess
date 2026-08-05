#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$MetricsPath,
    [string]$ReadmePath,
    [string]$WikiQualityPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedRepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
if ([string]::IsNullOrWhiteSpace($MetricsPath)) {
    $MetricsPath = Join-Path $resolvedRepositoryRoot 'artifacts/quality-report/metrics.json'
}
if ([string]::IsNullOrWhiteSpace($ReadmePath)) {
    $ReadmePath = Join-Path $resolvedRepositoryRoot 'README.md'
}

function Get-Percentile {
    param(
        [Parameter(Mandatory)][double[]]$Values,
        [Parameter(Mandatory)][ValidateRange(0, 100)][double]$Percentile
    )

    if ($Values.Count -eq 0) { return $null }
    $sorted = @($Values | Sort-Object)
    if ($sorted.Count -eq 1) { return [double]$sorted[0] }

    $position = ($Percentile / 100.0) * ($sorted.Count - 1)
    $lower = [math]::Floor($position)
    $upper = [math]::Ceiling($position)
    if ($lower -eq $upper) { return [double]$sorted[$lower] }

    $weight = $position - $lower
    return ([double]$sorted[$lower] * (1.0 - $weight)) + ([double]$sorted[$upper] * $weight)
}

function Format-Number([Nullable[double]]$Value, [string]$Format = '0.##') {
    if ($null -eq $Value) { return 'n/a' }
    return ([double]$Value).ToString($Format, [Globalization.CultureInfo]::InvariantCulture)
}

function Format-Percent([Nullable[double]]$Value) {
    if ($null -eq $Value) { return 'n/a' }
    return ([double]$Value).ToString('0.00', [Globalization.CultureInfo]::InvariantCulture) + '%'
}

function Get-NullableValues {
    param([object[]]$Items, [scriptblock]$Selector)

    $values = [Collections.Generic.List[double]]::new()
    foreach ($item in $Items) {
        $value = & $Selector $item
        if ($null -ne $value) { $values.Add([double]$value) }
    }

    return $values.ToArray()
}

function Replace-Block {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Replacement
    )

    $start = '<!-- SHARPACCESS_QUALITY_SNAPSHOT_START -->'
    $end = '<!-- SHARPACCESS_QUALITY_SNAPSHOT_END -->'
    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    $content = [IO.File]::ReadAllText($resolvedPath)
    $pattern = [regex]::Escape($start) + '.*?' + [regex]::Escape($end)
    if (-not [regex]::IsMatch($content, $pattern, [Text.RegularExpressions.RegexOptions]::Singleline)) {
        throw "Quality snapshot markers are missing from $resolvedPath."
    }

    $updated = [regex]::Replace(
        $content,
        $pattern,
        "$start`n$Replacement`n$end",
        [Text.RegularExpressions.RegexOptions]::Singleline)

    [IO.File]::WriteAllText(
        $resolvedPath,
        $updated.Replace("`r`n", "`n"),
        [Text.UTF8Encoding]::new($false))
}

$metrics = Get-Content -Raw -LiteralPath $MetricsPath | ConvertFrom-Json -Depth 100
if ($metrics.schemaVersion -lt 2) {
    throw "Unsupported quality metrics schema: $($metrics.schemaVersion)."
}

$members = @($metrics.members)
$dependencies = @($metrics.dependencies)

$lineValues = Get-NullableValues ($members | Where-Object { $_.coverage.totalLines -gt 0 }) {
    param($member) $member.coverage.lineCoverage
}
$branchValues = Get-NullableValues ($members | Where-Object { $_.coverage.totalBranches -gt 0 }) {
    param($member) $member.coverage.branchCoverage
}
$instabilityValues = Get-NullableValues ($dependencies | Where-Object { $null -ne $_.instability }) {
    param($dependency) $dependency.instability
}
$caValues = Get-NullableValues $dependencies { param($dependency) $dependency.afferentCoupling }
$ceValues = Get-NullableValues $dependencies { param($dependency) $dependency.efferentCoupling }

$lineP95 = Get-Percentile $lineValues 95
$lineWorst = if ($lineValues.Count) { ($lineValues | Measure-Object -Minimum).Minimum } else { $null }
$branchP95 = Get-Percentile $branchValues 95
$branchWorst = if ($branchValues.Count) { ($branchValues | Measure-Object -Minimum).Minimum } else { $null }
$instabilityP95 = Get-Percentile $instabilityValues 95
$instabilityWorst = if ($instabilityValues.Count) { ($instabilityValues | Measure-Object -Maximum).Maximum } else { $null }
$caP95 = Get-Percentile $caValues 95
$caWorst = if ($caValues.Count) { ($caValues | Measure-Object -Maximum).Maximum } else { $null }
$ceP95 = Get-Percentile $ceValues 95
$ceWorst = if ($ceValues.Count) { ($ceValues | Measure-Object -Maximum).Maximum } else { $null }

$aggregateLine = [Nullable[double]]$metrics.summary.coverage.lineCoverage
$aggregateBranch = [Nullable[double]]$metrics.summary.coverage.branchCoverage

$rows = @(
    '| Metric | p95 | Worst observed | Aggregate / release interpretation |',
    '|---|---:|---:|---|',
    "| Line coverage | $(Format-Percent $lineP95) | $(Format-Percent $lineWorst) minimum | $(Format-Percent $aggregateLine) repository aggregate |",
    "| Branch coverage | $(Format-Percent $branchP95) | $(Format-Percent $branchWorst) minimum | $(Format-Percent $aggregateBranch) repository aggregate |",
    "| CRAP score | $(Format-Number ([Nullable[double]]$metrics.summary.crapScore.percentile95)) | $(Format-Number ([Nullable[double]]$metrics.summary.crapScore.maximum)) maximum | Executable methods only |",
    "| Cyclomatic complexity | $(Format-Number ([Nullable[double]]$metrics.summary.cyclomaticComplexity.percentile95)) | $(Format-Number ([Nullable[double]]$metrics.summary.cyclomaticComplexity.maximum)) maximum | Roslyn source metrics |",
    "| Maintainability index | $(Format-Number ([Nullable[double]]$metrics.summary.maintainabilityIndex.percentile95)) | $(Format-Number ([Nullable[double]]$metrics.summary.maintainabilityIndex.minimum)) minimum | Higher is better |",
    "| Class coupling | $(Format-Number ([Nullable[double]]$metrics.summary.classCoupling.percentile95)) | $(Format-Number ([Nullable[double]]$metrics.summary.classCoupling.maximum)) maximum | Distinct referenced types |",
    "| Afferent coupling (Ca) | $(Format-Number ([Nullable[double]]$caP95)) | $(Format-Number ([Nullable[double]]$caWorst)) maximum | Project + namespace units |",
    "| Efferent coupling (Ce) | $(Format-Number ([Nullable[double]]$ceP95)) | $(Format-Number ([Nullable[double]]$ceWorst)) maximum | Project + namespace units |",
    "| Instability | $(Format-Number -Value ([Nullable[double]]$instabilityP95) -Format '0.000') | $(Format-Number -Value ([Nullable[double]]$instabilityWorst) -Format '0.000') maximum | ``Ce / (Ca + Ce)``; informational |",
    '| Critical mutation invariants | N/A | **0 survived; 0 infrastructure failures required** | Binary per selected invariant; release tier must pass |'
)

$block = @"
**Source:** exact-revision ``artifacts/quality-report/metrics.json`` · **Schema:** $($metrics.schemaVersion) · **Enforcement:** $($metrics.enforcement)

$($rows -join "`n")

<details>
<summary>Scope and percentile notes</summary>

- Coverage percentiles are calculated across matched executable production members in ``SharpAccess.Core``, ``SharpAccess.Sqlite``, and ``SharpAccess.Postgres``.
- The repository aggregate remains the release coverage score; the minimum exposes the worst observed member because coverage is higher-is-better.
- CRAP, cyclomatic complexity, maintainability index, and class-coupling statistics use the report's exact member dataset.
- Ca, Ce, and instability are calculated across project and namespace dependency units.
- Mutation invariants are binary and therefore do not have a meaningful p95.
- Consult ``artifacts/quality-report/index.html`` and ``metrics.json`` for complete project, namespace, type, member, dependency, and hotspot detail.

</details>
"@

Replace-Block -Path $ReadmePath -Replacement $block
if (-not [string]::IsNullOrWhiteSpace($WikiQualityPath)) {
    Replace-Block -Path $WikiQualityPath -Replacement $block
}

Write-Host "Updated the public quality snapshot from metrics revision $($metrics.revision)."
