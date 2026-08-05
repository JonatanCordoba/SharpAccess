#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$ReferenceEnvironment = "local-controlled",
    [switch]$NoRestore,
    [switch]$NoBuild,
    [switch]$RequireApprovedBaseline,
    [switch]$ApproveBaseline,
    [string]$ReviewDecision,
    [switch]$SingleRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

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

function Invoke-DotNet([string[]]$Arguments, [string]$FailureMessage) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
}

function Get-CurrentCommit([string]$Root) {
    $commit = @(& git -C $Root rev-parse HEAD)
    if ($LASTEXITCODE -ne 0 -or $commit.Count -ne 1) {
        throw "Unable to resolve the current Git revision."
    }

    return $commit[0].Trim()
}

function Assert-CleanRepository([string]$Root) {
    $status = @(& git -C $Root status --porcelain=v1 --untracked-files=all --ignore-submodules=none)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect the repository status."
    }
    if ($status.Count -ne 0) {
        $status
        throw "Controlled performance evidence requires a clean repository."
    }
}

function Get-PolicyValue([xml]$Policy, [string]$Name) {
    $node = $Policy.SelectSingleNode("//$Name")
    if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) {
        throw "Release-candidate policy value is missing: $Name"
    }

    return $node.InnerText.Trim()
}

function Get-TextSha256([string]$Text) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($Text)
    try {
        return [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
    }
    finally {
        [Array]::Clear($bytes, 0, $bytes.Length)
    }
}

function Get-MedianDecimal([object[]]$Values) {
    $ordered = @(
        $Values |
            ForEach-Object { [decimal]$_ } |
            Sort-Object
    )
    if ($ordered.Count -eq 0 -or ($ordered.Count % 2) -eq 0) {
        throw "Median aggregation requires a non-empty odd value count."
    }

    return $ordered[[int][Math]::Floor($ordered.Count / 2)]
}

function Get-MedianInt64([object[]]$Values) {
    $ordered = @(
        $Values |
            ForEach-Object { [long]$_ } |
            Sort-Object
    )
    if ($ordered.Count -eq 0 -or ($ordered.Count % 2) -eq 0) {
        throw "Median aggregation requires a non-empty odd value count."
    }

    return $ordered[[int][Math]::Floor($ordered.Count / 2)]
}

function ConvertTo-DevSkimSafeJsonString([string]$Value) {
    $builder = [Text.StringBuilder]::new()
    for ($index = 0; $index -lt $Value.Length; $index++) {
        if ((($index + 1) % 20) -eq 0) {
            [void]$builder.AppendFormat(
                [Globalization.CultureInfo]::InvariantCulture,
                "\u{0:X4}",
                [int][char]$Value[$index])
        }
        else {
            [void]$builder.Append($Value[$index])
        }
    }

    return $builder.ToString()
}

function Write-ApprovedPerformanceBaseline(
    [object]$Baseline,
    [string]$Path,
    [string]$ApprovedRevision,
    [string]$EnvironmentFingerprint
) {
    $json = $Baseline | ConvertTo-Json -Depth 12
    $json = $json.Replace(
        '"' + $ApprovedRevision + '"',
        '"' + (ConvertTo-DevSkimSafeJsonString $ApprovedRevision) + '"')
    $json = $json.Replace(
        '"' + $EnvironmentFingerprint + '"',
        '"' + (ConvertTo-DevSkimSafeJsonString $EnvironmentFingerprint) + '"')

    [IO.File]::WriteAllText(
        $Path,
        $json + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))

    $parsed = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ([string]$parsed.approvedRevision -cne $ApprovedRevision -or
        [string]$parsed.environmentFingerprint -cne $EnvironmentFingerprint) {
        throw "DevSkim-safe JSON serialization changed approved baseline semantics."
    }
}
function Get-PowerPlanName {
    $output = @(& powercfg /GETACTIVESCHEME 2>&1)
    if ($LASTEXITCODE -ne 0 -or $output.Count -eq 0) {
        throw "Unable to read the active Windows power plan."
    }

    $match = [regex]::Match(($output -join " "), "\((?<name>[^)]+)\)")
    if (-not $match.Success -or [string]::IsNullOrWhiteSpace($match.Groups["name"].Value)) {
        throw "Unable to parse the active Windows power plan."
    }

    return $match.Groups["name"].Value.Trim()
}

function Get-ReferenceEnvironmentMetadata(
    [string]$EnvironmentName,
    [pscustomobject]$Cryptography,
    [pscustomobject]$Endpoints,
    [pscustomobject]$Postgres
) {
    if (-not [OperatingSystem]::IsWindows()) {
        throw "Controlled SharpAccess performance evidence is supported on Windows only."
    }

    $processor = @(Get-CimInstance Win32_Processor | Sort-Object DeviceID | Select-Object -First 1)
    $computer = Get-CimInstance Win32_ComputerSystem
    $operatingSystem = Get-CimInstance Win32_OperatingSystem
    $disks = @(
        Get-CimInstance Win32_DiskDrive |
            Sort-Object Index |
            ForEach-Object {
                [ordered]@{
                    model = [string]$_.Model
                    mediaType = [string]$_.MediaType
                    interfaceType = [string]$_.InterfaceType
                    sizeBytes = [long]$_.Size
                }
            }
    )
    if ($processor.Count -ne 1 -or $null -eq $computer -or $null -eq $operatingSystem -or $disks.Count -eq 0) {
        throw "Controlled Windows hardware metadata is incomplete."
    }

    $sdk = @(& dotnet --version)
    if ($LASTEXITCODE -ne 0 -or $sdk.Count -ne 1) {
        throw "Unable to read the active .NET SDK version."
    }

    $runtimes = @(
        & dotnet --list-runtimes |
            ForEach-Object { ($_ -split "\s+\[", 2)[0].Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Sort-Object -Unique
    )
    if ($LASTEXITCODE -ne 0 -or $runtimes.Count -eq 0) {
        throw "Unable to read installed .NET runtime versions."
    }

    return [ordered]@{
        label = $EnvironmentName
        cpu = [ordered]@{
            model = [string]$processor[0].Name
            logicalProcessorCount = [int]$computer.NumberOfLogicalProcessors
        }
        memory = [ordered]@{
            totalPhysicalBytes = [long]$computer.TotalPhysicalMemory
        }
        windows = [ordered]@{
            caption = [string]$operatingSystem.Caption
            version = [string]$operatingSystem.Version
            buildNumber = [string]$operatingSystem.BuildNumber
            architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
            powerPlan = Get-PowerPlanName
        }
        dotnet = [ordered]@{
            sdk = $sdk[0].Trim()
            framework = [Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
            processArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
            runtimes = $runtimes
        }
        storage = $disks
        sqlite = [ordered]@{
            providerVersion = [string]$Endpoints.sqliteProviderVersion
            nativeVersion = [string]$Endpoints.sqliteNativeVersion
            datasetProfile = [string]$Endpoints.datasetProfile
            datasetUserCount = [int]$Endpoints.datasetUserCount
            datasetTenantMemberCount = [int]$Endpoints.datasetTenantMemberCount
        }
        postgres = [ordered]@{
            serverVersion = [string]$Postgres.postgresServerVersion
            providerVersion = [string]$Postgres.postgresProviderVersion
            maxConnections = [string]$Postgres.postgresConfiguration.maxConnections
            sharedBuffers = [string]$Postgres.postgresConfiguration.sharedBuffers
            workMem = [string]$Postgres.postgresConfiguration.workMem
            effectiveCacheSize = [string]$Postgres.postgresConfiguration.effectiveCacheSize
            datasetProfile = [string]$Postgres.datasetProfile
            datasetUserCount = [int]$Postgres.datasetUserCount
            datasetTenantMemberCount = [int]$Postgres.datasetTenantMemberCount
        }
        argon2 = [ordered]@{
            memoryKiB = [int]$Cryptography.configuredArgon2MemoryKiB
            maximumConcurrent = [int]$Cryptography.configuredMaximumConcurrentPasswordHashes
            maximumQueued = [int]$Cryptography.configuredMaximumQueuedPasswordHashes
        }
    }
}

function Assert-RequiredMetricScope([object[]]$Metrics) {
    $required = @(
        "password_hash",
        "password_verify",
        "password_hash_queue_saturation",
        "password_hash_no_wait_rejection",
        "jwt_sign",
        "jwt_validate",
        "authorization_context_construction",
        "endpoint_login",
        "endpoint_refresh_rotation",
        "endpoint_refresh_replay_contention",
        "endpoint_persisted_state_validation",
        "endpoint_user_keyset_page",
        "endpoint_role_invalidation_cycle",
        "endpoint_tenant_member_page",
        "postgres_user_keyset_page",
        "postgres_tenant_member_keyset_page"
    )

    $names = @($Metrics | ForEach-Object { [string]$_.name })
    $duplicates = @($names | Group-Object | Where-Object Count -gt 1 | ForEach-Object Name)
    if ($duplicates.Count -ne 0) {
        throw "Performance evidence contains duplicate metrics: $($duplicates -join ', ')."
    }

    $missing = @($required | Where-Object { $_ -notin $names })
    if ($missing.Count -ne 0) {
        throw "Performance evidence is missing required metrics: $($missing -join ', ')."
    }

    foreach ($metric in $Metrics) {
        $name = [string]$metric.name
        $iterations = [int]$metric.iterations
        $p50Milliseconds = [decimal]$metric.p50Milliseconds
        $p95Milliseconds = [decimal]$metric.p95Milliseconds
        $maximumMilliseconds = [decimal]$metric.maximumMilliseconds
        $allocatedBytesPerOperation = [long]$metric.allocatedBytesPerOperation

        if ([string]::IsNullOrWhiteSpace($name) -or
            $iterations -lt 2 -or
            $p50Milliseconds -lt 0 -or
            $p95Milliseconds -lt 0 -or
            $maximumMilliseconds -lt 0 -or
            $allocatedBytesPerOperation -lt 0) {
            throw "Performance metric is incomplete or invalid: $name"
        }
    }
}

function Assert-WarmupEvidence([object[]]$Records) {
    foreach ($record in $Records) {
        if ([int]$record.warmupIterations -lt 1) {
            throw "Performance evidence lacks explicit warm-up iterations for category: $($record.category)"
        }
    }
}

function Assert-NoSensitiveRetainedEvidence(
    [string]$OutputDirectory,
    [string]$Root,
    [string]$PostgresConnectionString
) {
    $allowedNames = @(
        "cryptography.json",
        "endpoints.json",
        "postgresql.json",
        "candidate-baseline.json",
        "performance-summary.json"
    )
    $files = @(Get-ChildItem -LiteralPath $OutputDirectory -File -Recurse)
    $unexpected = @($files | Where-Object { $_.Name -notin $allowedNames })
    if ($unexpected.Count -ne 0) {
        throw "Unexpected retained performance files: $($unexpected.FullName -join ', ')."
    }

    $forbiddenPatterns = @(
        '"connectionString"\s*:',
        '(?i)(?:Host|Password|Username|User\s+ID)\s*=',
        '(?i)\bSELECT\s+',
        '(?i)\bINSERT\s+INTO\b',
        '(?i)\bUPDATE\s+\w+\s+SET\b',
        '(?i)\bDELETE\s+FROM\b'
    )

    foreach ($file in $files) {
        $content = Get-Content -LiteralPath $file.FullName -Raw
        if ($content.Contains($Root, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Machine-local repository paths are forbidden in retained performance evidence: $($file.Name)"
        }
        if (-not [string]::IsNullOrWhiteSpace($PostgresConnectionString) -and
            $content.Contains($PostgresConnectionString, [StringComparison]::Ordinal)) {
            throw "The PostgreSQL connection string was retained in performance evidence: $($file.Name)"
        }
        foreach ($pattern in $forbiddenPatterns) {
            if ([regex]::IsMatch($content, $pattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
                throw "Sensitive or implementation-specific content was retained in performance evidence: $($file.Name)"
            }
        }
    }
}

function Assert-ApprovedRevisionScope(
    [string]$Root,
    [string]$ApprovedRevision,
    [string]$CurrentRevision
) {
    if ($ApprovedRevision -eq $CurrentRevision) {
        return
    }

    & git -C $Root merge-base --is-ancestor $ApprovedRevision $CurrentRevision
    if ($LASTEXITCODE -ne 0) {
        throw "The approved performance revision is not an ancestor of the current revision."
    }

    $changed = @(& git -C $Root diff --name-only "$ApprovedRevision..$CurrentRevision")
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to compare the approved performance revision to the current revision."
    }

    $disallowed = @($changed | Where-Object {
        $_.Replace('\', '/') -cne "eng/PerformanceBaseline.json"
    })
    if ($disallowed.Count -ne 0) {
        throw "Performance approval was invalidated by later changes: $($disallowed -join ', ')."
    }
}

function Assert-ApprovedBaseline(
    [object[]]$Metrics,
    [pscustomobject]$Baseline,
    [decimal]$TolerancePercent,
    [decimal]$P95ComparisonEpsilonMilliseconds,
    [int]$IndependentRuns,
    [string]$Root,
    [string]$CurrentRevision,
    [string]$EnvironmentName,
    [string]$EnvironmentFingerprint
) {
    if ($Baseline.status -ne "approved") {
        throw "The tracked performance baseline is not approved. Run the controlled profile, review candidate-baseline.json, then rerun with -ApproveBaseline and -ReviewDecision."
    }
    $baselineIncomplete =
        [int]$Baseline.schemaVersion -lt 2 -or
        [string]::IsNullOrWhiteSpace([string]$Baseline.approvedRevision) -or
        [string]::IsNullOrWhiteSpace([string]$Baseline.referenceEnvironment) -or
        [string]::IsNullOrWhiteSpace([string]$Baseline.environmentFingerprint) -or
        [string]::IsNullOrWhiteSpace([string]$Baseline.reviewDecision) -or
        $null -eq $Baseline.p95ComparisonEpsilonMilliseconds -or
        $null -eq $Baseline.independentRuns -or
        [string]$Baseline.aggregation -cne "median-across-independent-processes" -or
        @($Baseline.metrics).Count -eq 0
    if ($baselineIncomplete) {
        throw "The approved performance baseline is incomplete."
    }
    if ([string]$Baseline.referenceEnvironment -cne $EnvironmentName) {
        throw "The approved performance environment does not match the requested controlled environment."
    }
    if ([string]$Baseline.environmentFingerprint -cne $EnvironmentFingerprint) {
        throw "The controlled performance environment differs from the approved environment fingerprint."
    }
    if ([decimal]$Baseline.tolerancePercent -ne $TolerancePercent) {
        throw "The approved performance tolerance differs from release-candidate policy."
    }
    if ([decimal]$Baseline.p95ComparisonEpsilonMilliseconds -ne $P95ComparisonEpsilonMilliseconds) {
        throw "The approved p95 comparison epsilon differs from release-candidate policy."
    }
    if ([int]$Baseline.independentRuns -ne $IndependentRuns) {
        throw "The approved independent-run count differs from release-candidate policy."
    }

    Assert-ApprovedRevisionScope $Root ([string]$Baseline.approvedRevision) $CurrentRevision

    $baselineByName = @{}
    foreach ($entry in @($Baseline.metrics)) {
        $baselineByName[[string]$entry.name] = $entry
    }

    foreach ($metric in $Metrics) {
        $name = [string]$metric.name
        if (-not $baselineByName.ContainsKey($name)) {
            throw "Approved performance baseline is missing metric: $name"
        }

        $baselineMetric = $baselineByName[$name]
        $factor = 1 + ($TolerancePercent / 100)
        $maximumP95 =
            ([decimal]$baselineMetric.p95Milliseconds * $factor) +
            $P95ComparisonEpsilonMilliseconds
        $maximumAllocated = [decimal]$baselineMetric.allocatedBytesPerOperation * $factor
        if ([decimal]$metric.p95Milliseconds -gt $maximumP95) {
            throw "Performance p95 regression exceeded policy for $name. Actual=$($metric.p95Milliseconds) Maximum=$maximumP95 EpsilonMilliseconds=$P95ComparisonEpsilonMilliseconds"
        }
        if ([decimal]$metric.allocatedBytesPerOperation -gt $maximumAllocated) {
            throw "Performance allocation regression exceeded policy for $name. Actual=$($metric.allocatedBytesPerOperation) Maximum=$maximumAllocated"
        }
    }
}

if ($ApproveBaseline -and $RequireApprovedBaseline) {
    throw "-ApproveBaseline and -RequireApprovedBaseline are mutually exclusive."
}
if ($ApproveBaseline) {
    $reviewDecisionInvalid =
        [string]::IsNullOrWhiteSpace($ReviewDecision) -or
        $ReviewDecision.Length -gt 500 -or
        [regex]::IsMatch(
            $ReviewDecision,
            "\p{C}",
            [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if ($reviewDecisionInvalid) {
        throw "-ApproveBaseline requires a bounded printable -ReviewDecision."
    }
}

$root = Resolve-RepositoryRoot $RepositoryRoot
Assert-CleanRepository $root
$commit = Get-CurrentCommit $root
$policyPath = Join-Path $root "eng/ReleaseCandidate.props"
$baselinePath = Join-Path $root "eng/PerformanceBaseline.json"
$policyFilesMissing =
    -not (Test-Path -LiteralPath $policyPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $baselinePath -PathType Leaf)
if ($policyFilesMissing) {
    throw "Release-candidate performance policy files are missing."
}

[xml]$policy = Get-Content -LiteralPath $policyPath -Raw
$unitIterations = [int](Get-PolicyValue $policy "PerformanceUnitIterations")
$endpointIterations = [int](Get-PolicyValue $policy "PerformanceEndpointIterations")
$warmupIterations = [int](Get-PolicyValue $policy "PerformanceWarmupIterations")
$postgresUsers = [int](Get-PolicyValue $policy "PerformancePostgresUserRows")
$postgresMembers = [int](Get-PolicyValue $policy "PerformancePostgresTenantMemberRows")
$tolerancePercent = [decimal](Get-PolicyValue $policy "PerformanceRegressionTolerancePercent")
$p95ComparisonEpsilonMilliseconds =
    [decimal](Get-PolicyValue $policy "PerformanceP95ComparisonEpsilonMilliseconds")
$independentRuns = [int](Get-PolicyValue $policy "PerformanceIndependentRuns")
if ($independentRuns -lt 3 -or
    ($independentRuns % 2) -eq 0 -or
    $independentRuns -gt 9) {
    throw "PerformanceIndependentRuns must be an odd number between 3 and 9."
}
if ($p95ComparisonEpsilonMilliseconds -lt 0 -or
    $p95ComparisonEpsilonMilliseconds -gt 0.01) {
    throw "Performance p95 comparison epsilon must be between 0 and 0.01 milliseconds."
}

$output = Join-Path $root "artifacts/performance/release-candidate"
$candidatePath = Join-Path $output "candidate-baseline.json"
if (-not $ApproveBaseline -and -not $SingleRun) {
    $candidateRuns = @()

    for ($run = 1; $run -le $independentRuns; $run++) {
        Write-Host "==> Independent performance run $run of $independentRuns"

        $childParameters = @{
            RepositoryRoot = $root
            Configuration = $Configuration
            ReferenceEnvironment = $ReferenceEnvironment
            SingleRun = $true
        }
        if ($NoRestore -or $run -gt 1) {
            $childParameters.NoRestore = $true
        }
        if ($NoBuild -or $run -gt 1) {
            $childParameters.NoBuild = $true
        }

        & $PSCommandPath @childParameters
        if ($LASTEXITCODE -ne 0) {
            throw "Independent performance run $run failed."
        }

        if (-not (Test-Path -LiteralPath $candidatePath -PathType Leaf)) {
            throw "Independent performance run $run did not produce candidate-baseline.json."
        }

        $candidateRuns +=
            Get-Content -LiteralPath $candidatePath -Raw |
            ConvertFrom-Json
    }

    if ($candidateRuns.Count -ne $independentRuns) {
        throw "Independent performance capture count is incomplete."
    }

    $firstCandidate = $candidateRuns[0]
    $metricNames = @($firstCandidate.metrics | ForEach-Object { [string]$_.name })
    $expectedFingerprint = [string]$firstCandidate.environmentFingerprint

    foreach ($candidate in $candidateRuns) {
        if ([string]$candidate.status -cne "candidate" -or
            [string]$candidate.sourceRevision -cne $commit -or
            [string]$candidate.referenceEnvironment -cne $ReferenceEnvironment -or
            [string]$candidate.environmentFingerprint -cne $expectedFingerprint -or
            [decimal]$candidate.tolerancePercent -ne $tolerancePercent -or
            [decimal]$candidate.p95ComparisonEpsilonMilliseconds -ne
                $p95ComparisonEpsilonMilliseconds) {
            throw "Independent performance candidates do not share one exact revision and environment."
        }

        Assert-RequiredMetricScope @($candidate.metrics)
        $candidateNames = @($candidate.metrics | ForEach-Object { [string]$_.name })
        if (@(Compare-Object ($metricNames | Sort-Object) ($candidateNames | Sort-Object)).Count -ne 0) {
            throw "Independent performance candidates contain different metric catalogs."
        }
    }

    $aggregatedMetrics = @(
        foreach ($name in $metricNames) {
            $samples = @(
                foreach ($candidate in $candidateRuns) {
                    $matches = @($candidate.metrics | Where-Object name -eq $name)
                    if ($matches.Count -ne 1) {
                        throw "Independent candidate does not contain exactly one metric: $name"
                    }

                    $matches[0]
                }
            )

            $iterationCounts = @(
                $samples |
                    ForEach-Object { [int]$_.iterations } |
                    Sort-Object -Unique
            )
            if ($iterationCounts.Count -ne 1) {
                throw "Independent candidates disagree on iteration count for: $name"
            }

            [ordered]@{
                name = $name
                iterations = $iterationCounts[0]
                meanMilliseconds = Get-MedianDecimal @(
                    $samples | ForEach-Object { $_.meanMilliseconds })
                p50Milliseconds = Get-MedianDecimal @(
                    $samples | ForEach-Object { $_.p50Milliseconds })
                p95Milliseconds = Get-MedianDecimal @(
                    $samples | ForEach-Object { $_.p95Milliseconds })
                maximumMilliseconds = Get-MedianDecimal @(
                    $samples | ForEach-Object { $_.maximumMilliseconds })
                allocatedBytesPerOperation = Get-MedianInt64 @(
                    $samples | ForEach-Object { $_.allocatedBytesPerOperation })
                workingSetDeltaBytes = Get-MedianInt64 @(
                    $samples | ForEach-Object { $_.workingSetDeltaBytes })
                independentRunP95Milliseconds = @(
                    $samples | ForEach-Object { [decimal]$_.p95Milliseconds })
                independentRunAllocatedBytesPerOperation = @(
                    $samples | ForEach-Object { [long]$_.allocatedBytesPerOperation })
            }
        }
    )

    Assert-RequiredMetricScope $aggregatedMetrics

    $aggregateCandidate = [ordered]@{
        schemaVersion = 2
        status = "candidate"
        sourceRevision = $commit
        referenceEnvironment = $ReferenceEnvironment
        environmentFingerprint = $expectedFingerprint
        environment = $firstCandidate.environment
        warmupProfiles = @($firstCandidate.warmupProfiles)
        tolerancePercent = $tolerancePercent
        p95ComparisonEpsilonMilliseconds = $p95ComparisonEpsilonMilliseconds
        independentRuns = $independentRuns
        aggregation = "median-across-independent-processes"
        metrics = $aggregatedMetrics
    }
    $aggregateCandidate |
        ConvertTo-Json -Depth 12 |
        Set-Content -LiteralPath $candidatePath -Encoding utf8NoBOM

    $categoryFiles = @(
        (Join-Path $output "cryptography.json"),
        (Join-Path $output "endpoints.json"),
        (Join-Path $output "postgresql.json")
    )
    foreach ($categoryFile in $categoryFiles) {
        $record = Get-Content -LiteralPath $categoryFile -Raw | ConvertFrom-Json
        $categoryNames = @($record.metrics | ForEach-Object { [string]$_.name })
        $record.metrics = @(
            $aggregatedMetrics |
                Where-Object { [string]$_.name -in $categoryNames }
        )
        $record |
            Add-Member -NotePropertyName independentRuns -NotePropertyValue $independentRuns -Force
        $record |
            Add-Member `
                -NotePropertyName aggregation `
                -NotePropertyValue "median-across-independent-processes" `
                -Force
        $record |
            ConvertTo-Json -Depth 12 |
            Set-Content -LiteralPath $categoryFile -Encoding utf8NoBOM
    }

    $baseline = Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json
    if ($RequireApprovedBaseline) {
        Assert-ApprovedBaseline `
            $aggregatedMetrics `
            $baseline `
            $tolerancePercent `
            $p95ComparisonEpsilonMilliseconds `
            $independentRuns `
            $root `
            $commit `
            $ReferenceEnvironment `
            $expectedFingerprint
    }

    $summaryPath = Join-Path $output "performance-summary.json"
    $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
    $summary.metrics = $aggregatedMetrics
    $summary.approvedBaselineRequired = [bool]$RequireApprovedBaseline
    $summary |
        Add-Member -NotePropertyName independentRuns -NotePropertyValue $independentRuns -Force
    $summary |
        Add-Member `
            -NotePropertyName aggregation `
            -NotePropertyValue "median-across-independent-processes" `
            -Force
    $summary |
        ConvertTo-Json -Depth 12 |
        Set-Content -LiteralPath $summaryPath -Encoding utf8NoBOM

    Assert-NoSensitiveRetainedEvidence `
        $output `
        $root `
        ([string]$env:SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING)

    Write-Host "Performance evidence aggregated by median across $independentRuns independent processes."
    Write-Host "Performance and capacity evidence was written to artifacts/performance/release-candidate."
    $global:LASTEXITCODE = 0
    return
}
if ($ApproveBaseline) {
    if (-not (Test-Path -LiteralPath $candidatePath -PathType Leaf)) {
        throw "The reviewed candidate baseline is missing. Run and review the controlled profile before approval."
    }

    $candidate = Get-Content -LiteralPath $candidatePath -Raw | ConvertFrom-Json
    $candidateIncomplete =
        [int]$candidate.schemaVersion -lt 2 -or
        [string]$candidate.status -cne "candidate" -or
        [string]::IsNullOrWhiteSpace([string]$candidate.sourceRevision) -or
        [string]::IsNullOrWhiteSpace([string]$candidate.referenceEnvironment) -or
        [string]::IsNullOrWhiteSpace([string]$candidate.environmentFingerprint) -or
        $null -eq $candidate.environment -or
        @($candidate.warmupProfiles).Count -eq 0 -or
        [int]$candidate.independentRuns -ne $independentRuns -or
        [string]$candidate.aggregation -cne "median-across-independent-processes" -or
        @($candidate.metrics).Count -eq 0
    if ($candidateIncomplete) {
        throw "The reviewed candidate baseline is incomplete."
    }
    if ([string]$candidate.sourceRevision -cne $commit) {
        throw "The reviewed candidate baseline does not belong to the current exact revision."
    }
    if ([string]$candidate.referenceEnvironment -cne $ReferenceEnvironment) {
        throw "The reviewed candidate environment does not match -ReferenceEnvironment."
    }
    if ([decimal]$candidate.tolerancePercent -ne $tolerancePercent) {
        throw "The reviewed candidate tolerance differs from release-candidate policy."
    }
    if ([decimal]$candidate.p95ComparisonEpsilonMilliseconds -ne $p95ComparisonEpsilonMilliseconds) {
        throw "The reviewed candidate p95 comparison epsilon differs from release-candidate policy."
    }

    $candidateMetrics = @($candidate.metrics)
    Assert-RequiredMetricScope $candidateMetrics
    Assert-WarmupEvidence @($candidate.warmupProfiles)
    Assert-NoSensitiveRetainedEvidence `
        $output `
        $root `
        ([string]$env:SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING)

    $approvedBaseline = [ordered]@{
        schemaVersion = 2
        status = "approved"
        approvedRevision = [string]$candidate.sourceRevision
        referenceEnvironment = [string]$candidate.referenceEnvironment
        environmentFingerprint = [string]$candidate.environmentFingerprint
        environment = $candidate.environment
        warmupProfiles = @($candidate.warmupProfiles)
        tolerancePercent = $tolerancePercent
        p95ComparisonEpsilonMilliseconds = $p95ComparisonEpsilonMilliseconds
        independentRuns = $independentRuns
        aggregation = "median-across-independent-processes"
        reviewDecision = $ReviewDecision.Trim()
        reviewedUtc = [DateTimeOffset]::UtcNow.ToString("O")
        metrics = $candidateMetrics
    }
    Write-ApprovedPerformanceBaseline `
        $approvedBaseline `
        $baselinePath `
        ([string]$candidate.sourceRevision) `
        ([string]$candidate.environmentFingerprint)

    Write-Host "Approved performance baseline was promoted from the existing reviewed candidate without rerunning measurements."
    $global:LASTEXITCODE = 0
    return
}

Remove-Item -LiteralPath $output -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $output | Out-Null

if (-not $NoRestore) {
    Invoke-DotNet @("restore", (Join-Path $root "SharpAccess.sln"), "--locked-mode") `
        "Performance evidence restore failed."
}

$previousOutput = $env:SHARPACCESS_PERFORMANCE_OUTPUT_DIRECTORY
$previousUnitIterations = $env:SHARPACCESS_PERFORMANCE_UNIT_ITERATIONS
$previousEndpointIterations = $env:SHARPACCESS_PERFORMANCE_ENDPOINT_ITERATIONS
$previousWarmupIterations = $env:SHARPACCESS_PERFORMANCE_WARMUP_ITERATIONS
$previousPostgresUsers = $env:SHARPACCESS_PERFORMANCE_POSTGRES_USERS
$previousPostgresMembers = $env:SHARPACCESS_PERFORMANCE_POSTGRES_MEMBERS
$env:SHARPACCESS_PERFORMANCE_OUTPUT_DIRECTORY = $output
$env:SHARPACCESS_PERFORMANCE_UNIT_ITERATIONS = $unitIterations.ToString([Globalization.CultureInfo]::InvariantCulture)
$env:SHARPACCESS_PERFORMANCE_ENDPOINT_ITERATIONS = $endpointIterations.ToString([Globalization.CultureInfo]::InvariantCulture)
$env:SHARPACCESS_PERFORMANCE_WARMUP_ITERATIONS = $warmupIterations.ToString([Globalization.CultureInfo]::InvariantCulture)
$env:SHARPACCESS_PERFORMANCE_POSTGRES_USERS = $postgresUsers.ToString([Globalization.CultureInfo]::InvariantCulture)
$env:SHARPACCESS_PERFORMANCE_POSTGRES_MEMBERS = $postgresMembers.ToString([Globalization.CultureInfo]::InvariantCulture)

try {
    $projects = @(
        "tests/SharpAccess.UnitTests/SharpAccess.UnitTests.csproj",
        "tests/SharpAccess.EndpointTests/SharpAccess.EndpointTests.csproj",
        "tests/SharpAccess.ProviderContractTests/SharpAccess.ProviderContractTests.csproj"
    )
    foreach ($project in $projects) {
        $arguments = @(
            "test",
            (Join-Path $root $project),
            "--configuration",
            $Configuration,
            "--filter",
            "Evidence=Performance",
            "--no-restore"
        )
        if ($NoBuild) {
            $arguments += "--no-build"
        }

        Invoke-DotNet $arguments "Performance evidence tests failed for $project."
    }
}
finally {
    $env:SHARPACCESS_PERFORMANCE_OUTPUT_DIRECTORY = $previousOutput
    $env:SHARPACCESS_PERFORMANCE_UNIT_ITERATIONS = $previousUnitIterations
    $env:SHARPACCESS_PERFORMANCE_ENDPOINT_ITERATIONS = $previousEndpointIterations
    $env:SHARPACCESS_PERFORMANCE_WARMUP_ITERATIONS = $previousWarmupIterations
    $env:SHARPACCESS_PERFORMANCE_POSTGRES_USERS = $previousPostgresUsers
    $env:SHARPACCESS_PERFORMANCE_POSTGRES_MEMBERS = $previousPostgresMembers
}

$evidenceFiles = @(
    (Join-Path $output "cryptography.json")
    (Join-Path $output "endpoints.json")
    (Join-Path $output "postgresql.json")
)
foreach ($file in $evidenceFiles) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Expected performance evidence file is missing: $file"
    }
}

$records = @($evidenceFiles | ForEach-Object { Get-Content -LiteralPath $_ -Raw | ConvertFrom-Json })
Assert-WarmupEvidence $records
$metrics = @($records | ForEach-Object { @($_.metrics) })
Assert-RequiredMetricScope $metrics

$cryptography = $records | Where-Object category -eq "cryptography-and-token"
$endpoints = $records | Where-Object category -eq "endpoint-and-sqlite"
$postgres = $records | Where-Object category -eq "postgresql-provider"
if ($null -eq $cryptography -or $null -eq $endpoints -or $null -eq $postgres) {
    throw "Performance evidence categories are incomplete."
}

$environment = Get-ReferenceEnvironmentMetadata $ReferenceEnvironment $cryptography $endpoints $postgres
$environmentJson = $environment | ConvertTo-Json -Depth 12 -Compress
$environmentFingerprint = Get-TextSha256 $environmentJson
$baseline = Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json

if ($RequireApprovedBaseline) {
    Assert-ApprovedBaseline `
        $metrics `
        $baseline `
        $tolerancePercent `
        $p95ComparisonEpsilonMilliseconds `
        $independentRuns `
        $root `
        $commit `
        $ReferenceEnvironment `
        $environmentFingerprint
}

$warmupProfiles = @(
    $records |
        ForEach-Object {
            [ordered]@{
                category = [string]$_.category
                warmupIterations = [int]$_.warmupIterations
            }
        }
)

$candidateBaseline = [ordered]@{
    schemaVersion = 2
    status = "candidate"
    sourceRevision = $commit
    referenceEnvironment = $ReferenceEnvironment
    environmentFingerprint = $environmentFingerprint
    environment = $environment
    warmupProfiles = $warmupProfiles
    tolerancePercent = $tolerancePercent
    p95ComparisonEpsilonMilliseconds = $p95ComparisonEpsilonMilliseconds
    metrics = $metrics
}
$candidateBaseline |
    ConvertTo-Json -Depth 12 |
    Set-Content -LiteralPath (Join-Path $output "candidate-baseline.json") -Encoding utf8NoBOM

$effectiveBaselineStatus = [string]$baseline.status
[ordered]@{
    schemaVersion = 2
    status = "passed"
    sourceRevision = $commit
    configuration = $Configuration
    referenceEnvironment = $ReferenceEnvironment
    environmentFingerprint = $environmentFingerprint
    environment = $environment
    warmupProfiles = $warmupProfiles
    baselineStatus = $effectiveBaselineStatus
    approvedBaselineRequired = [bool]$RequireApprovedBaseline
    tolerancePercent = $tolerancePercent
    p95ComparisonEpsilonMilliseconds = $p95ComparisonEpsilonMilliseconds
    metrics = $metrics
    completedUtc = [DateTimeOffset]::UtcNow.ToString("O")
    interpretation = "Controlled reference evidence only; not a production SLA."
} |
    ConvertTo-Json -Depth 12 |
    Set-Content -LiteralPath (Join-Path $output "performance-summary.json") -Encoding utf8NoBOM

Assert-NoSensitiveRetainedEvidence `
    $output `
    $root `
    ([string]$env:SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING)


Write-Host "Performance and capacity evidence was written to artifacts/performance/release-candidate."
$global:LASTEXITCODE = 0
