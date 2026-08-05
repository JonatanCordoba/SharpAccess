#Requires -Version 7.0
[CmdletBinding()] param([string]$RepositoryRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$root = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { (Resolve-Path (Join-Path $PSScriptRoot "..")).Path } else { (Resolve-Path $RepositoryRoot).Path }
Set-Location $root
& dotnet restore SharpAccess.sln --use-lock-file --force-evaluate
if ($LASTEXITCODE -ne 0) { throw "Lock-file refresh failed." }
& dotnet restore SharpAccess.sln --locked-mode
if ($LASTEXITCODE -ne 0) { throw "Locked restore verification failed." }
Write-Host "NuGet lock files refreshed and verified."
