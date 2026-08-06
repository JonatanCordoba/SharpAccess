#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$RepositoryUrl = "https://github.com/JonatanCordoba/SharpAccess",
    [string]$ChangedCodeBaseRef = "origin/master"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Resolves the exact Git repository root used for revision-bound evidence.
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

# Runs one named promotion stage.
function Invoke-PromotionStage([string]$Name, [scriptblock]$Action) {
    Write-Host ""
    Write-Host "==> $Name"
    & $Action
}

# Requires a committed, clean tree and returns the exact revision.
function Assert-CleanCommittedRevision([string]$Root) {
    $revision = (& git -C $Root rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $revision -notmatch "^[0-9a-fA-F]{40}$") {
        throw "Unable to resolve the exact Git revision."
    }
    $status = @(& git -C $Root status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect the Git working tree."
    }
    if ($status.Count -ne 0) {
        throw "PostgreSQL promotion evidence requires a committed clean tree.`n$($status -join "`n")"
    }
    return $revision.ToLowerInvariant()
}

# Requires one exact environment opt-in without logging its secret value.
function Assert-EnvironmentValue([string]$Name, [string]$ExpectedValue = "") {
    $value = [Environment]::GetEnvironmentVariable($Name, "Process")
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "$Name is required."
    }

    $expectedValueWasSpecified = -not [string]::IsNullOrWhiteSpace($ExpectedValue)
    if ($expectedValueWasSpecified) {
        $valueMatches = [string]::Equals(
            $value,
            $ExpectedValue,
            [StringComparison]::OrdinalIgnoreCase)
        if (-not $valueMatches) {
            throw "$Name must equal $ExpectedValue."
        }
    }
}

# Reads one required provider-status property.
function Get-StatusValue([string]$Root, [string]$Name) {
    [xml]$status = Get-Content -LiteralPath (Join-Path $Root "eng/ProviderStatus.props") -Raw
    $node = $status.SelectSingleNode("//PropertyGroup/$Name")
    if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) {
        throw "Provider status property is missing: $Name"
    }
    return $node.InnerText.Trim()
}

# Adds one required artifact hash without reading or serializing secret inputs.
function Add-ArtifactEvidence(
    [Collections.Generic.List[object]]$Evidence,
    [string]$Root,
    [string]$RelativePath) {
    $path = Join-Path $Root $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required promotion artifact is missing: $RelativePath"
    }
    $item = Get-Item -LiteralPath $path
    $Evidence.Add([pscustomobject][ordered]@{
        path = $RelativePath.Replace("\", "/")
        bytes = $item.Length
        sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    })
}

if (-not [OperatingSystem]::IsWindows()) {
    throw "PostgreSQL promotion evidence is supported on Windows only."
}
foreach ($tool in @("git", "dotnet", "pwsh", "psql", "createdb", "dropdb", "pg_dump", "pg_restore")) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "$tool is required for PostgreSQL promotion evidence."
    }
}

$root = Resolve-RepositoryRoot $RepositoryRoot
Set-Location -LiteralPath $root
$revision = Assert-CleanCommittedRevision $root
& git -C $root rev-parse --verify "$ChangedCodeBaseRef^{commit}" *> $null
if ($LASTEXITCODE -ne 0) {
    throw "Changed-code base ref '$ChangedCodeBaseRef' is unavailable. Run git fetch origin before the promotion gate."
}

Assert-EnvironmentValue "SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING"
Assert-EnvironmentValue "SHARPACCESS_PROVIDER_TEST_ALLOW_RESET" "true"
Assert-EnvironmentValue "SHARPACCESS_POSTGRES_READINESS" "true"

if ((Get-StatusValue $root "SharpAccessPostgresStatus") -cne "Supported") {
    throw "SharpAccess.Postgres must be Supported in eng/ProviderStatus.props for the coordinated promotion revision."
}
$publicApi = Get-Content -LiteralPath (Join-Path $root "eng/public-api/SharpAccess.Postgres.txt") -Raw
if ($publicApi -notmatch "Microsoft\.Extensions\.DependencyInjection\.PostgresServiceCollectionExtensions") {
    throw "The reviewed PostgreSQL public registration type is missing from the public API baseline."
}
[xml]$project = Get-Content -LiteralPath (Join-Path $root "providers/SharpAccess.Postgres/SharpAccess.Postgres.csproj") -Raw
if ($project.Project.PropertyGroup.IsPackable.Count -lt 2) {
    throw "PostgreSQL conditional packability is missing."
}

& git -C $root diff --check HEAD
if ($LASTEXITCODE -ne 0) {
    throw "The committed promotion revision fails git diff --check."
}

Invoke-PromotionStage "PostgreSQL real-engine contracts, readiness, migrations, concurrency, cancellation, timeout, error classification, and query plans" {
    & (Join-Path $root "scripts/postgres-smoke.ps1") `
        -RepositoryRoot $root `
        -Configuration $Configuration `
        -RequireConfigured
}

Invoke-PromotionStage "PostgreSQL promotion line, branch, and changed-code coverage" {
    & (Join-Path $root "scripts/provider-coverage.ps1") `
        -RepositoryRoot $root `
        -Provider Postgres `
        -Configuration $Configuration `
        -ChangedCodeBaseRef $ChangedCodeBaseRef `
        -PromotionGate
}

Invoke-PromotionStage "PostgreSQL-specific provider-promotion mutations" {
    & (Join-Path $root "scripts/mutation-test.ps1") `
        -RepositoryRoot $root `
        -Tier ProviderPromotion `
        -BaseRef $ChangedCodeBaseRef
}

Invoke-PromotionStage "PostgreSQL native backup and restore" {
    & (Join-Path $root "scripts/postgres-recovery-drill.ps1") `
        -RepositoryRoot $root `
        -Configuration $Configuration
}

Invoke-PromotionStage "Complete clean-tree Windows gate with required PostgreSQL and package-consumer evidence" {
    & (Join-Path $root "scripts/verify-local.ps1") `
        -RepositoryRoot $root `
        -RepositoryUrl $RepositoryUrl
}

$revisionAfter = (& git -C $root rev-parse HEAD).Trim().ToLowerInvariant()
if ($LASTEXITCODE -ne 0 -or $revisionAfter -cne $revision) {
    throw "The checked-out revision changed during PostgreSQL promotion evidence."
}
[void](Assert-CleanCommittedRevision $root)

$packageVersion = $project.Project.PropertyGroup.Version |
    Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace([string]$packageVersion)) {
    throw "The PostgreSQL package version is missing."
}
$packageVersion = ([string]$packageVersion).Trim()

$artifacts = [Collections.Generic.List[object]]::new()
foreach ($relativePath in @(
    "artifacts/provider-coverage/postgres/evidence.json",
    "artifacts/mutation/providerpromotion.json",
    "artifacts/mutation/release.json",
    "artifacts/operations/postgres-recovery/postgres-recovery.json",
    "artifacts/packages/SharpAccess.Core.$packageVersion.nupkg",
    "artifacts/packages/SharpAccess.Core.$packageVersion.snupkg",
    "artifacts/packages/SharpAccess.Sqlite.$packageVersion.nupkg",
    "artifacts/packages/SharpAccess.Sqlite.$packageVersion.snupkg",
    "artifacts/packages/SharpAccess.Postgres.$packageVersion.nupkg",
    "artifacts/packages/SharpAccess.Postgres.$packageVersion.snupkg",
    "artifacts/sbom/sbom-evidence.json"
)) {
    Add-ArtifactEvidence $artifacts $root $relativePath
}

$evidenceRelativePath = "artifacts/postgres-promotion/evidence.json"
& git -C $root check-ignore -q -- $evidenceRelativePath
if ($LASTEXITCODE -ne 0) {
    throw "$evidenceRelativePath must remain ignored release evidence."
}
$evidencePath = Join-Path $root $evidenceRelativePath
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $evidencePath) | Out-Null

[pscustomobject][ordered]@{
    schemaVersion = 1
    provider = "SharpAccess.Postgres"
    providerStatus = "Supported"
    revision = $revision
    changedCodeBaseRef = $ChangedCodeBaseRef
    configuration = $Configuration
    platform = "Windows"
    publicRegistration = "Microsoft.Extensions.DependencyInjection.PostgresServiceCollectionExtensions.AddPostgresAccess"
    packageVersion = $packageVersion
    packageIds = @("SharpAccess.Core", "SharpAccess.Sqlite", "SharpAccess.Postgres")
    gates = [ordered]@{
        realEngineContracts = "passed"
        restrictedPrincipalReadiness = "passed"
        emptyAndHistoricalMigrations = "passed"
        concurrencyCancellationTimeoutAndErrorClassification = "passed"
        boundedQueriesAndQueryPlans = "passed"
        promotionCoverage = "passed"
        postgresSpecificMutation = "passed"
        nativeBackupRestore = "passed"
        packageValidationAndConsumerSmoke = "passed"
        completeCleanTreeGate = "passed"
    }
    unrelatedStableReleaseGates = [ordered]@{
        protectedOidc = "not evaluated by PostgreSQL promotion"
        controlledPerformanceBaseline = "not evaluated by PostgreSQL promotion"
        canonicalExportAndPublication = "not evaluated by PostgreSQL promotion"
    }
    credentials = "redacted; connection string not retained"
    completedUtc = [DateTimeOffset]::UtcNow.ToString("O")
    artifacts = @($artifacts)
} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $evidencePath -Encoding utf8

[void](Assert-CleanCommittedRevision $root)

Write-Host ""
Write-Host "PostgreSQL promotion gate passed for exact revision $revision."
Write-Host "Evidence: $evidenceRelativePath"
