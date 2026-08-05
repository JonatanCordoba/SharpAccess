#Requires -Version 7.0
[CmdletBinding()]
param([string]$RepositoryRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Import-Module (Join-Path $PSScriptRoot "SharpAccess.Build/SharpAccess.Build.psd1") -Force

# Prevents package smoke work from creating generated files inside the repository.
function Assert-SmokeRootOutsideRepository([string]$Root, [string]$SmokeRoot) {
    $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $smokeFull = [System.IO.Path]::GetFullPath($SmokeRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if ($smokeFull.StartsWith($rootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Package smoke root must be outside the repository to avoid modifying repository build inputs. SmokeRoot=$SmokeRoot"
    }
}

if (-not [OperatingSystem]::IsWindows()) { throw "SharpAccess package smoke is supported on Windows only." }
$root = Resolve-SharpAccessRepositoryRoot $RepositoryRoot
. (Join-Path $root "scripts/provider-status.ps1")
$packages = Join-Path $root "artifacts/packages"
if (-not (Test-Path -LiteralPath $packages -PathType Container)) {
    throw "Package artifacts are missing. Run scripts/pack.ps1 first."
}

$supportedPackages = @(Get-SharpAccessSupportedPackageCatalog -RepositoryRoot $root)
$supportedIds = @($supportedPackages.PackageId | Sort-Object)
$expectedIds = @("SharpAccess.Core", "SharpAccess.Postgres", "SharpAccess.Sqlite")
if (Compare-Object -ReferenceObject $expectedIds -DifferenceObject $supportedIds) {
    throw "Package consumer smoke must be updated before changing the supported package set. Supported=$($supportedIds -join ', ')"
}

$version = Get-SharpAccessVersion -RepositoryRoot $root
foreach ($package in $supportedPackages) {
    $runtime = Join-Path $packages "$($package.PackageId).$version.nupkg"
    if (-not (Test-Path -LiteralPath $runtime -PathType Leaf)) {
        throw "Missing supported package artifact for $($package.PackageId) $version."
    }
}

$smokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) "sharpaccess-package-smoke/windows"
Assert-SmokeRootOutsideRepository -Root $root -SmokeRoot $smokeRoot
Remove-Item -LiteralPath $smokeRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $smokeRoot -Force | Out-Null
$previousLocation = (Get-Location).Path
$previousNuGetPackages = [Environment]::GetEnvironmentVariable("NUGET_PACKAGES", "Process")
$env:NUGET_PACKAGES = Join-Path $smokeRoot ".nuget/packages"
try {
    Set-Location -LiteralPath $smokeRoot
    Invoke-SharpAccessDotNet -Arguments @("new", "web", "--framework", "net10.0", "--no-https", "--name", "PackageConsumer") -FailureMessage "Failed to create package consumer app."
    Set-Location -LiteralPath (Join-Path $smokeRoot "PackageConsumer")

    $projectFile = Join-Path (Get-Location).Path "PackageConsumer.csproj"
    $projectText = Get-Content -LiteralPath $projectFile -Raw
    $projectText = $projectText.Replace("<PropertyGroup>", "<PropertyGroup>`n    <NuGetAudit>false</NuGetAudit>")
    Set-Content -LiteralPath $projectFile -Value $projectText -Encoding UTF8

    $nugetConfigPath = "NuGet.Config"
    Set-Content -LiteralPath $nugetConfigPath -Encoding UTF8 -Value '<?xml version="1.0" encoding="utf-8"?>'
    Add-Content -LiteralPath $nugetConfigPath -Encoding UTF8 -Value '<configuration>'
    Add-Content -LiteralPath $nugetConfigPath -Encoding UTF8 -Value '  <packageSources>'
    Add-Content -LiteralPath $nugetConfigPath -Encoding UTF8 -Value '    <clear />'
    Add-Content -LiteralPath $nugetConfigPath -Encoding UTF8 -Value ('    <add key="local-sharpaccess" value="{0}" />' -f $packages)
    Add-Content -LiteralPath $nugetConfigPath -Encoding UTF8 -Value '    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />'
    Add-Content -LiteralPath $nugetConfigPath -Encoding UTF8 -Value '  </packageSources>'
    Add-Content -LiteralPath $nugetConfigPath -Encoding UTF8 -Value '</configuration>'

    foreach ($package in $supportedPackages) {
        Invoke-SharpAccessDotNet -Arguments @("add", "package", $package.PackageId, "--version", $version) -FailureMessage "Failed to add $($package.PackageId) package."
    }

    $program = @(
    'using Microsoft.Extensions.Configuration;',
    'using Microsoft.Extensions.DependencyInjection;',
    '',
    'WebApplicationBuilder builder = WebApplication.CreateBuilder(args);',
    '',
    'builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>',
    '{',
    '    ["SharpAccess:BaseUri"] = "https://localhost/auth",',
    '    ["SharpAccess:JwtIssuer"] = "package-smoke",',
    '    ["SharpAccess:JwtAudience"] = "package-smoke-clients",',
    '    ["SharpAccess:JwtSigningKey"] = "SMOKE-JWT-SIGNING-KEY-12345678901234567890",',
    '    ["SharpAccess:TokenHashing:Key"] = "SMOKE-TOKEN-HASHING-KEY-12345678901234567890",',
    '    ["SharpAccess:RateLimits:PartitionKey"] = "SMOKE-RATE-LIMIT-PARTITION-KEY-123456789012345",',
    '    ["SharpAccess:Passwords:CurrentPepperVersion"] = "v1",',
    '    ["SharpAccess:Passwords:Peppers:v1"] = "SMOKE-PASSWORD-PEPPER-12345678901234567890",',
    '    ["SharpAccess:Sqlite:ConnectionString"] = "Data Source=package-smoke.db"',
    '});',
    '',
    'builder.Services.AddSharpAccess(builder.Configuration, options =>',
    '{',
    '    options.Features.PasswordAuthentication = true;',
    '    options.Features.Registration = true;',
    '    options.Features.PasswordReset = true;',
    '    options.Features.RefreshTokens = true;',
    '    options.Features.Administration = true;',
    '    options.Features.Tenancy = true;',
    '    options.OpenIdConnect.Providers["google"].Enabled = false;',
    '});',
    '',
    'builder.Services.AddSqliteAccess(builder.Configuration, options =>',
    '{',
    '    options.ConnectionString = "Data Source=package-smoke-override.db";',
    '});',
    '',
    'ServiceCollection postgresServices = new();',
    'postgresServices.AddPostgresAccess(options =>',
    '{',
    '    options.ConnectionString = "Host=localhost;Database=sharpaccess_package_smoke;Username=unused;Password=unused;Timeout=1;Command Timeout=1";',
    '});',
    '',
    'WebApplication app = builder.Build();',
    'app.UseSharpAccess();',
    'app.MapSharpAccessEndpoints();',
    'app.MapGet("/health", () => Results.Ok("ok"));',
    'app.Run();'
    )
    Set-Content -LiteralPath "Program.cs" -Value $program -Encoding UTF8

    if (Select-String -LiteralPath $projectFile -Pattern "ProjectReference" -Quiet) {
        throw "Smoke app must consume packages, not project references."
    }
    Invoke-SharpAccessDotNet -Arguments @("restore", "--configfile", "NuGet.Config") -FailureMessage "Package consumer restore failed."
    Invoke-SharpAccessDotNet -Arguments @("build", "--configuration", "Release", "--no-restore", "-warnaserror") -FailureMessage "Package consumer build failed."
}
finally {
    Set-Location -LiteralPath $previousLocation
    [Environment]::SetEnvironmentVariable("NUGET_PACKAGES", $previousNuGetPackages, "Process")
}
