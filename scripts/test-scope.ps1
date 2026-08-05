#Requires -Version 7.0
[CmdletBinding()]
param([string]$RepositoryRoot,[string]$BaseRef,[string]$HeadRef = "HEAD",[string]$GitHubOutput)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$root = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { (Resolve-Path (Join-Path $PSScriptRoot "..")).Path } else { (Resolve-Path $RepositoryRoot).Path }
if ([string]::IsNullOrWhiteSpace($BaseRef)) { $BaseRef = "HEAD^" }
function Invoke-Git([string[]]$Arguments) {
    $output = @(& git -C $root @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "git failed: $($output -join [Environment]::NewLine)" }
    return $output
}
[void](Invoke-Git @("rev-parse", "--verify", "$BaseRef^{commit}"))
[void](Invoke-Git @("rev-parse", "--verify", "$HeadRef^{commit}"))
$mergeBase = (Invoke-Git @("merge-base", $BaseRef, $HeadRef) | Select-Object -First 1).Trim()
$files = @(Invoke-Git @("diff", "--name-only", $mergeBase, $HeadRef))
$core=$false; $sqlite=$false; $postgres=$false; $release=$false; $mutation="None"; $allProviders=$false
foreach ($file in $files) {
    $path = $file.Replace("\", "/")
    $sharedControl = $path -match "^(global\.json|Directory\.(Build|Packages)\..+|NuGet\.Config|SharpAccess\.sln|\.config/dotnet-tools\.json|\.github/workflows/|eng/(CoveragePolicy\.props|ProviderCoverage\.props|ProviderStatus\.props|mutations\.json)|tests/SharpAccess\.PackageTests/|scripts/(setup-test|provider-contracts|provider-coverage|changed-line-coverage|mutation-test|test-scope|verify-coverage|verify-structure)\.ps1)"
    if ($sharedControl) { $core=$true; $allProviders=$true; $release=$true }
    if ($path -match "^(src/SharpAccess\.Core/|tests/SharpAccess\.ProviderContractTests/(?:AuthProviderContractTestBase\.cs|Registration/MultipleProviderRegistrationTests\.cs)|coverlet\.)") { $core=$true; $allProviders=$true }
    if ($path -match "^(providers/SharpAccess\.Sqlite/|tests/SharpAccess\.ProviderContractTests/(?:Sqlite|Infrastructure/Sqlite|Migrations/Sqlite|Security/Sqlite|Transactions/Sqlite|Registration/Sqlite)|tests/SharpAccess\.IntegrationTests/|tests/SharpAccess\.EndpointTests/)") { $core=$true; $sqlite=$true }
    if ($path -match "^(providers/SharpAccess\.Postgres/|tests/SharpAccess\.ProviderContractTests/(?:Postgres|Infrastructure/Postgres|Migrations/Postgres|Security/Postgres|Transactions/Postgres|Registration/Postgres))") { $core=$true; $postgres=$true }
    if ($path -match "(Migration|Persistence|IAuthStore|ProviderContracts)") { $core=$true; $allProviders=$true }
    if ($path -match "(Security|Tokens|Services|OAuth|Authorization|RateLimit|Password)") { $core=$true; $allProviders=$true; $mutation="PullRequest" }
    if ($path -match "^(\.github/|scripts/(release|pack|sbom|verify-local|local-ci)|eng/ProviderStatus|docs/(RELEASE|PROVIDER-STATUS)|PROJECT_MANIFEST|Directory\.)") { $release=$true }
}
if ($allProviders) { $sqlite=$true; $postgres=$true }
if ($files.Count -eq 0) { $core=$true; $sqlite=$true }
$result=[ordered]@{core=$core;sqlite=$sqlite;postgres=$postgres;release=$release;mutationTier=$mutation;baseRef=$BaseRef;headRef=$HeadRef;mergeBase=$mergeBase;files=@($files)}
$result | ConvertTo-Json -Depth 5
if (-not [string]::IsNullOrWhiteSpace($GitHubOutput)) {
    foreach ($name in "core","sqlite","postgres","release") { Add-Content -LiteralPath $GitHubOutput -Value "$name=$($result[$name].ToString().ToLowerInvariant())" }
    Add-Content -LiteralPath $GitHubOutput -Value "mutation_tier=$mutation"
}
