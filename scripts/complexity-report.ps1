#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$CoveragePath = "artifacts/coverage/combined/coverage-report.xml",
    [string]$BaselinePath = "eng/ComplexityBaseline.json",
    [string]$OutputDirectory = "artifacts/quality/complexity",
    [switch]$UpdateBaseline
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$invariant = [Globalization.CultureInfo]::InvariantCulture

# Resolves and validates the selected repository root.
function Resolve-RepositoryRoot([string]$Candidate) {
    if ([string]::IsNullOrWhiteSpace($Candidate)) { $Candidate = Join-Path $PSScriptRoot ".." }
    $resolved = (Resolve-Path -LiteralPath $Candidate).Path
    if (-not (Test-Path -LiteralPath (Join-Path $resolved "SharpAccess.sln") -PathType Leaf)) { throw "Repository root is invalid: $resolved" }
    return $resolved
}

# Resolves one repository-relative path while allowing an explicit absolute path.
function Resolve-RepositoryPath([string]$Root, [string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) { return $Path }
    return Join-Path $Root $Path
}

# Reads every production complexity scope and its status-aware enforcement mode.
function Get-ComplexityPolicies([string]$Root) {
    $path = Join-Path $Root "eng/ComplexityPolicy.props"
    [xml]$document = Get-Content -LiteralPath $path -Raw
    $policies = [System.Collections.Generic.List[object]]::new()
    $ownedAssemblies = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($gate in @($document.Project.ItemGroup.ComplexityGate)) {
        $name = [string]$gate.Include
        if ([string]::IsNullOrWhiteSpace($name)) { throw "A complexity scope is missing Include." }
        $assemblies = @(([string]$gate.Assemblies).Split(';', [StringSplitOptions]::RemoveEmptyEntries -bor [StringSplitOptions]::TrimEntries))
        if ($assemblies.Count -eq 0) { throw "Complexity scope '$name' has no assemblies." }
        foreach ($assembly in $assemblies) {
            if (-not $ownedAssemblies.Add($assembly)) { throw "Complexity assembly '$assembly' belongs to more than one scope." }
        }
        $enforcement = [string]$gate.Enforcement
        if ([string]::IsNullOrWhiteSpace($enforcement)) { $enforcement = 'ReportOnly' }
        if ($enforcement -notin @('Ratchet', 'ReportOnly')) { throw "Complexity scope '$name' has unsupported enforcement '$enforcement'." }
        $policies.Add([pscustomobject]@{
            Name = $name
            Assemblies = $assemblies
            Enforcement = $enforcement
            MaximumCyclomaticComplexity = [int]::Parse([string]$gate.MaximumCyclomaticComplexity, $invariant)
            MaximumCrapScore = [double]::Parse([string]$gate.MaximumCrapScore, $invariant)
            CrapRegressionTolerance = [double]::Parse([string]$gate.CrapRegressionTolerance, $invariant)
            ReportTop = [int]::Parse([string]$gate.ReportTop, $invariant)
        })
    }
    if ($policies.Count -eq 0) { throw "At least one complexity scope is required." }
    if (@($policies | Where-Object Enforcement -CEQ 'Ratchet').Count -ne 1) { throw "Exactly one complexity scope must use Ratchet enforcement." }
    return @($policies)
}

# Parses one invariant decimal attribute with the selected fallback.
function Get-DecimalAttribute([System.Xml.XmlElement]$Element, [string]$Name, [double]$Fallback) {
    $value = $Element.GetAttribute($Name)
    if ([string]::IsNullOrWhiteSpace($value)) { return $Fallback }
    return [double]::Parse($value, $invariant)
}

# Calculates the standard CRAP score from method complexity and line coverage.
function Get-CrapScore([int]$Complexity, [double]$LineRate) {
    $uncovered = 1.0 - [Math]::Min(1.0, [Math]::Max(0.0, $LineRate))
    return [Math]::Round(($Complexity * $Complexity * [Math]::Pow($uncovered, 3)) + $Complexity, 2, [MidpointRounding]::AwayFromZero)
}

# Extracts deterministic method metrics from every declared production assembly.
function Get-ComplexityMetrics([string]$Root, [string]$Path, [object[]]$Policies) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Coverage report is missing: $Path" }
    [xml]$document = Get-Content -LiteralPath $Path -Raw
    $scopeByAssembly = @{}
    foreach ($policy in $Policies) { foreach ($assembly in $policy.Assemblies) { $scopeByAssembly[$assembly] = $policy.Name } }
    $metrics = [System.Collections.Generic.List[object]]::new()
    foreach ($package in @($document.SelectNodes('/coverage/packages/package'))) {
        $assembly = $package.GetAttribute('name')
        if (-not $scopeByAssembly.ContainsKey($assembly)) { continue }
        foreach ($class in @($package.SelectNodes('classes/class'))) {
            $className = $class.GetAttribute('name')
            $file = $class.GetAttribute('filename').Replace('\', '/')
            if ([IO.Path]::IsPathRooted($file)) {
                try { $file = [IO.Path]::GetRelativePath($Root, $file).Replace('\', '/') } catch { }
            }
            foreach ($method in @($class.SelectNodes('methods/method'))) {
                $methodName = $method.GetAttribute('name')
                $signature = $method.GetAttribute('signature')
                $complexity = [Math]::Max(1, [int][Math]::Round((Get-DecimalAttribute $method 'complexity' 1.0), 0, [MidpointRounding]::AwayFromZero))
                $lineRate = Get-DecimalAttribute $method 'line-rate' 0.0
                $lineNumbers = @($method.SelectNodes('lines/line') | ForEach-Object { [int]$_.GetAttribute('number') })
                $startLine = if ($lineNumbers.Count -eq 0) { 0 } else { ($lineNumbers | Measure-Object -Minimum).Minimum }
                $endLine = if ($lineNumbers.Count -eq 0) { 0 } else { ($lineNumbers | Measure-Object -Maximum).Maximum }
                $key = "$assembly|$className|$methodName|$signature"
                $metrics.Add([pscustomobject]@{
                    Key = $key
                    Scope = [string]$scopeByAssembly[$assembly]
                    Assembly = $assembly
                    Class = $className
                    Method = $methodName
                    Signature = $signature
                    File = $file
                    StartLine = [int]$startLine
                    EndLine = [int]$endLine
                    CyclomaticComplexity = $complexity
                    LineCoverage = [Math]::Round($lineRate * 100.0, 2, [MidpointRounding]::AwayFromZero)
                    CrapScore = Get-CrapScore $complexity $lineRate
                })
            }
        }
    }
    $ordered = @($metrics | Sort-Object @{ Expression = 'CrapScore'; Descending = $true }, @{ Expression = 'CyclomaticComplexity'; Descending = $true }, Key)
    if ($ordered.Count -eq 0) { throw "No declared production method metrics were found in $Path." }
    $ratchetAssemblies = @($Policies | Where-Object Enforcement -CEQ 'Ratchet' | ForEach-Object Assemblies)
    if (@($ordered | Where-Object { $ratchetAssemblies -contains $_.Assembly }).Count -eq 0) { throw "No ratcheted production method metrics were found in $Path." }
    return $ordered
}

# Selects methods that exceed the thresholds for their declared scope.
function Get-Hotspots([object[]]$Metrics, [object[]]$Policies) {
    $policyByName = @{}
    foreach ($policy in $Policies) { $policyByName[$policy.Name] = $policy }
    return @($Metrics | Where-Object {
        $policy = $policyByName[$_.Scope]
        $_.CyclomaticComplexity -gt $policy.MaximumCyclomaticComplexity -or $_.CrapScore -gt $policy.MaximumCrapScore
    })
}

# Writes deterministic JSON, CSV, and Markdown complexity evidence for every production scope.
function Write-ComplexityEvidence([string]$Directory, [object[]]$Metrics, [object[]]$Hotspots, [object[]]$Violations, [object[]]$Policies) {
    New-Item -ItemType Directory -Force -Path $Directory | Out-Null
    $jsonPath = Join-Path $Directory 'complexity.json'
    $csvPath = Join-Path $Directory 'complexity.csv'
    $markdownPath = Join-Path $Directory 'complexity.md'
    $scopeSummaries = @($Policies | ForEach-Object {
        $scope = $_
        [ordered]@{
            name = $scope.Name
            enforcement = $scope.Enforcement
            assemblies = @($scope.Assemblies)
            methods = @($Metrics | Where-Object Scope -CEQ $scope.Name).Count
            hotspots = @($Hotspots | Where-Object Scope -CEQ $scope.Name).Count
        }
    })
    $payload = [ordered]@{
        schemaVersion = 2
        scopes = $scopeSummaries
        totals = [ordered]@{ methods = $Metrics.Count; hotspots = $Hotspots.Count; violations = $Violations.Count }
        violations = @($Violations)
        methods = @($Metrics)
    }
    [IO.File]::WriteAllText($jsonPath, (($payload | ConvertTo-Json -Depth 9) + [Environment]::NewLine), [System.Text.UTF8Encoding]::new($false))
    $Metrics | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding utf8
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('# Complexity and CRAP inventory')
    $lines.Add('')
    $lines.Add("Methods: $($Metrics.Count); hotspots: $($Hotspots.Count); ratchet violations: $($Violations.Count).")
    $lines.Add('')
    $lines.Add('| Scope | Enforcement | Methods | Hotspots | Assemblies |')
    $lines.Add('|---|---|---:|---:|---|')
    foreach ($scope in $scopeSummaries) { $lines.Add("| $($scope.name) | $($scope.enforcement) | $($scope.methods) | $($scope.hotspots) | $(@($scope.assemblies) -join ', ') |") }
    $lines.Add('')
    $lines.Add('| CRAP | Complexity | Coverage | Scope | Method | Location |')
    $lines.Add('|---:|---:|---:|---|---|---|')
    $reportTop = ($Policies.ReportTop | Measure-Object -Maximum).Maximum
    foreach ($metric in @($Metrics | Select-Object -First $reportTop)) {
        $methodLabel = "$($metric.Class).$($metric.Method)".Replace('|', '\|')
        $location = "$($metric.File):$($metric.StartLine)".Replace('|', '\|')
        $lines.Add("| $($metric.CrapScore) | $($metric.CyclomaticComplexity) | $($metric.LineCoverage)% | $($metric.Scope) | $methodLabel | $location |")
    }
    [IO.File]::WriteAllLines($markdownPath, $lines, [System.Text.UTF8Encoding]::new($false))
}

# Replaces the reviewed ratcheted hotspot baseline with the current supported-production debt inventory.
function Update-ComplexityBaseline([string]$Path, [object[]]$Hotspots, [pscustomobject]$Policy) {
    $baseline = [ordered]@{
        schemaVersion = 1
        baselineStatus = 'approved'
        policy = [ordered]@{ assemblies = @($Policy.Assemblies); maximumCyclomaticComplexity = $Policy.MaximumCyclomaticComplexity; maximumCrapScore = $Policy.MaximumCrapScore; crapRegressionTolerance = $Policy.CrapRegressionTolerance }
        hotspots = @($Hotspots | Select-Object Key, Assembly, Class, Method, Signature, File, StartLine, CyclomaticComplexity, CrapScore)
    }
    [IO.File]::WriteAllText($Path, (($baseline | ConvertTo-Json -Depth 7) + [Environment]::NewLine), [System.Text.UTF8Encoding]::new($false))
    Write-Host "Complexity baseline updated with $($Hotspots.Count) reviewed ratcheted hotspot(s): $Path"
}

# Compares current ratcheted hotspots to the approved baseline and returns new or worsened debt.
function Get-ComplexityViolations([string]$Path, [object[]]$Hotspots, [pscustomobject]$Policy) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Complexity baseline is missing: $Path" }
    $baseline = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ($baseline.schemaVersion -ne 1 -or $baseline.baselineStatus -ne 'approved') { throw "Complexity baseline requires explicit bootstrap approval. Run setup-test with -UpdateComplexityBaseline, inspect eng/ComplexityBaseline.json, and commit it." }
    if ([int]$baseline.policy.maximumCyclomaticComplexity -ne $Policy.MaximumCyclomaticComplexity -or [double]$baseline.policy.maximumCrapScore -ne $Policy.MaximumCrapScore) { throw "Complexity policy changed without a reviewed baseline refresh." }
    $baselineAssemblies = @($baseline.policy.assemblies | Sort-Object)
    $policyAssemblies = @($Policy.Assemblies | Sort-Object)
    if (@(Compare-Object -ReferenceObject $baselineAssemblies -DifferenceObject $policyAssemblies).Count -ne 0) { throw "Ratcheted complexity assemblies changed without a reviewed baseline refresh." }
    $approved = @{}
    foreach ($entry in @($baseline.hotspots)) { $approved[[string]$entry.Key] = $entry }
    $violations = [System.Collections.Generic.List[object]]::new()
    foreach ($hotspot in $Hotspots) {
        if (-not $approved.ContainsKey($hotspot.Key)) {
            $violations.Add([pscustomobject]@{ Kind = 'NewHotspot'; Key = $hotspot.Key; Scope = $hotspot.Scope; File = $hotspot.File; StartLine = $hotspot.StartLine; CyclomaticComplexity = $hotspot.CyclomaticComplexity; CrapScore = $hotspot.CrapScore; Message = 'New complexity hotspot exceeds the approved thresholds.' })
            continue
        }
        $previous = $approved[$hotspot.Key]
        $complexityWorsened = $hotspot.CyclomaticComplexity -gt [int]$previous.CyclomaticComplexity
        $crapWorsened = $hotspot.CrapScore -gt ([double]$previous.CrapScore + $Policy.CrapRegressionTolerance)
        if ($complexityWorsened -or $crapWorsened) {
            $violations.Add([pscustomobject]@{ Kind = 'WorsenedHotspot'; Key = $hotspot.Key; Scope = $hotspot.Scope; File = $hotspot.File; StartLine = $hotspot.StartLine; CyclomaticComplexity = $hotspot.CyclomaticComplexity; CrapScore = $hotspot.CrapScore; PreviousCyclomaticComplexity = [int]$previous.CyclomaticComplexity; PreviousCrapScore = [double]$previous.CrapScore; Message = 'Existing complexity hotspot became worse than its approved baseline.' })
        }
    }
    return @($violations)
}

$root = Resolve-RepositoryRoot $RepositoryRoot
$coverage = Resolve-RepositoryPath $root $CoveragePath
$baseline = Resolve-RepositoryPath $root $BaselinePath
$output = Resolve-RepositoryPath $root $OutputDirectory
$policies = @(Get-ComplexityPolicies -Root $root)
$ratchetPolicy = $policies | Where-Object Enforcement -CEQ 'Ratchet' | Select-Object -First 1
$metrics = @(Get-ComplexityMetrics -Root $root -Path $coverage -Policies $policies)
$hotspots = @(Get-Hotspots -Metrics $metrics -Policies $policies)
$ratchetedHotspots = @($hotspots | Where-Object Scope -CEQ $ratchetPolicy.Name)
$violations = @()
if ($UpdateBaseline) { Update-ComplexityBaseline -Path $baseline -Hotspots $ratchetedHotspots -Policy $ratchetPolicy }
else { $violations = @(Get-ComplexityViolations -Path $baseline -Hotspots $ratchetedHotspots -Policy $ratchetPolicy) }
Write-ComplexityEvidence -Directory $output -Metrics $metrics -Hotspots $hotspots -Violations $violations -Policies $policies
Write-Host "Complexity inventory generated for $($policies.Count) production scope(s): $output"
if ($violations.Count -ne 0) {
    $summary = @($violations | Select-Object -First 20 | ForEach-Object { "$($_.Kind): $($_.File):$($_.StartLine) $($_.Key) complexity=$($_.CyclomaticComplexity) CRAP=$($_.CrapScore)" }) -join [Environment]::NewLine
    throw "Complexity ratchet failed with $($violations.Count) violation(s):$([Environment]::NewLine)$summary"
}
Write-Host "Complexity ratchet passed with $($ratchetedHotspots.Count) approved supported-production hotspot(s); incubation scopes were inventoried as report-only."
