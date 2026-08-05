#Requires -Version 7.0
[CmdletBinding()]
param([string]$RepositoryRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-RepositoryRoot([string]$Candidate) {
    if ([string]::IsNullOrWhiteSpace($Candidate)) { $Candidate = Join-Path $PSScriptRoot ".." }
    $resolved = (Resolve-Path -LiteralPath $Candidate).Path
    if (-not (Test-Path -LiteralPath (Join-Path $resolved "SharpAccess.sln") -PathType Leaf)) { throw "Repository root is invalid: $resolved" }
    return $resolved
}
function Assert-File([string]$Root, [string]$RelativePath) {
    if (-not (Test-Path -LiteralPath (Join-Path $Root $RelativePath) -PathType Leaf)) { throw "Required file is missing: $RelativePath" }
}
function Get-ActiveRepositoryFiles([string]$Root) {
    if (Get-Command git -ErrorAction SilentlyContinue) {
        & git -C $Root rev-parse --is-inside-work-tree *> $null
        if ($LASTEXITCODE -eq 0) {
            $files = @(& git -C $Root ls-files --cached --others --exclude-standard | ForEach-Object { $_.Replace("\", "/") } | Where-Object { Test-Path -LiteralPath (Join-Path $Root $_) -PathType Leaf })
            if ($LASTEXITCODE -ne 0) { throw "Unable to enumerate active repository files with git." }
            return @($files | Sort-Object -Unique)
        }
    }
    return @(Get-ChildItem -LiteralPath $Root -Recurse -File | Where-Object { $_.FullName -notmatch "[\\/](?:\.git|\.vs|\.vscode|\.idea|artifacts|bin|obj)[\\/]" } | ForEach-Object { [IO.Path]::GetRelativePath($Root, $_.FullName).Replace("\", "/") } | Sort-Object -Unique)
}
function Assert-NoForbiddenTopology([string[]]$Files) {
    $forbidden = @($Files | Where-Object {
        $_ -match "(?i)\.sh$" -or
        $_ -match "(?i)^eng/containers/" -or
        $_ -match "(?i)(?:^|/)Dockerfile(?:\.|$)" -or
        $_ -match "(?i)(?:^|/)(?:docker-)?compose(?:\.[^.]+)?\.ya?ml$" -or
        $_ -match "(?i)^providers/SharpAccess\.(?:SqlServer|MySql)/" -or
        $_ -match "(?i)^eng/public-api/SharpAccess\.(?:SqlServer|MySql)\.txt$" -or
        $_ -match "(?i)^scripts/(?:sqlserver|mysql|live-test)" -or
        $_ -match "(?i)^docs/(?:SQLSERVER|MYSQL|LOCAL-LIVE-TESTING)" -or
        $_ -match "(?i)^tests/.+(?:SqlServer|MySql|LocalLiveEnvironment).+\.cs$" -or
        $_ -match "(?i)\.py$" -or
        ($_ -notmatch "/" -and $_ -match "(?i)\.(?:patch|log|binlog|tmp|bak|orig|rej)$")
    })
    if ($forbidden.Count -ne 0) { throw "Forbidden Windows-only repository artifacts were found: $($forbidden -join ', ')" }
}
function Assert-PowerShellPolicy([string]$Root, [string[]]$Files) {
    $scripts = @($Files | Where-Object { $_ -match "(?i)^scripts/.+\.ps1$" })
    foreach ($script in $scripts) {
        $content = Get-Content -LiteralPath (Join-Path $Root $script) -Raw
        $firstLine = Get-Content -LiteralPath (Join-Path $Root $script) -TotalCount 1
        if ($firstLine -cne "#Requires -Version 7.0") { throw "$script must require PowerShell 7." }
        if ($content -notmatch "Set-StrictMode -Version Latest") { throw "$script must enable strict mode." }
    }
}
function Assert-WindowsWorkflows([string]$Root, [string[]]$Files) {
    foreach ($workflow in @($Files | Where-Object { $_ -match "(?i)^\.github/workflows/.+\.ya?ml$" })) {
        $content = Get-Content -LiteralPath (Join-Path $Root $workflow) -Raw
        if ($content -match "(?i)ubuntu-latest|macos-latest|shell:\s*bash|\bbash\s+\.??/?scripts/|\bdocker\b|(?m)^\s*services:\s*$") {
            throw "$workflow violates the Windows-only, container-free workflow policy."
        }
        if ($content -match "(?m)^\s*runs-on:\s*" -and $content -notmatch "(?m)^\s*runs-on:\s*windows-latest\s*$") {
            throw "$workflow must use windows-latest."
        }
    }
}
function Assert-LockFileOwnership([string[]]$Files) {
    $projects = @($Files | Where-Object { $_ -match "(?i)\.csproj$" })
    $locks = @($Files | Where-Object { $_ -match "(?i)(?:^|/)packages\.lock\.json$" })
    $projectDirectories = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($project in $projects) {
        $directory = [IO.Path]::GetDirectoryName($project).Replace("\", "/")
        [void]$projectDirectories.Add($directory)
        if ($locks -notcontains "$directory/packages.lock.json") { throw "Active project is missing packages.lock.json: $project" }
    }
    foreach ($lock in $locks) {
        $directory = [IO.Path]::GetDirectoryName($lock).Replace("\", "/")
        if (-not $projectDirectories.Contains($directory)) { throw "Lock file is not beneath an active project: $lock" }
    }
}
function Assert-SolutionAgreement([string]$Root, [string[]]$Files) {
    $solution = Get-Content -LiteralPath (Join-Path $Root "SharpAccess.sln") -Raw
    $solutionProjects = @([regex]::Matches($solution, '"([^"\r\n]+\.csproj)"') | ForEach-Object { $_.Groups[1].Value.Replace("\", "/") } | Sort-Object -Unique)
    $activeProjects = @($Files | Where-Object { $_ -match "(?i)\.csproj$" } | Sort-Object -Unique)
    $differences = @(Compare-Object -ReferenceObject $activeProjects -DifferenceObject $solutionProjects)
    if ($differences.Count -ne 0) { throw "SharpAccess.sln and the active project tree disagree: $($differences | Out-String)" }
}
function Assert-ProviderCatalog([string]$Root) {
    [xml]$status = Get-Content -LiteralPath (Join-Path $Root "eng/ProviderStatus.props") -Raw
    $expected = @{
        SharpAccessCoreStatus = "Supported"
        SharpAccessSqliteStatus = "Supported"
        SharpAccessPostgresStatus = "Supported"
    }
    foreach ($name in $expected.Keys) {
        $node = $status.SelectSingleNode("//PropertyGroup/$name")
        if ($null -eq $node -or $node.InnerText.Trim() -cne $expected[$name]) { throw "Provider status $name is missing or inaccurate." }
    }
    foreach ($retired in "SharpAccessSqlServerStatus", "SharpAccessMySqlStatus") {
        if ($null -ne $status.SelectSingleNode("//PropertyGroup/$retired")) { throw "$retired must not exist in the active provider catalog." }
    }
    $providerProjects = @(Get-ChildItem -LiteralPath (Join-Path $Root "providers") -Directory -Filter "SharpAccess.*" | Select-Object -ExpandProperty Name | Sort-Object)
    if (($providerProjects -join "|") -cne "SharpAccess.Postgres|SharpAccess.Sqlite") { throw "Active provider projects must be exactly SharpAccess.Sqlite and SharpAccess.Postgres." }
}
function Assert-NoRetiredPublicSurface([string]$Root, [string[]]$Files) {
    $candidateFiles = @($Files | Where-Object {
        $_ -match "(?i)^(?:src|providers|samples|tools|scripts)/.*\.(?:cs|csproj|ps1)$" -and
        $_ -cne "scripts/verify-structure.ps1"
    })
    $violations = @($candidateFiles | Where-Object {
        $content = Get-Content -LiteralPath (Join-Path $Root $_) -Raw
        $content -match "AddSqlServerAccess|AddMySqlAccess|SharpAccess\.SqlServer|SharpAccess\.MySql"
    })
    if ($violations.Count -ne 0) { throw "Retired provider surface remains active: $($violations -join ', ')" }
}
function Assert-SbomOrchestration([string]$Root) {
    $localCi = Get-Content -LiteralPath (Join-Path $Root "scripts/local-ci.ps1") -Raw
    $releaseDryRun = Get-Content -LiteralPath (Join-Path $Root "scripts/release-dry-run.ps1") -Raw

    if ($localCi -notmatch '\[switch\]\$SkipSbom') {
        throw "local-ci.ps1 must expose -SkipSbom for release orchestration."
    }
    if ($localCi -notmatch '\[switch\]\$RequirePostgres') {
        throw "local-ci.ps1 must expose -RequirePostgres for supported PostgreSQL release orchestration."
    }
    if ($localCi -notmatch '(?s)if \(-not \$SkipSbom\)\s*\{\s*Invoke-LocalCiStage "Package inventory and SBOM"') {
        throw "local-ci.ps1 must keep standalone SBOM generation behind -SkipSbom."
    }
    if ($releaseDryRun -notmatch '(?m)scripts/local-ci\.ps1"\) -RepositoryRoot \$root -RepositoryUrl \$RepositoryUrl -SkipSbom -RequirePostgres\s*$') {
        throw "release-dry-run.ps1 must invoke local-ci.ps1 with -SkipSbom -RequirePostgres."
    }
    if ($releaseDryRun -notmatch '-RequiredPackageArchive \$supportedPackageIds') {
        throw "release-dry-run.ps1 must require every supported prerelease package archive."
    }
    $formalSbomCalls = [regex]::Matches($releaseDryRun, 'scripts/sbom\.ps1').Count
    if ($formalSbomCalls -ne 2) {
        throw "release-dry-run.ps1 must retain exactly the prerelease and stable formal SBOM branches."
    }
}
function Assert-RoadmapAwareness([string]$Root) {
    $roadmap = Get-Content -LiteralPath (Join-Path $Root "docs/ROADMAP.md") -Raw
    foreach ($provider in "SQL Server", "MySQL") {
        if ($roadmap -notmatch [regex]::Escape($provider) -or $roadmap -notmatch "(?i)roadmap") { throw "$provider must remain documented as future roadmap work." }
    }
    if ($roadmap -notmatch "(?i)Windows-only" -or $roadmap -notmatch "(?i)PowerShell 7") { throw "The roadmap must record the Windows-only PowerShell policy." }
}

if (-not [OperatingSystem]::IsWindows()) { throw "SharpAccess repository verification is supported on Windows only." }
$root = Resolve-RepositoryRoot $RepositoryRoot
$files = @(Get-ActiveRepositoryFiles $root)
Assert-NoForbiddenTopology $files
Assert-PowerShellPolicy $root $files
Assert-WindowsWorkflows $root $files
Assert-LockFileOwnership $files
Assert-SolutionAgreement $root $files
Assert-ProviderCatalog $root
Assert-NoRetiredPublicSurface $root $files
Assert-SbomOrchestration $root
Assert-RoadmapAwareness $root
$requiredFiles = @(
    ".editorconfig", ".gitignore", ".config/dotnet-tools.json", "SharpAccess.sln",
    "Directory.Build.props", "Directory.Packages.props", "eng/ProviderStatus.props",
    "eng/CoveragePolicy.props", "eng/ProviderCoverage.props", "eng/ComplexityPolicy.props", "eng/QualityReportPolicy.props",
    "eng/ComplexityBaseline.json", "eng/PerformanceBaseline.json", "PROJECT_MANIFEST.md",
    "README.md", "SECURITY.md", "docs/README.md", "docs/ROADMAP.md", "docs/PROVIDER-STATUS.md",
    "docs/PROVIDER-CONTRACT-TESTING.md", "docs/RELEASE-CHECKLIST.md",
    "docs/RELEASE-CANDIDATE.md", "docs/RELEASE-REPOSITORY-BOOTSTRAP.md", "docs/QUALITY-REPORT.md",
    "scripts/verify-local.ps1", "scripts/verify-structure.ps1", "scripts/setup-test.ps1", "scripts/quality-report.ps1",
    "scripts/local-ci.ps1", "scripts/provider-contracts.ps1", "scripts/provider-coverage.ps1",
    "scripts/postgres-recovery-drill.ps1", "scripts/postgres-promotion.ps1",
    "scripts/release-candidate.ps1", "docs/POSTGRES-PROMOTION.md",
    "docs/adr/0021-postgresql-support-promotion.md",
    "scripts/export-dry-run.ps1", "scripts/pack.ps1", "scripts/package-smoke.ps1",
    "scripts/sbom.ps1", ".github/workflows/ci.yml", ".github/workflows/provider-contracts.yml",
    ".github/workflows/release-candidate.yml", ".github/required-checks.json")
foreach ($file in $requiredFiles) { Assert-File $root $file }
foreach ($provider in "SharpAccess.Sqlite", "SharpAccess.Postgres") {
    $rootSources = @(Get-ChildItem -LiteralPath (Join-Path $root "providers/$provider") -Filter "*.cs" -File)
    if ($rootSources.Count -ne 0) { throw "$provider source files must use responsibility-bearing directories." }
}
Write-Host "Repository structure passed: Windows-only PowerShell tooling with supported Core, SQLite, and PostgreSQL packages."