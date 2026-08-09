#Requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ArtifactRoot,

    [Parameter(Mandatory)]
    [string] $Version,

    [string] $Source = 'https://api.nuget.org/v3/index.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-DotNet {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet exited with code $LASTEXITCODE while running: dotnet $($Arguments -join ' ')"
    }
}

$resolvedArtifactRoot = [System.IO.Path]::GetFullPath($ArtifactRoot)
if (-not (Test-Path -LiteralPath $resolvedArtifactRoot -PathType Container)) {
    throw "Artifact root does not exist: $resolvedArtifactRoot"
}

if ([string]::IsNullOrWhiteSpace($env:NUGET_API_KEY)) {
    throw 'NUGET_API_KEY is not available from the Trusted Publishing login step.'
}
if ([string]::IsNullOrWhiteSpace($env:NUGET_SYMBOL_API_KEY)) {
    throw 'NUGET_SYMBOL_API_KEY is not available for symbol publication.'
}

$packageIds = @('SharpAccess.Core', 'SharpAccess.Sqlite', 'SharpAccess.Postgres')
foreach ($packageId in $packageIds) {
    $packageName = "$packageId.$Version.nupkg"
    $symbolName = "$packageId.$Version.snupkg"
    $packageMatches = @(Get-ChildItem -LiteralPath $resolvedArtifactRoot -Recurse -File | Where-Object Name -CEQ $packageName)
    $symbolMatches = @(Get-ChildItem -LiteralPath $resolvedArtifactRoot -Recurse -File | Where-Object Name -CEQ $symbolName)

    if ($packageMatches.Count -ne 1) {
        throw "Expected exactly one package '$packageName', found $($packageMatches.Count)."
    }
    if ($symbolMatches.Count -ne 1) {
        throw "Expected exactly one symbol package '$symbolName', found $($symbolMatches.Count)."
    }
    Write-Host "Publishing $packageId $Version."
    Invoke-DotNet -Arguments @(
        'nuget',
        'push',
        $packageMatches[0].FullName,
        '--source',
        $Source,
        '--timeout',
        '300',
        '--force-english-output',
        '--no-symbols'
    )

    Write-Host "Publishing symbols for $packageId $Version."
    Invoke-DotNet -Arguments @(
        'nuget',
        'push',
        $symbolMatches[0].FullName,
        '--source',
        $Source,
        '--timeout',
        '300',
        '--force-english-output'
    )
}

Write-Host 'SharpAccess NuGet publication cohort completed in dependency order.'
