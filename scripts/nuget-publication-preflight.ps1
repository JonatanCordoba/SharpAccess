#Requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $RepositoryRoot,

    [Parameter(Mandatory)]
    [string] $Repository,

    [Parameter(Mandatory)]
    [string] $ReleaseTag,

    [Parameter(Mandatory)]
    [long] $ReleaseCandidateRunId,

    [Parameter(Mandatory)]
    [long] $ReleaseCandidateArtifactId,

    [Parameter(Mandatory)]
    [string] $ExpectedArtifactName,

    [Parameter(Mandatory)]
    [ValidatePattern('^sha256:[0-9A-Fa-f]{64}$')]
    [string] $ExpectedArtifactDigest,

    [Parameter(Mandatory)]
    [string] $Confirmation,

    [Parameter(Mandatory)]
    [string] $GitHubToken,

    [Parameter(Mandatory)]
    [string] $GitHubRef,

    [Parameter(Mandatory)]
    [string] $GitHubWorkflowRef,

    [string] $ExpectedRepository = 'JonatanCordoba/SharpAccess',

    [string] $ExpectedReleaseWorkflowPath = '.github/workflows/release-candidate.yml',

    [string] $ExpectedHeadBranch = 'main',

    [string] $ExpectedRunEvent = 'workflow_dispatch',

    [string] $GitHubOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-GitHubGet {
    param(
        [Parameter(Mandatory)]
        [string] $Uri
    )

    $headers = @{
        Accept                 = 'application/vnd.github+json'
        Authorization          = "Bearer $GitHubToken"
        'X-GitHub-Api-Version' = '2026-03-10'
    }

    Invoke-RestMethod -Method Get -Uri $Uri -Headers $headers
}

function Write-WorkflowOutput {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string] $Value
    )

    if ([string]::IsNullOrWhiteSpace($GitHubOutput)) {
        return
    }

    Add-Content -LiteralPath $GitHubOutput -Value "$Name=$Value" -Encoding utf8NoBOM
}

$resolvedRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
if (-not (Test-Path -LiteralPath $resolvedRoot -PathType Container)) {
    throw "Repository root does not exist: $resolvedRoot"
}

$workflowPath = Join-Path $resolvedRoot '.github/workflows/publish-nuget.yml'
if (-not (Test-Path -LiteralPath $workflowPath -PathType Leaf)) {
    throw "Publication workflow is missing from the selected tag: $workflowPath"
}

if ($Repository -cne $ExpectedRepository) {
    throw "Repository identity mismatch. Expected '$ExpectedRepository', got '$Repository'."
}

if ($ReleaseTag -cnotmatch '^v0\.9\.0-rc\.[1-9][0-9]*$') {
    throw "Release tag '$ReleaseTag' is outside the approved 0.9.0 RC publication line."
}

$expectedConfirmation = "PUBLISH $ReleaseTag"
if ($Confirmation -cne $expectedConfirmation) {
    throw "Publication confirmation mismatch. Expected exactly '$expectedConfirmation'."
}

$expectedRef = "refs/tags/$ReleaseTag"
if ($GitHubRef -cne $expectedRef) {
    throw "The workflow must be dispatched from '$expectedRef'. Current ref: '$GitHubRef'."
}

$expectedWorkflowRefPrefix = "$ExpectedRepository/.github/workflows/publish-nuget.yml@refs/tags/$ReleaseTag"
if ($GitHubWorkflowRef -cne $expectedWorkflowRefPrefix) {
    throw "Publication workflow identity mismatch. Expected '$expectedWorkflowRefPrefix', got '$GitHubWorkflowRef'."
}

if ([string]::IsNullOrWhiteSpace($GitHubToken)) {
    throw 'GitHub token is empty.'
}

$repositoryParts = @($ExpectedRepository.Split('/', 2))
if ($repositoryParts.Count -ne 2) {
    throw "Expected repository '$ExpectedRepository' is invalid."
}

$owner = [Uri]::EscapeDataString($repositoryParts[0])
$repo = [Uri]::EscapeDataString($repositoryParts[1])
$tag = [Uri]::EscapeDataString($ReleaseTag)
$apiBase = "https://api.github.com/repos/$owner/$repo"

$tagRef = Invoke-GitHubGet -Uri "$apiBase/git/ref/tags/$tag"
if ([string]$tagRef.ref -cne $expectedRef) {
    throw "Hosted tag ref mismatch. Expected '$expectedRef', got '$($tagRef.ref)'."
}
if ([string]$tagRef.object.type -cne 'tag') {
    throw "Release tag '$ReleaseTag' is not an annotated tag object."
}
if ([string]::IsNullOrWhiteSpace([string]$tagRef.object.sha)) {
    throw "Release tag '$ReleaseTag' has no tag-object SHA."
}

$tagObjectSha = [string]$tagRef.object.sha
$tagObject = Invoke-GitHubGet -Uri "$apiBase/git/tags/$tagObjectSha"
if ([string]$tagObject.tag -cne $ReleaseTag) {
    throw "Hosted tag-object identity mismatch. Expected '$ReleaseTag', got '$($tagObject.tag)'."
}
if ([string]$tagObject.object.type -cne 'commit') {
    throw "Release tag '$ReleaseTag' does not target a commit."
}
if (-not [bool]$tagObject.verification.verified) {
    throw "GitHub does not consider tag '$ReleaseTag' verified. Reason: $($tagObject.verification.reason)"
}
if ([string]::IsNullOrWhiteSpace([string]$tagObject.verification.verified_at)) {
    throw "GitHub reports tag '$ReleaseTag' as verified without a verification timestamp."
}

$releaseSha = ([string]$tagObject.object.sha).ToLowerInvariant()
if ($releaseSha -cnotmatch '^[0-9a-f]{40}$') {
    throw "Tagged commit SHA is invalid: '$releaseSha'."
}

$run = Invoke-GitHubGet -Uri "$apiBase/actions/runs/$ReleaseCandidateRunId"
if ([long]$run.id -ne $ReleaseCandidateRunId) {
    throw "Release-candidate run ID mismatch. Expected '$ReleaseCandidateRunId', got '$($run.id)'."
}
if ([string]$run.repository.full_name -cne $ExpectedRepository) {
    throw "Release-candidate run belongs to '$($run.repository.full_name)', not '$ExpectedRepository'."
}
if ([string]$run.status -cne 'completed' -or [string]$run.conclusion -cne 'success') {
    throw "Release-candidate run '$ReleaseCandidateRunId' is not a successful completed run. Status='$($run.status)', conclusion='$($run.conclusion)'."
}
if ([string]$run.event -cne $ExpectedRunEvent) {
    throw "Release-candidate run '$ReleaseCandidateRunId' has unexpected event '$($run.event)'. Expected '$ExpectedRunEvent'."
}
if ([string]$run.head_branch -cne $ExpectedHeadBranch) {
    throw "Release-candidate run '$ReleaseCandidateRunId' has head branch '$($run.head_branch)'. Expected '$ExpectedHeadBranch'."
}
if (([string]$run.head_sha).ToLowerInvariant() -cne $releaseSha) {
    throw "Release-candidate run head SHA '$($run.head_sha)' does not equal tagged commit '$releaseSha'."
}

$runWorkflowPath = ([string]$run.path -split '@', 2)[0]
if ($runWorkflowPath -cne $ExpectedReleaseWorkflowPath) {
    throw "Run '$ReleaseCandidateRunId' came from '$runWorkflowPath', not '$ExpectedReleaseWorkflowPath'."
}

$artifact = Invoke-GitHubGet -Uri "$apiBase/actions/artifacts/$ReleaseCandidateArtifactId"
if ([long]$artifact.id -ne $ReleaseCandidateArtifactId) {
    throw "Artifact ID mismatch. Expected '$ReleaseCandidateArtifactId', got '$($artifact.id)'."
}
if ([string]$artifact.name -cne $ExpectedArtifactName) {
    throw "Artifact name mismatch. Expected '$ExpectedArtifactName', got '$($artifact.name)'."
}
if ([bool]$artifact.expired) {
    throw "Artifact '$ReleaseCandidateArtifactId' is expired."
}
if ([long]$artifact.workflow_run.id -ne $ReleaseCandidateRunId) {
    throw "Artifact '$ReleaseCandidateArtifactId' belongs to run '$($artifact.workflow_run.id)', not '$ReleaseCandidateRunId'."
}
if (([string]$artifact.workflow_run.head_sha).ToLowerInvariant() -cne $releaseSha) {
    throw "Artifact '$ReleaseCandidateArtifactId' head SHA does not equal tagged commit '$releaseSha'."
}

$actualArtifactDigest = ([string]$artifact.digest).ToLowerInvariant()
$expectedDigest = $ExpectedArtifactDigest.ToLowerInvariant()
if ($actualArtifactDigest -cne $expectedDigest) {
    throw "Artifact digest mismatch. Expected '$expectedDigest', got '$actualArtifactDigest'."
}

$releaseVersion = $ReleaseTag.Substring(1)
Write-WorkflowOutput -Name 'release_sha' -Value $releaseSha
Write-WorkflowOutput -Name 'release_version' -Value $releaseVersion
Write-WorkflowOutput -Name 'artifact_name' -Value ([string]$artifact.name)
Write-WorkflowOutput -Name 'artifact_digest' -Value $actualArtifactDigest

Write-Host "NuGet publication identity preflight passed."
Write-Host "Release tag:      $ReleaseTag"
Write-Host "Release commit:   $releaseSha"
Write-Host "RC run ID:        $ReleaseCandidateRunId"
Write-Host "RC artifact ID:   $ReleaseCandidateArtifactId"
Write-Host "RC artifact name: $($artifact.name)"
Write-Host "RC artifact hash: $actualArtifactDigest"
