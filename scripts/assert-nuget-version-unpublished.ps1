#Requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$packageIds = @('SharpAccess.Core', 'SharpAccess.Sqlite', 'SharpAccess.Postgres')
$normalizedVersion = $Version.ToLowerInvariant()

foreach ($packageId in $packageIds) {
    $lowerId = $packageId.ToLowerInvariant()
    $uri = "https://api.nuget.org/v3-flatcontainer/$lowerId/index.json"

    try {
        $index = Invoke-RestMethod -Method Get -Uri $uri -Headers @{ Accept = 'application/json' }
    }
    catch {
        $response = $_.Exception.Response
        if ($null -ne $response -and [int]$response.StatusCode -eq 404) {
            Write-Host "NuGet package ID '$packageId' has no published versions."
            continue
        }
        throw
    }

    $publishedVersions = @($index.versions | ForEach-Object { ([string]$_).ToLowerInvariant() })
    if ($normalizedVersion -in $publishedVersions) {
        throw "NuGet already contains '$packageId' version '$Version'. Stop and review publication integrity before any retry."
    }

    Write-Host "NuGet version is available: $packageId $Version"
}

Write-Host 'All SharpAccess RC package versions are currently unpublished.'
