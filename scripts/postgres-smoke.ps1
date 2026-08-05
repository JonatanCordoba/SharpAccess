#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$Configuration = "Release",
    [switch]$NoBuild,
    [switch]$RequireConfigured
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$arguments = @{
    Configuration = $Configuration
}

if (-not [string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $arguments.RepositoryRoot = $RepositoryRoot
}
if ($NoBuild) {
    $arguments.NoBuild = $true
}
if ($RequireConfigured) {
    $arguments.RequireConfigured = $true
}

& (Join-Path $PSScriptRoot "provider-contracts.ps1") @arguments
