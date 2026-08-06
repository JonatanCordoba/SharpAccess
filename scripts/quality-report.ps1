#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$RepositoryUrl = "https://github.com/JonatanCordoba/SharpAccess",
    [string]$CoveragePath = "artifacts/coverage/combined/coverage-report.xml",
    [string]$ComplexityPath = "artifacts/quality/complexity/complexity.json",
    [string]$OutputDirectory = "artifacts/quality-report"
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-RepositoryRoot([string]$Candidate) {
    if ([string]::IsNullOrWhiteSpace($Candidate)) { $Candidate = Join-Path $PSScriptRoot ".." }
    $resolved = (Resolve-Path -LiteralPath $Candidate).Path
    if (-not (Test-Path -LiteralPath (Join-Path $resolved "SharpAccess.sln") -PathType Leaf)) {
        throw "Repository root is invalid: $resolved"
    }
    return $resolved
}

function Resolve-RepositoryPath([string]$Root, [string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) { return $Path }
    return Join-Path $Root $Path
}

function Assert-CleanCommittedRevision([string]$Root) {
    $revision = (& git -C $Root rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($revision)) {
        throw "Quality-report generation requires a committed Git revision."
    }
    $status = @(& git -C $Root status --porcelain=v1 --untracked-files=all --ignore-submodules=none)
    if ($LASTEXITCODE -ne 0) { throw "Unable to inspect the Git working tree." }
    if ($status.Count -ne 0) {
        throw "Quality-report generation requires a clean tracked and nonignored working tree:`n$($status -join "`n")"
    }
    return $revision
}

function Assert-RequiredCoverageEvidence([string]$PolicyPath, [string]$CoverageFile) {
    [xml]$policyDocument = Get-Content -LiteralPath $PolicyPath -Raw
    [xml]$coverageDocument = Get-Content -LiteralPath $CoverageFile -Raw
    $requiredAssemblies = @(
        $policyDocument.SelectNodes("//QualityReportProject") |
            Where-Object { $_.GetAttribute("Classification") -ceq "Production" } |
            ForEach-Object { $_.GetAttribute("Assembly") } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Sort-Object -Unique
    )
    if ($requiredAssemblies.Count -eq 0) {
        throw "Quality-report policy declares no production assemblies."
    }

    $packages = @($coverageDocument.SelectNodes("/coverage/packages/package"))
    $observedAssemblies = @(
        $packages |
            ForEach-Object { $_.GetAttribute("name") } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Sort-Object -Unique
    )
    $observedSummary = if ($observedAssemblies.Count -eq 0) { "<none>" } else { $observedAssemblies -join ", " }
    foreach ($assembly in $requiredAssemblies) {
        $matches = @($packages | Where-Object { $_.GetAttribute("name") -ceq $assembly })
        if ($matches.Count -eq 0) {
            throw "Required quality-report coverage evidence is missing for production assembly '$assembly'. Observed packages: $observedSummary. Coverage input: $CoverageFile"
        }
        $executableLines = @($matches | ForEach-Object { $_.SelectNodes(".//line") }).Count
        if ($executableLines -eq 0) {
            throw "Required quality-report coverage evidence contains no executable lines for production assembly '$assembly'. Observed packages: $observedSummary. Coverage input: $CoverageFile"
        }
    }
}

if (-not [OperatingSystem]::IsWindows()) { throw "SharpAccess quality reporting is supported on Windows only." }
$root = Resolve-RepositoryRoot $RepositoryRoot
Set-Location -LiteralPath $root
$revision = Assert-CleanCommittedRevision $root
$coverage = Resolve-RepositoryPath $root $CoveragePath
$complexity = Resolve-RepositoryPath $root $ComplexityPath
$output = Resolve-RepositoryPath $root $OutputDirectory
$policy = Join-Path $root "eng/QualityReportPolicy.props"
$project = Join-Path $root "tools/SharpAccess.QualityReport/SharpAccess.QualityReport.csproj"
$coverageSource = Split-Path -Parent $coverage
$coverageOutput = Join-Path $output "coverage"

foreach ($required in $coverage,$complexity,$policy,$project) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required quality-report input is missing: $required" }
}
Assert-RequiredCoverageEvidence -PolicyPath $policy -CoverageFile $coverage

Remove-Item -LiteralPath $output -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $coverageOutput | Out-Null
Copy-Item -Path (Join-Path $coverageSource "*") -Destination $coverageOutput -Recurse -Force

$toolManifest = Get-Content -LiteralPath (Join-Path $root ".config/dotnet-tools.json") -Raw | ConvertFrom-Json
$reportGeneratorVersion = [string]$toolManifest.tools.'dotnet-reportgenerator-globaltool'.version
if ([string]::IsNullOrWhiteSpace($reportGeneratorVersion)) { throw "ReportGenerator version is missing from the tool manifest." }

& dotnet run --project $project --configuration Release --no-build --no-restore -- `
    --repository-root $root `
    --repository-url $RepositoryUrl `
    --revision $revision `
    --policy $policy `
    --coverage $coverage `
    --complexity $complexity `
    --output $output `
    --report-generator-version $reportGeneratorVersion
if ($LASTEXITCODE -ne 0) { throw "SharpAccess.QualityReport failed with exit code $LASTEXITCODE." }

$index = Join-Path $output "index.html"
$metrics = Join-Path $output "metrics.json"
$manifest = Join-Path $output "manifest.json"
foreach ($required in $index,$metrics,$manifest,(Join-Path $coverageOutput "index.html")) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Quality-report output is incomplete: $required" }
}

$evidence = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
if ([string]$evidence.revision -cne $revision) {
    throw "Quality-report manifest revision differs from checked-out HEAD."
}

$sensitivePrefixes = @(
    $root,
    $env:USERPROFILE,
    $env:LOCALAPPDATA,
    $env:APPDATA,
    $env:TEMP,
    $env:TMP,
    [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
) |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    ForEach-Object { [IO.Path]::GetFullPath([string]$_).TrimEnd('\', '/') } |
    Sort-Object -Unique

$uncMachinePrefix = if ([string]::IsNullOrWhiteSpace($env:COMPUTERNAME)) {
    $null
}
else {
    "\\$($env:COMPUTERNAME)\"
}

foreach ($path in $index,$metrics,$manifest) {
    $content = Get-Content -LiteralPath $path -Raw
    foreach ($prefix in $sensitivePrefixes) {
        if ($content.Contains($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Quality-report output contains a host-specific absolute path: $path"
        }
    }
    if ($null -ne $uncMachinePrefix -and $content.Contains($uncMachinePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Quality-report output contains a host-specific UNC path: $path"
    }
}

Write-Host "Engineering-quality report passed for revision $revision."
Write-Host "Open: artifacts/quality-report/index.html"
