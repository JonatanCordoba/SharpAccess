#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [ValidateSet('PullRequest', 'Weekly', 'ProviderPromotion', 'Release')]
    [string]$Tier = 'PullRequest',
    [string]$BaseRef = 'HEAD^',
    [string]$CatalogPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Resolves and validates the repository root.
function Resolve-RepositoryRoot([string]$Candidate) {
    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        $Candidate = Join-Path $PSScriptRoot '..'
    }

    $resolved = (Resolve-Path -LiteralPath $Candidate).Path
    if (-not (Test-Path -LiteralPath (Join-Path $resolved 'SharpAccess.sln') -PathType Leaf)) {
        throw 'Repository root is invalid.'
    }

    return $resolved
}

# Runs a native process without invoking a command shell.
function Invoke-NativeProcess(
    [string]$FileName,
    [string[]]$Arguments,
    [string]$WorkingDirectory) {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true

    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "Failed to start native process: $FileName"
        }

        $standardOutputTask = $process.StandardOutput.ReadToEndAsync()
        $standardErrorTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()

        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            StandardOutput = $standardOutputTask.GetAwaiter().GetResult()
            StandardError = $standardErrorTask.GetAwaiter().GetResult()
        }
    }
    finally {
        $process.Dispose()
    }
}

# Runs Git and converts native failures into terminating errors.
function Invoke-Git([string]$Root, [string[]]$Arguments) {
    $result = Invoke-NativeProcess -FileName 'git' -Arguments (@('-C', $Root) + $Arguments) -WorkingDirectory $Root
    if ($result.ExitCode -ne 0) {
        throw "git failed: $($result.StandardError)"
    }

    return $result.StandardOutput
}

# Computes a stable fingerprint for tracked changes and active untracked files.
function Get-RepositoryState([string]$Root) {
    $status = Invoke-Git $Root @('status', '--porcelain=v1', '--untracked-files=all')
    $diff = Invoke-Git $Root @('diff', '--binary', 'HEAD', '--')
    $untracked = @(
        (Invoke-Git $Root @('ls-files', '--others', '--exclude-standard')) -split '\r?\n' |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Sort-Object
    )
    $builder = [Text.StringBuilder]::new()
    [void]$builder.AppendLine($status)
    [void]$builder.AppendLine($diff)
    foreach ($relativePath in $untracked) {
        $path = Join-Path $Root $relativePath
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
            [void]$builder.AppendLine("$relativePath|$hash")
        }
    }

    $bytes = [Text.Encoding]::UTF8.GetBytes($builder.ToString())
    $fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
    return [pscustomobject]@{
        Dirty = -not [string]::IsNullOrWhiteSpace($status)
        Status = $status.TrimEnd()
        Fingerprint = $fingerprint
    }
}

# Removes abandoned mutation snapshots without touching a live process snapshot.
function Remove-StaleMutationSnapshots([string]$TemporaryRoot) {
    foreach ($directory in @(Get-ChildItem -LiteralPath $TemporaryRoot -Directory -Filter 'sharpaccess-mutation-*' -ErrorAction SilentlyContinue)) {
        if ($directory.LastWriteTimeUtc -lt [DateTime]::UtcNow.AddDays(-1)) {
            Remove-Item -LiteralPath $directory.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

# Copies exactly the active tracked and non-ignored untracked files into an isolated tree.
function Copy-RepositorySnapshot([string]$Root, [string]$Destination) {
    New-Item -ItemType Directory -Path $Destination | Out-Null
    $files = @(
        (Invoke-Git $Root @('ls-files', '--cached', '--others', '--exclude-standard')) -split '\r?\n' |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Sort-Object -Unique
    )
    foreach ($relativePath in $files) {
        $source = Join-Path $Root $relativePath
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            continue
        }

        $destinationPath = Join-Path $Destination $relativePath
        $destinationDirectory = Split-Path -Parent $destinationPath
        New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
        Copy-Item -LiteralPath $source -Destination $destinationPath
    }
}

# Resolves a catalog path within the isolated tree and rejects path traversal.
function Resolve-IsolatedPath([string]$IsolationRoot, [string]$RelativePath) {
    if ([IO.Path]::IsPathRooted($RelativePath)) {
        throw "Mutation catalog paths must be repository-relative: $RelativePath"
    }

    $rootWithSeparator = [IO.Path]::GetFullPath($IsolationRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $resolved = [IO.Path]::GetFullPath((Join-Path $IsolationRoot $RelativePath))
    if (-not $resolved.StartsWith($rootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Mutation catalog path escapes the isolated tree: $RelativePath"
    }

    return $resolved
}

# Reads one integer TRX counter attribute.
function Read-IntegerAttribute([System.Xml.XmlNode]$Node, [string]$Name) {
    $attribute = $Node.Attributes.GetNamedItem($Name)
    if ($null -eq $attribute -or [string]::IsNullOrWhiteSpace($attribute.Value)) {
        return 0
    }

    return [int]::Parse($attribute.Value, [Globalization.CultureInfo]::InvariantCulture)
}

# Reads the aggregate counters from one TRX result.
function Read-TrxSummary([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    [xml]$document = Get-Content -LiteralPath $Path -Raw
    $counters = $document.SelectSingleNode(
        "/*[local-name()='TestRun']" +
        "/*[local-name()='ResultSummary']" +
        "/*[local-name()='Counters']")
    if ($null -eq $counters) {
        return $null
    }

    return [pscustomobject]@{
        Total = Read-IntegerAttribute $counters 'total'
        Executed = Read-IntegerAttribute $counters 'executed'
        Passed = Read-IntegerAttribute $counters 'passed'
        Failed = Read-IntegerAttribute $counters 'failed'
        Error = Read-IntegerAttribute $counters 'error'
        Timeout = Read-IntegerAttribute $counters 'timeout'
        Aborted = Read-IntegerAttribute $counters 'aborted'
        NotExecuted = Read-IntegerAttribute $counters 'notExecuted'
    }
}

# Executes one stable mutation-invariant test category.
function Invoke-TargetedTest(
    [string]$Filter,
    [string]$ResultsDirectory,
    [string]$RunName,
    [string]$TestProject,
    [string]$IsolationRoot) {
    New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null
    $trxName = "$RunName.trx"
    $trxPath = Join-Path $ResultsDirectory $trxName
    $stdoutPath = Join-Path $ResultsDirectory "$RunName.stdout.log"
    $stderrPath = Join-Path $ResultsDirectory "$RunName.stderr.log"
    Remove-Item -LiteralPath $trxPath,$stdoutPath,$stderrPath -Force -ErrorAction SilentlyContinue

    $arguments = @(
        'test'
        $TestProject
        '--configuration'
        'Release'
        '--no-restore'
        '-warnaserror'
        '--filter'
        $Filter
        '--logger'
        "trx;LogFileName=$trxName"
        '--results-directory'
        $ResultsDirectory
    )
    $processResult = Invoke-NativeProcess -FileName 'dotnet' -Arguments $arguments -WorkingDirectory $IsolationRoot
    [IO.File]::WriteAllText($stdoutPath, $processResult.StandardOutput, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($stderrPath, $processResult.StandardError, [Text.UTF8Encoding]::new($false))

    return [pscustomobject]@{
        ExitCode = $processResult.ExitCode
        TrxPath = $trxPath
        StandardOutputPath = $stdoutPath
        StandardErrorPath = $stderrPath
        Summary = Read-TrxSummary $trxPath
    }
}

# Returns a baseline failure description or null for a passing non-empty run.
function Test-BaselineRun([object]$Run) {
    if ($null -eq $Run.Summary) { return 'The baseline test did not produce a readable TRX summary.' }
    if ($Run.Summary.Total -le 0 -or $Run.Summary.Executed -le 0) { return 'The baseline filter executed no tests.' }
    if ($Run.ExitCode -ne 0 -or $Run.Summary.Failed -gt 0) { return 'The unmutated baseline test did not pass.' }
    return $null
}

# Classifies one mutated test execution.
function Get-MutationOutcome([object]$Run) {
    if ($null -eq $Run.Summary) {
        return [pscustomobject]@{ Status = 'InfrastructureFailure'; Reason = 'The mutated test run did not produce a readable TRX summary.' }
    }
    if ($Run.Summary.Total -le 0 -or $Run.Summary.Executed -le 0) {
        return [pscustomobject]@{ Status = 'InfrastructureFailure'; Reason = 'The mutation filter executed no tests.' }
    }
    if ($Run.Summary.Failed -gt 0) {
        return [pscustomobject]@{ Status = 'Killed'; Reason = 'The invariant test failed while the mutation was active.' }
    }
    if ($Run.ExitCode -eq 0) {
        return [pscustomobject]@{ Status = 'Survived'; Reason = 'The invariant test passed while the mutation was active.' }
    }
    return [pscustomobject]@{ Status = 'InfrastructureFailure'; Reason = 'The mutated test command failed without a failing test result.' }
}

# Converts absolute run paths into repository artifact-relative evidence paths.
function Convert-RunEvidence([object]$Run, [string]$Root) {
    if ($null -eq $Run) { return $null }
    return [pscustomobject]@{
        exitCode = $Run.ExitCode
        trxPath = [IO.Path]::GetRelativePath($Root, $Run.TrxPath).Replace('\', '/')
        standardOutputPath = [IO.Path]::GetRelativePath($Root, $Run.StandardOutputPath).Replace('\', '/')
        standardErrorPath = [IO.Path]::GetRelativePath($Root, $Run.StandardErrorPath).Replace('\', '/')
        summary = $Run.Summary
    }
}

# Writes native process logs and fails when the process did not succeed.
function Save-ProcessEvidence([object]$Run, [string]$Directory, [string]$Name, [string]$Failure) {
    New-Item -ItemType Directory -Force -Path $Directory | Out-Null
    [IO.File]::WriteAllText((Join-Path $Directory "$Name.stdout.log"), $Run.StandardOutput, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $Directory "$Name.stderr.log"), $Run.StandardError, [Text.UTF8Encoding]::new($false))
    if ($Run.ExitCode -ne 0) { throw $Failure }
}

$root = Resolve-RepositoryRoot $RepositoryRoot
$catalogSource = if ([string]::IsNullOrWhiteSpace($CatalogPath)) {
    Join-Path $root 'eng/mutations.json'
}
else {
    (Resolve-Path -LiteralPath $CatalogPath).Path
}
$catalog = Get-Content -LiteralPath $catalogSource -Raw | ConvertFrom-Json -Depth 20
if ([int]$catalog.version -ne 2) { throw 'Mutation catalog version 2 is required.' }

$selected = @($catalog.mutations | Where-Object { @($_.tier) -contains $Tier })
if ($Tier -eq 'PullRequest') {
    $changed = @(
        (Invoke-Git $root @('diff', '--name-only', $BaseRef, '--', 'src', 'providers')) -split '\r?\n' |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    $matched = @($selected | Where-Object { $changed -contains [string]$_.path })
    if ($matched.Count -gt 0) {
        $selected = $matched
    }
}

$tierName = $Tier.ToLowerInvariant()
$artifactRoot = Join-Path $root 'artifacts/mutation'
$runRoot = Join-Path $artifactRoot "runs/$tierName"
$evidencePath = Join-Path $artifactRoot "$tierName.json"
if (Test-Path -LiteralPath $runRoot) {
    Remove-Item -LiteralPath $runRoot -Recurse -Force -ErrorAction Stop
}
if (Test-Path -LiteralPath $runRoot) { throw "Unable to clear prior mutation evidence: $runRoot" }
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
$revision = (Invoke-Git $root @('rev-parse', 'HEAD')).Trim()
$catalogHash = (Get-FileHash -LiteralPath $catalogSource -Algorithm SHA256).Hash.ToLowerInvariant()
$beforeState = Get-RepositoryState $root
$startedUtc = [DateTimeOffset]::UtcNow
$temporaryRoot = [IO.Path]::GetTempPath()
Remove-StaleMutationSnapshots $temporaryRoot
$isolationRoot = Join-Path $temporaryRoot "sharpaccess-mutation-$PID-$([Guid]::NewGuid().ToString('N'))"
$baselines = [Collections.Generic.List[object]]::new()
$outcomes = [Collections.Generic.List[object]]::new()
$failure = $null
$afterState = $null

try {
    Copy-RepositorySnapshot $root $isolationRoot
    $infrastructureDirectory = Join-Path $runRoot '_isolation'
    $restoreRun = Invoke-NativeProcess -FileName 'dotnet' -Arguments @('restore', 'SharpAccess.sln', '--locked-mode') -WorkingDirectory $isolationRoot
    Save-ProcessEvidence $restoreRun $infrastructureDirectory 'restore' 'The isolated locked restore failed.'

    $baselineKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($mutation in $selected) {
        $project = [string]$mutation.testProject
        $invariant = [string]$mutation.invariant
        if ([string]::IsNullOrWhiteSpace($project) -or [string]::IsNullOrWhiteSpace($invariant)) {
            throw "Mutation $($mutation.id) is missing testProject or invariant."
        }

        $key = "$project|$invariant"
        if (-not $baselineKeys.Add($key)) { continue }
        $filter = "MutationInvariant=$invariant"
        $baselineDirectory = Join-Path $runRoot "_baselines/$invariant"
        $baselineRun = Invoke-TargetedTest -Filter $filter -ResultsDirectory $baselineDirectory -RunName 'baseline' -TestProject (Resolve-IsolatedPath $isolationRoot $project) -IsolationRoot $isolationRoot
        $baselineFailure = Test-BaselineRun $baselineRun
        $baselines.Add([pscustomobject]@{
            invariant = $invariant
            testProject = $project
            filter = $filter
            passed = $null -eq $baselineFailure
            reason = $baselineFailure
            run = Convert-RunEvidence $baselineRun $root
        })
        if ($null -ne $baselineFailure) { throw "$invariant baseline failed: $baselineFailure" }
    }

    foreach ($mutation in $selected) {
        $id = [string]$mutation.id
        $relativeSource = [string]$mutation.path
        $sourcePath = Resolve-IsolatedPath $isolationRoot $relativeSource
        $project = [string]$mutation.testProject
        $invariant = [string]$mutation.invariant
        $filter = "MutationInvariant=$invariant"
        $mutationRunRoot = Join-Path $runRoot $id
        New-Item -ItemType Directory -Force -Path $mutationRunRoot | Out-Null
        $status = 'InfrastructureFailure'
        $reason = $null
        $mutatedRun = $null
        $original = $null
        $sourceWasMutated = $false

        try {
            $original = Get-Content -LiteralPath $sourcePath -Raw
            $normalized = $original.Replace("`r`n", "`n")
            $old = ([string]$mutation.oldText).Replace("`r`n", "`n")
            $new = ([string]$mutation.newText).Replace("`r`n", "`n")
            $anchorCount = [regex]::Matches($normalized, [regex]::Escape($old)).Count
            if ($anchorCount -ne 1) { throw "Mutation anchor count is $anchorCount; expected exactly one." }

            [IO.File]::WriteAllText($sourcePath, $normalized.Replace($old, $new), [Text.UTF8Encoding]::new($false))
            $sourceWasMutated = $true
            $mutatedRun = Invoke-TargetedTest -Filter $filter -ResultsDirectory $mutationRunRoot -RunName 'mutated' -TestProject (Resolve-IsolatedPath $isolationRoot $project) -IsolationRoot $isolationRoot
            $outcome = Get-MutationOutcome $mutatedRun
            $status = $outcome.Status
            $reason = $outcome.Reason
        }
        catch {
            $status = 'InfrastructureFailure'
            $reason = $_.Exception.Message
        }
        finally {
            if ($sourceWasMutated -and $null -ne $original) {
                [IO.File]::WriteAllText($sourcePath, $original, [Text.UTF8Encoding]::new($false))
            }
        }

        $outcomes.Add([pscustomobject]@{
            id = $id
            tier = $Tier
            critical = [bool]$mutation.critical
            path = $relativeSource
            invariant = $invariant
            testProject = $project
            filter = $filter
            status = $status
            reason = $reason
            mutated = Convert-RunEvidence $mutatedRun $root
        })
        Write-Host "${id}: $status"
    }

    foreach ($project in @($selected.testProject | Sort-Object -Unique)) {
        $projectName = [IO.Path]::GetFileNameWithoutExtension([string]$project)
        $cleanupRun = Invoke-NativeProcess -FileName 'dotnet' -Arguments @('build', (Resolve-IsolatedPath $isolationRoot ([string]$project)), '--configuration', 'Release', '--no-restore', '-warnaserror') -WorkingDirectory $isolationRoot
        Save-ProcessEvidence $cleanupRun (Join-Path $runRoot '_cleanup') $projectName "Mutation cleanup build failed for $project."
    }
}
catch {
    $failure = $_.Exception.Message
}
finally {
    if (Test-Path -LiteralPath $isolationRoot -PathType Container) {
        try {
            Remove-Item -LiteralPath $isolationRoot -Recurse -Force -ErrorAction Stop
        }
        catch {
            $failure = "Mutation isolation cleanup failed: $($_.Exception.Message)"
        }
    }
    if (Test-Path -LiteralPath $isolationRoot -PathType Container) {
        $failure = 'Mutation isolation cleanup left the temporary tree on disk.'
    }
    $afterState = Get-RepositoryState $root
    if ($beforeState.Fingerprint -ne $afterState.Fingerprint) {
        $failure = 'The primary working tree changed during isolated mutation execution.'
    }
}

$failedCritical = @($outcomes | Where-Object { $_.critical -and $_.status -ne 'Killed' })
if ($null -eq $failure -and $failedCritical.Count -ne 0) {
    $failure = "Critical mutation evidence failed for: $($failedCritical.id -join ', ')."
}

$evidence = [pscustomobject]@{
    schemaVersion = 2
    revision = $revision
    tier = $Tier
    catalogSha256 = $catalogHash
    startedUtc = $startedUtc
    completedUtc = [DateTimeOffset]::UtcNow
    isolation = [pscustomobject]@{
        mode = 'copied-working-tree'
        primaryDirty = $beforeState.Dirty
        primaryFingerprintBefore = $beforeState.Fingerprint
        primaryFingerprintAfter = $afterState.Fingerprint
        primaryTreeUnchanged = $beforeState.Fingerprint -eq $afterState.Fingerprint
        temporaryTreeRemoved = -not (Test-Path -LiteralPath $isolationRoot)
    }
    selectedMutationCount = $selected.Count
    baselines = @($baselines)
    mutations = @($outcomes)
    succeeded = $null -eq $failure
    failure = $failure
}
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
$evidence | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $evidencePath -Encoding utf8

if ($null -ne $failure) { throw "$failure Evidence: $evidencePath" }
Write-Host "Critical $Tier mutation tier passed in an isolated copied tree. Evidence: $evidencePath"
