#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$Path,
    [string]$BaseRef,
    [string]$HeadRef,
    [ValidateSet("Supported", "Sqlite", "Postgres", "All")][string]$Scope = "Supported",
    [string]$EvidencePath,
    [decimal]$MinimumRate = 0.90,
    [decimal]$MinimumBranchRate = 0.75)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$root = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { (Resolve-Path (Join-Path $PSScriptRoot "..")).Path } else { (Resolve-Path $RepositoryRoot).Path }
if ([string]::IsNullOrWhiteSpace($Path)) { $Path = Join-Path $root "artifacts/coverage/combined/Cobertura.xml" }
if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Cobertura file not found: $Path" }

function Invoke-Git([string[]]$Arguments) {
    $output = @(& git -C $root @Arguments 2>&1 | ForEach-Object { [string]$_ })
    if ($LASTEXITCODE -ne 0) { throw "git failed: $($output -join [Environment]::NewLine)" }
    return $output
}

function Test-NonExecutableChangedText([string[]]$Lines) {
    if ($Lines.Count -eq 0) { return $false }

    foreach ($line in $Lines) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed)) { continue }
        if ($trimmed.StartsWith("//", [StringComparison]::Ordinal)) { continue }
        if ($trimmed.StartsWith("/*", [StringComparison]::Ordinal)) { continue }
        if ($trimmed.StartsWith("*", [StringComparison]::Ordinal)) { continue }
        if ($trimmed -match '^#(?:region|endregion|pragma|nullable|line)\b') { continue }
        return $false
    }

    return $true
}

if ([string]::IsNullOrWhiteSpace($BaseRef)) {
    $status = @(& git -C $root status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0) { throw "Unable to inspect Git status." }
    if ($status.Count -gt 0) { $BaseRef = "HEAD" }
    else {
        & git -C $root rev-parse --verify HEAD^ *> $null
        $BaseRef = if ($LASTEXITCODE -eq 0) { "HEAD^" } else { "HEAD" }
    }
}

$sourcePrefixes = switch ($Scope) {
    "Supported" { @("src/SharpAccess.Core", "providers/SharpAccess.Sqlite") }
    "Sqlite" { @("providers/SharpAccess.Sqlite") }
    "Postgres" { @("providers/SharpAccess.Postgres") }
    "All" { @("src/SharpAccess.Core", "providers/SharpAccess.Sqlite", "providers/SharpAccess.Postgres") }
}
$diffArguments = @("diff", "--unified=0", "--no-color", $BaseRef)
if (-not [string]::IsNullOrWhiteSpace($HeadRef)) { $diffArguments += $HeadRef }
$diffArguments += "--"
$diffArguments += $sourcePrefixes
$diff = Invoke-Git $diffArguments
$changed = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$changedText = @{}
$current = $null
$nextAddedLine = 0
$remainingAddedLines = 0
foreach ($line in $diff) {
    if ($line.StartsWith("+++ b/", [StringComparison]::Ordinal)) {
        $current = $line.Substring(6).Replace("\", "/")
        $nextAddedLine = 0
        $remainingAddedLines = 0
        continue
    }
    if ($null -eq $current -or -not $current.EndsWith(".cs", [StringComparison]::OrdinalIgnoreCase) -or $current.Contains("/obj/", [StringComparison]::OrdinalIgnoreCase)) { continue }
    if ($line -match "^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@") {
        $nextAddedLine = [int]$Matches[1]
        $remainingAddedLines = if ([string]::IsNullOrWhiteSpace($Matches[2])) { 1 } else { [int]$Matches[2] }
        for ($number = $nextAddedLine; $number -lt $nextAddedLine + $remainingAddedLines; $number++) { [void]$changed.Add("$current|$number") }
        continue
    }
    if ($remainingAddedLines -gt 0 -and $line.StartsWith("+", [StringComparison]::Ordinal) -and -not $line.StartsWith("+++", [StringComparison]::Ordinal)) {
        $key = "$current|$nextAddedLine"
        $changedText[$key] = $line.Substring(1)
        $nextAddedLine++
        $remainingAddedLines--
    }
}

[xml]$coverage = Get-Content -LiteralPath $Path -Raw
$classNodes = @($coverage.SelectNodes("/*[local-name()='coverage']/*[local-name()='packages']/*[local-name()='package']/*[local-name()='classes']/*[local-name()='class'][@filename]"))
if ($classNodes.Count -eq 0) { throw "Cobertura class nodes were not found: $Path" }
$coveragePackages = @($coverage.SelectNodes("/*[local-name()='coverage']/*[local-name()='packages']/*[local-name()='package'][@name]") | ForEach-Object { $_.GetAttribute("name") } | Sort-Object -Unique)
$expectedPackages = switch ($Scope) {
    "Supported" { @("SharpAccess.Core", "SharpAccess.Sqlite") }
    "Sqlite" { @("SharpAccess.Sqlite") }
    "Postgres" { @("SharpAccess.Postgres") }
    "All" { @("SharpAccess.Core", "SharpAccess.Sqlite", "SharpAccess.Postgres") }
}
$missingCoveragePackages = @($expectedPackages | Where-Object { $coveragePackages -notcontains $_ })
$coverageFiles = @($classNodes | ForEach-Object { $_.GetAttribute("filename").Replace("\", "/") } | Sort-Object -Unique)
$changedFiles = @(
    $changed |
        ForEach-Object { $_.Substring(0, $_.LastIndexOf("|")) } |
        Sort-Object -Unique
)
$nonCoverableChangedFiles = [System.Collections.Generic.List[string]]::new()
$missingCoverageFiles = [System.Collections.Generic.List[string]]::new()
foreach ($changedFile in $changedFiles) {
    $coveredFile = $coverageFiles |
        Where-Object {
            $_ -eq $changedFile -or
            $_.EndsWith("/$changedFile", [StringComparison]::OrdinalIgnoreCase)
        } |
        Select-Object -First 1
    if ($null -ne $coveredFile) { continue }

    $sourcePath = Join-Path $root $changedFile
    $source = if (Test-Path -LiteralPath $sourcePath -PathType Leaf) { Get-Content -LiteralPath $sourcePath -Raw } else { "" }
    $explicitlyExcluded =
        $source -match "\[\s*ExcludeFromCodeCoverage(?:Attribute)?\s*\]" -or
        $source -match "(?i)<auto-generated"
    $changedLinesForFile = @(
        $changed |
            Where-Object { $_.StartsWith("$changedFile|", [StringComparison]::OrdinalIgnoreCase) } |
            ForEach-Object {
                if ($changedText.ContainsKey($_)) { [string]$changedText[$_] }
            }
    )
    $nonExecutableChangeOnly = Test-NonExecutableChangedText $changedLinesForFile
    if ($explicitlyExcluded -or $nonExecutableChangeOnly) {
        $nonCoverableChangedFiles.Add($changedFile)
    }
    else {
        $missingCoverageFiles.Add($changedFile)
    }
}

$hits = @{}
$branches = @{}
foreach ($classNode in $classNodes) {
    $file = $classNode.GetAttribute("filename").Replace("\", "/")
    foreach ($lineNode in @($classNode.SelectNodes("./*[local-name()='lines']/*[local-name()='line'][@number and @hits]"))) {
        $number = [int]$lineNode.GetAttribute("number")
        $lineHits = [int]$lineNode.GetAttribute("hits")
        $key = "$file|$number"
        if (-not $hits.ContainsKey($key) -or $lineHits -gt $hits[$key]) { $hits[$key] = $lineHits }
        $conditionCoverage = $lineNode.GetAttribute("condition-coverage")
        if ($conditionCoverage -match "\((\d+)/(\d+)\)") {
            $coveredBranches = [int]$Matches[1]
            $totalBranches = [int]$Matches[2]
            if (-not $branches.ContainsKey($key) -or $coveredBranches -gt $branches[$key].Covered) {
                $branches[$key] = [pscustomobject]@{ Covered = $coveredBranches; Total = $totalBranches }
            }
        }
    }
}

$covered = 0; $coverable = 0; $coveredBranches = 0; $coverableBranches = 0
$uncovered = [System.Collections.Generic.List[string]]::new()
$uncoveredBranchLines = [System.Collections.Generic.List[string]]::new()
foreach ($item in $changed) {
    $separator = $item.LastIndexOf("|")
    $file = $item.Substring(0, $separator)
    $lineNumber = $item.Substring($separator + 1)
    $match = $hits.Keys | Where-Object { $_ -eq $item -or $_.EndsWith("$file|$lineNumber", [StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
    if ($null -eq $match) { continue }
    $coverable++
    if ($hits[$match] -gt 0) { $covered++ } else { $uncovered.Add($item) }
    if ($branches.ContainsKey($match)) {
        $coveredBranches += $branches[$match].Covered
        $coverableBranches += $branches[$match].Total
        if ($branches[$match].Covered -lt $branches[$match].Total) { $uncoveredBranchLines.Add($item) }
    }
}
$rate = if ($coverable -eq 0) { [decimal]1 } else { [decimal]$covered / $coverable }
$branchRate = if ($coverableBranches -eq 0) { [decimal]1 } else { [decimal]$coveredBranches / $coverableBranches }
$revisionRef = if ([string]::IsNullOrWhiteSpace($HeadRef)) { "HEAD" } else { $HeadRef }
$revision = (Invoke-Git @("rev-parse", $revisionRef) | Select-Object -First 1).Trim()
$workingTreeDirty = @(& git -C $root status --porcelain=v1 --untracked-files=all).Count -gt 0
$evidence = [pscustomobject]@{
    revision = $revision
    workingTreeDirty = $workingTreeDirty
    baseRef = $BaseRef
    headRef = if ([string]::IsNullOrWhiteSpace($HeadRef)) { "working-tree" } else { $HeadRef }
    scope = $Scope
    sourcePrefixes = @($sourcePrefixes)
    rawChangedLines = $changed.Count
    coverableChangedLines = $coverable
    coveredChangedLines = $covered
    rate = $rate
    minimum = $MinimumRate
    uncovered = @($uncovered)
    coverableChangedBranches = $coverableBranches
    coveredChangedBranches = $coveredBranches
    branchRate = $branchRate
    minimumBranch = $MinimumBranchRate
    uncoveredBranchLines = @($uncoveredBranchLines | Sort-Object -Unique)
    nonCoverableChangedFiles = @($nonCoverableChangedFiles)
    missingCoverageFiles = @($missingCoverageFiles)
    coveragePackages = @($coveragePackages)
    missingCoveragePackages = @($missingCoveragePackages)
}
if ([string]::IsNullOrWhiteSpace($EvidencePath)) { $EvidencePath = Join-Path $root "artifacts/coverage/changed-lines.json" }
$evidencePath = if ([IO.Path]::IsPathRooted($EvidencePath)) { $EvidencePath } else { Join-Path $root $EvidencePath }
New-Item -ItemType Directory -Force (Split-Path -Parent $evidencePath) | Out-Null
$evidence | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $evidencePath -Encoding utf8
if ($rate -lt $MinimumRate) { throw "Changed-line coverage is $rate; minimum required is $MinimumRate. Evidence: $evidencePath" }
if ($branchRate -lt $MinimumBranchRate) { throw "Changed-branch coverage is $branchRate; minimum required is $MinimumBranchRate. Evidence: $evidencePath" }
if ($missingCoverageFiles.Count -ne 0) { throw "Changed production files with executable or unclassified changes are absent from coverage: $($missingCoverageFiles -join ', '). Evidence: $evidencePath" }
if ($missingCoveragePackages.Count -ne 0) { throw "Required packages are absent from coverage: $($missingCoveragePackages -join ', '). Evidence: $evidencePath" }
Write-Host "Changed-code coverage passed: line $covered/$coverable ($rate), branch $coveredBranches/$coverableBranches ($branchRate)."
