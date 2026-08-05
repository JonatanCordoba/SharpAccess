#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$ChangedLineBaseRef,
    [ValidateRange(0, 8)][int]$MaxParallelTestJobs = 0,
    [switch]$UpdateComplexityBaseline
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Runs one dotnet command and converts a nonzero exit code into a terminating failure.
function Invoke-DotNet([string[]]$Arguments, [string]$Failure) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw $Failure }
}

# Gets one named coverage threshold from the central policy.
function Get-CoverageGate([string]$Root, [string]$Name) {
    [xml]$policy = Get-Content -LiteralPath (Join-Path $Root "eng/CoveragePolicy.props") -Raw
    $entry = @($policy.Project.ItemGroup.CoverageGate) | Where-Object Include -CEQ $Name | Select-Object -First 1
    if ($null -eq $entry) { throw "Coverage gate is missing: $Name" }
    return [pscustomobject]@{ Line = [decimal]::Parse([string]$entry.Line, [Globalization.CultureInfo]::InvariantCulture); Branch = [decimal]::Parse([string]$entry.Branch, [Globalization.CultureInfo]::InvariantCulture) }
}

# Generates one normalized coverage report for the selected assemblies.
function Invoke-Report([string]$Reports, [string]$Target, [string]$AssemblyFilter, [string]$Root) {
    $arguments = @(
        "reportgenerator",
        "-reports:$Reports",
        "-targetdir:$Target",
        "-sourcedirs:$Root",
        "-assemblyfilters:$AssemblyFilter",
        "-reporttypes:XmlSummary;HtmlSummary;Html;Cobertura"
    )
    Invoke-DotNet $arguments "Coverage report generation failed for $Target."
    Move-Item -LiteralPath (Join-Path $Target "Summary.xml") -Destination (Join-Path $Target "coverage.xml") -Force
    & (Join-Path $Root "scripts/normalize-coverage-file-names.ps1") -RepositoryRoot $Root -SearchRoot $Target
}

# Resolves the bounded test-worker count from the explicit value, environment override, or host capacity.
function Resolve-MaxParallelTestJobs([int]$Requested) {
    if ($Requested -gt 0) { return $Requested }
    if (-not [string]::IsNullOrWhiteSpace($env:SHARPACCESS_MAX_PARALLEL_TEST_JOBS)) {
        $parsed = 0
        if (-not [int]::TryParse($env:SHARPACCESS_MAX_PARALLEL_TEST_JOBS, [ref]$parsed) -or $parsed -lt 1 -or $parsed -gt 8) {
            throw "SHARPACCESS_MAX_PARALLEL_TEST_JOBS must be an integer from 1 through 8."
        }
        return $parsed
    }
    $capacity = [Math]::Max(1, [int][Math]::Floor([Environment]::ProcessorCount / 2))
    return [Math]::Min(2, $capacity)
}

# Starts one isolated coverage-enabled test process without shell argument re-parsing.
function Start-TestProcess([pscustomobject]$Run, [string]$Root) {
    $resultPath = Join-Path $Root $Run.ResultPath
    New-Item -ItemType Directory -Force -Path $resultPath | Out-Null
    $arguments = @(
        'test',
        $Run.ProjectPath,
        '--configuration',
        'Release',
        '--no-build',
        '--settings',
        $Run.SettingsPath,
        '--collect:XPlat Code Coverage',
        '--results-directory',
        $resultPath
    )
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'dotnet'
    $startInfo.WorkingDirectory = $Root
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $arguments) { [void]$startInfo.ArgumentList.Add($argument) }
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) { throw "Unable to start tests for $($Run.Name)." }
    return [pscustomobject]@{
        Run = $Run
        Process = $process
        StandardOutput = $process.StandardOutput.ReadToEndAsync()
        StandardError = $process.StandardError.ReadToEndAsync()
    }
}

# Completes one test process, persists deterministic output, and returns its exit status.
function Complete-TestProcess([pscustomobject]$Execution, [string]$Root) {
    $Execution.Process.WaitForExit()
    $standardOutput = $Execution.StandardOutput.GetAwaiter().GetResult()
    $standardError = $Execution.StandardError.GetAwaiter().GetResult()
    $logPath = Join-Path $Root (Join-Path $Execution.Run.ResultPath 'test-output.log')
    $log = ($standardOutput + $standardError).TrimEnd()
    [IO.File]::WriteAllText($logPath, $log + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
    if (-not [string]::IsNullOrWhiteSpace($log)) { Write-Host $log }
    $exitCode = $Execution.Process.ExitCode
    $Execution.Process.Dispose()
    return $exitCode
}

# Runs isolated test projects in bounded batches and normalizes each collector result.
function Invoke-ParallelCoverageTests([object[]]$Runs, [int]$Throttle, [string]$Root) {
    for ($offset = 0; $offset -lt $Runs.Count; $offset += $Throttle) {
        $end = [Math]::Min($offset + $Throttle - 1, $Runs.Count - 1)
        $batch = @($Runs[$offset..$end])
        $executions = @()
        foreach ($run in $batch) {
            Write-Host "Starting coverage tests: $($run.Name)"
            $executions += Start-TestProcess -Run $run -Root $Root
        }
        $failures = @()
        foreach ($execution in $executions) {
            $exitCode = Complete-TestProcess -Execution $execution -Root $Root
            & (Join-Path $Root 'scripts/normalize-coverage-file-names.ps1') -RepositoryRoot $Root -SearchRoot (Join-Path $Root $execution.Run.ResultPath)
            if ($exitCode -ne 0) { $failures += "$($execution.Run.Name) (exit $exitCode)" }
            else { Write-Host "Coverage tests passed: $($execution.Run.Name)" }
        }
        if ($failures.Count -ne 0) { throw "Coverage test failures: $($failures -join ', ')." }
    }
}

$root = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { (Resolve-Path (Join-Path $PSScriptRoot '..')).Path } else { (Resolve-Path $RepositoryRoot).Path }
Set-Location -LiteralPath $root
$parallelTestJobs = Resolve-MaxParallelTestJobs $MaxParallelTestJobs
Write-Host "Coverage test worker limit: $parallelTestJobs"
Remove-Item artifacts/test-results,artifacts/coverage,artifacts/quality/complexity -Recurse -Force -ErrorAction SilentlyContinue
'artifacts/test-results/unit','artifacts/test-results/combined','artifacts/coverage/unit','artifacts/coverage/combined','artifacts/coverage/core','artifacts/coverage/sqlite' |
    ForEach-Object { New-Item -ItemType Directory -Force $_ | Out-Null }

Invoke-DotNet @('tool','restore') 'Tool restore failed.'
Invoke-DotNet @('restore','SharpAccess.sln','--locked-mode') 'Locked restore failed.'
Invoke-DotNet @('build','SharpAccess.sln','--configuration','Release','--no-restore','-warnaserror') 'Build failed.'
$testRuns = @(
    [pscustomobject]@{ Name = 'SharpAccess.UnitTests'; ProjectPath = 'tests/SharpAccess.UnitTests/SharpAccess.UnitTests.csproj'; SettingsPath = 'coverlet.unit.runsettings'; ResultPath = 'artifacts/test-results/unit' },
    [pscustomobject]@{ Name = 'SharpAccess.IntegrationTests'; ProjectPath = 'tests/SharpAccess.IntegrationTests/SharpAccess.IntegrationTests.csproj'; SettingsPath = 'coverlet.runsettings'; ResultPath = 'artifacts/test-results/combined/SharpAccess.IntegrationTests' },
    [pscustomobject]@{ Name = 'SharpAccess.EndpointTests'; ProjectPath = 'tests/SharpAccess.EndpointTests/SharpAccess.EndpointTests.csproj'; SettingsPath = 'coverlet.runsettings'; ResultPath = 'artifacts/test-results/combined/SharpAccess.EndpointTests' },
    [pscustomobject]@{ Name = 'SharpAccess.ProviderContractTests'; ProjectPath = 'tests/SharpAccess.ProviderContractTests/SharpAccess.ProviderContractTests.csproj'; SettingsPath = 'coverlet.runsettings'; ResultPath = 'artifacts/test-results/combined/SharpAccess.ProviderContractTests' },
    [pscustomobject]@{ Name = 'SharpAccess.PackageTests'; ProjectPath = 'tests/SharpAccess.PackageTests/SharpAccess.PackageTests.csproj'; SettingsPath = 'coverlet.runsettings'; ResultPath = 'artifacts/test-results/combined/SharpAccess.PackageTests' }
)
Invoke-ParallelCoverageTests -Runs $testRuns -Throttle $parallelTestJobs -Root $root
Invoke-Report 'artifacts/test-results/unit/**/coverage-report.xml' 'artifacts/coverage/unit' '+SharpAccess.Core' $root

& ./scripts/normalize-coverage-file-names.ps1 -RepositoryRoot $root -SearchRoot 'artifacts/test-results/combined'
$reports = 'artifacts/test-results/unit/**/coverage-report.xml;artifacts/test-results/combined/**/coverage-report.xml'
Invoke-Report $reports 'artifacts/coverage/combined' '+SharpAccess.Core;+SharpAccess.Sqlite' $root
Invoke-Report $reports 'artifacts/coverage/core' '+SharpAccess.Core' $root
Invoke-Report $reports 'artifacts/coverage/sqlite' '+SharpAccess.Sqlite' $root

foreach ($name in 'CombinedSupported','Core','Sqlite') {
    $gate = Get-CoverageGate $root $name
    $directory = switch ($name) { 'CombinedSupported' { 'combined' } 'Core' { 'core' } 'Sqlite' { 'sqlite' } }
    & ./scripts/verify-coverage.ps1 -RepositoryRoot $root -Path "artifacts/coverage/$directory/coverage.xml" -Label $name -MinimumRate $gate.Line -MinimumBranchRate $gate.Branch
}

$changedGate = Get-CoverageGate $root 'ChangedHandwrittenProduction'
$changedArguments = @{
    RepositoryRoot = $root
    Path = 'artifacts/coverage/combined/coverage-report.xml'
    Scope = 'Supported'
    MinimumRate = $changedGate.Line
    MinimumBranchRate = $changedGate.Branch
}
if (-not [string]::IsNullOrWhiteSpace($ChangedLineBaseRef)) { $changedArguments.BaseRef = $ChangedLineBaseRef }
& ./scripts/changed-line-coverage.ps1 @changedArguments
$complexityArguments = @{ RepositoryRoot = $root; CoveragePath = 'artifacts/coverage/combined/coverage-report.xml' }
if ($UpdateComplexityBaseline) { $complexityArguments.UpdateBaseline = $true }
& ./scripts/complexity-report.ps1 @complexityArguments
