Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-SharpAccessRepositoryRoot {
    [CmdletBinding()]
    param([string]$Candidate)

    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        $Candidate = Join-Path $PSScriptRoot '..\..'
    }

    $resolved = (Resolve-Path -LiteralPath $Candidate).Path
    if (-not (Test-Path -LiteralPath (Join-Path $resolved 'SharpAccess.sln') -PathType Leaf)) {
        throw "Repository root is invalid: $resolved"
    }

    return $resolved
}

function Resolve-SharpAccessRepositoryPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$Path
    )

    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }

    return [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $Path))
}

function Get-SharpAccessVersion {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    $path = Join-Path $RepositoryRoot 'eng/Version.props'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Authoritative version file is missing: $path"
    }

    [xml]$document = Get-Content -LiteralPath $path -Raw
    $node = $document.SelectSingleNode('//SharpAccessVersion')
    if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) {
        throw "SharpAccessVersion is missing from $path"
    }

    $version = $node.InnerText.Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$') {
        throw "SharpAccessVersion is not a valid semantic version: $version"
    }

    return $version
}

function Get-SharpAccessRevision {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw 'Git is required for revision-bound evidence.'
    }

    $revision = @(& git -C $RepositoryRoot rev-parse HEAD)
    if ($LASTEXITCODE -ne 0 -or $revision.Count -ne 1 -or $revision[0] -notmatch '^[0-9a-fA-F]{40}$') {
        throw 'Unable to resolve the current full Git revision.'
    }

    return $revision[0].Trim().ToLowerInvariant()
}

function Invoke-SharpAccessDotNet {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$FailureMessage
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
}

function Write-SharpAccessUtf8NoBom {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Content
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    [IO.File]::WriteAllText(
        $Path,
        $Content.Replace("`r`n", "`n", [StringComparison]::Ordinal),
        [Text.UTF8Encoding]::new($false))
}

Export-ModuleMember -Function @(
    'Resolve-SharpAccessRepositoryRoot',
    'Resolve-SharpAccessRepositoryPath',
    'Get-SharpAccessVersion',
    'Get-SharpAccessRevision',
    'Invoke-SharpAccessDotNet',
    'Write-SharpAccessUtf8NoBom'
)
