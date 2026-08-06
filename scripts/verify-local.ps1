#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$RepositoryUrl = "https://github.com/JonatanCordoba/SharpAccess",
    [string]$Version
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Import-Module (Join-Path $PSScriptRoot "SharpAccess.Build/SharpAccess.Build.psd1") -Force

# Invokes the release dry run as the single full local verification entrypoint.
function Invoke-ReleaseDryRun([string]$Root, [string]$Url, [string]$RequestedVersion) {
    $arguments = @(
        "-NoLogo",
        "-NoProfile",
        "-File",
        (Join-Path $Root "scripts/release-dry-run.ps1"),
        "-RepositoryRoot",
        $Root,
        "-RepositoryUrl",
        $Url,
        "-Version",
        $RequestedVersion
    )

    & pwsh @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Full local verification failed with exit code $LASTEXITCODE."
    }
}

$root = Resolve-SharpAccessRepositoryRoot $RepositoryRoot
$authoritativeVersion = Get-SharpAccessVersion -RepositoryRoot $root
$requestedVersion = if ([string]::IsNullOrWhiteSpace($Version)) { $authoritativeVersion } else { $Version.Trim() }
if ($requestedVersion -ne $authoritativeVersion) {
    throw "Requested verification version $requestedVersion does not match authoritative version $authoritativeVersion."
}
Invoke-ReleaseDryRun -Root $root -Url $RepositoryUrl -RequestedVersion $requestedVersion
