#Requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $RepositoryRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = [System.IO.Path]::GetFullPath($RepositoryRoot)
$workflowPath = Join-Path $root '.github/workflows/publish-nuget.yml'
$requiredScripts = @(
    'scripts/nuget-publication-preflight.ps1',
    'scripts/validate-nuget-publication-artifact.ps1',
    'scripts/assert-nuget-version-unpublished.ps1',
    'scripts/publish-nuget-cohort.ps1'
)

if (-not (Test-Path -LiteralPath $workflowPath -PathType Leaf)) {
    throw "Missing workflow: $workflowPath"
}
foreach ($relativePath in $requiredScripts) {
    $path = Join-Path $root $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing required publication script: $path"
    }
}

$workflow = Get-Content -LiteralPath $workflowPath -Raw

$artifactValidatorPath =
    Join-Path $root 'scripts/validate-nuget-publication-artifact.ps1'
$artifactValidator =
    Get-Content -LiteralPath $artifactValidatorPath -Raw

$requiredLicenseFragments = @(
    'Directory.Build.props',
    'PackageLicenseExpression',
    '$expectedLicenseExpression'
)

foreach ($fragment in $requiredLicenseFragments) {
    if (-not $artifactValidator.Contains(
        $fragment,
        [System.StringComparison]::Ordinal
    )) {
        throw "Publication artifact validator is missing tracked license-policy fragment: $fragment"
    }
}

if ($artifactValidator.Contains(
    'AGPL-3.0-only',
    [System.StringComparison]::OrdinalIgnoreCase
)) {
    throw 'Publication artifact validator still contains the obsolete AGPL-3.0-only license assumption.'
}

$supplyChainPath = Join-Path $root 'eng/SupplyChain.props'
if (-not (Test-Path -LiteralPath $supplyChainPath -PathType Leaf)) {
    throw "Missing central action-pin authority: $supplyChainPath"
}
[xml]$supplyChainPolicy = Get-Content -LiteralPath $supplyChainPath -Raw

$expectedActionPins = [ordered]@{
    'actions/checkout' = @{
        Sha = ('df4cb1c069' + 'e1874edd31' + 'b4311f1884' + '172cec0e10')
        Release = 'v6.0.3'
    }
    'actions/setup-dotnet' = @{
        Sha = ('26b0ec14cb' + '23fa690473' + '9307f278c1' + '4f94c95bf1')
        Release = 'v5.4.0'
    }
    'actions/download-artifact' = @{
        Sha = ('3e5f45b2cf' + 'b9172054b4' + '087a40e8e0' + 'b5a5461e7c')
        Release = 'v8.0.1'
    }
    'NuGet/login' = @{
        Sha = ('8d196754b4' + '036150537f' + '80ac539e15' + 'c2f1028841')
        Release = 'v1.2.0'
    }
}

$centralActionPins = @(
    $supplyChainPolicy.Project.ItemGroup.ActionPin
)

foreach ($entry in $expectedActionPins.GetEnumerator()) {
    $matches = @(
        $centralActionPins |
            Where-Object {
                [string]$_.Include -ceq $entry.Key
            }
    )

    if ($matches.Count -ne 1) {
        throw "eng/SupplyChain.props must contain exactly one ActionPin for $($entry.Key)."
    }

    $pin = $matches[0]

    if ([string]$pin.Sha -cne $entry.Value.Sha) {
        throw "eng/SupplyChain.props has the wrong SHA for $($entry.Key)."
    }

    if ([string]$pin.Release -cne $entry.Value.Release) {
        throw "eng/SupplyChain.props has the wrong release metadata for $($entry.Key)."
    }
}
$requiredChecksPath = Join-Path $root '.github/required-checks.json'
if (-not (Test-Path -LiteralPath $requiredChecksPath -PathType Leaf)) {
    throw "Missing required-check manifest: $requiredChecksPath"
}
$requiredChecks = Get-Content -LiteralPath $requiredChecksPath -Raw
if ($requiredChecks -notmatch '(?i)main') {
    throw '.github/required-checks.json does not mention main.'
}
if ($requiredChecks -match '(?i)master') {
    throw '.github/required-checks.json still contains obsolete master metadata.'
}
$expectedRequiredChecks = @(
    'ci / ci-windows',
    'operational readiness / operational-readiness-windows',
    'provider-contracts / sqlite-supported',
    'pull request evidence / Validate pull request evidence',
    'sast / devskim',
    'secret scanning / tracked-secret-scan',
    'dependency review / review'
)
foreach ($check in $expectedRequiredChecks) {
    if (-not $requiredChecks.Contains($check, [System.StringComparison]::Ordinal)) {
        throw ".github/required-checks.json is missing live PR check: $check"
    }
}

$requiredFragments = @(
    'workflow_dispatch:',
    'environment: nuget-release',
    'actions: read',
    'contents: read',
    'id-token: write',
    'release_candidate_artifact_digest',
    'evidence_index_path',
    'checksums_path',
    'assert-nuget-version-unpublished.ps1',
    'publish-nuget-cohort.ps1'
)
foreach ($fragment in $requiredFragments) {
    if (-not $workflow.Contains($fragment, [System.StringComparison]::Ordinal)) {
        throw "Publication workflow is missing required fragment: $fragment"
    }
}

foreach ($entry in $expectedActionPins.GetEnumerator()) {
    $requiredActionUse = "$($entry.Key)@$($entry.Value.Sha)"

    if (-not $workflow.Contains(
        $requiredActionUse,
        [System.StringComparison]::Ordinal
    )) {
        throw "Publication workflow does not use central action pin: $requiredActionUse"
    }
}
$forbiddenFragments = @(
    '--skip-duplicate',
    'NUGET_API_KEY: ${{ secrets.',
    'dotnet pack',
    'dotnet build',
    'dotnet restore'
)
foreach ($fragment in $forbiddenFragments) {
    if ($workflow.Contains($fragment, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Publication workflow contains forbidden fragment: $fragment"
    }
}

$workflowActionUses = [regex]::Matches($workflow, '(?m)^\s*uses:\s*([^\s#]+)') | ForEach-Object { $_.Groups[1].Value }
foreach ($actionUse in $workflowActionUses) {
    if ($actionUse -notmatch '@[0-9a-fA-F]{40}$') {
        throw "External GitHub Action is not pinned to a full commit SHA: $actionUse"
    }
}

Write-Host 'NuGet publication implementation structure validation passed.'
