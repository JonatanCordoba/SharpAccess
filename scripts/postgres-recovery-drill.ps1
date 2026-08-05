#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [string]$ConnectionString,
    [string]$SourceDatabase = "sharpaccess_contract_tests_recovery",
    [string]$RestoredDatabase = "sharpaccess_contract_tests_recovery_restored",
    [switch]$NoRestore,
    [switch]$NoBuild
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-RepositoryRoot([string]$Candidate) {
    if ([string]::IsNullOrWhiteSpace($Candidate)) { $Candidate = Join-Path $PSScriptRoot ".." }
    $resolved = (Resolve-Path -LiteralPath $Candidate).Path
    if (-not (Test-Path -LiteralPath (Join-Path $resolved "SharpAccess.sln") -PathType Leaf)) { throw "Repository root is invalid: $resolved" }
    return $resolved
}
function Get-ConnectionValue([string]$Value, [string[]]$Names, [string]$DefaultValue = "") {
    foreach ($segment in $Value.Split(";", [StringSplitOptions]::RemoveEmptyEntries)) {
        $pair = $segment.Split("=", 2)
        if ($pair.Count -eq 2 -and $Names -contains $pair[0].Trim()) { return $pair[1].Trim() }
    }
    return $DefaultValue
}
function Assert-RecoveryDatabaseName([string]$Name, [string]$ExpectedSuffix) {
    if (-not $Name.StartsWith("sharpaccess_contract_tests_", [StringComparison]::Ordinal) -or -not $Name.EndsWith($ExpectedSuffix, [StringComparison]::Ordinal)) {
        throw "PostgreSQL recovery databases must use approved sharpaccess_contract_tests_* names. Database=$Name"
    }
}
function Invoke-Native([string]$Command, [string[]]$Arguments, [string]$Failure) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Failure ExitCode=$LASTEXITCODE" }
}
function Get-ClientArguments([string]$HostName, [int]$Port, [string]$Username) {
    return @("--host", $HostName, "--port", $Port.ToString([Globalization.CultureInfo]::InvariantCulture), "--username", $Username)
}
function New-TestConnectionString([string]$HostName, [int]$Port, [string]$Username, [string]$Password, [string]$Database) {
    return "Host=$HostName;Port=$Port;Database=$Database;Username=$Username;Password=$Password;Timeout=15;Command Timeout=30;Cancellation Timeout=2000;Pooling=false"
}
function Restore-EnvironmentValue([string]$Name, [string]$Value) {
    if ([string]::IsNullOrEmpty($Value)) { Remove-Item "Env:$Name" -ErrorAction SilentlyContinue } else { [Environment]::SetEnvironmentVariable($Name, $Value, "Process") }
}

if (-not [OperatingSystem]::IsWindows()) { throw "PostgreSQL release recovery evidence is supported on Windows only." }
$root = Resolve-RepositoryRoot $RepositoryRoot
if ([string]::IsNullOrWhiteSpace($ConnectionString)) { $ConnectionString = $env:SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING }
if ([string]::IsNullOrWhiteSpace($ConnectionString)) { throw "SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING or -ConnectionString is required." }
if (-not [string]::Equals($env:SHARPACCESS_PROVIDER_TEST_ALLOW_RESET, "true", [StringComparison]::OrdinalIgnoreCase)) { throw "SHARPACCESS_PROVIDER_TEST_ALLOW_RESET=true is required for the PostgreSQL recovery drill." }
$configuredDatabase = Get-ConnectionValue $ConnectionString @("Database", "Initial Catalog")
if ($configuredDatabase -ne "sharpaccess_contract_tests" -and -not $configuredDatabase.StartsWith("sharpaccess_contract_tests_", [StringComparison]::Ordinal)) { throw "The PostgreSQL recovery connection must target an approved scratch database." }
Assert-RecoveryDatabaseName $SourceDatabase "_recovery"
Assert-RecoveryDatabaseName $RestoredDatabase "_restored"
if ($SourceDatabase -eq $RestoredDatabase) { throw "Source and restored PostgreSQL recovery databases must differ." }
$hostName = Get-ConnectionValue $ConnectionString @("Host", "Server") "127.0.0.1"
$portText = Get-ConnectionValue $ConnectionString @("Port") "5432"
$username = Get-ConnectionValue $ConnectionString @("Username", "User ID", "UserId", "User")
$password = Get-ConnectionValue $ConnectionString @("Password", "Pwd")
$port = 0
if (-not [int]::TryParse($portText, [Globalization.NumberStyles]::Integer, [Globalization.CultureInfo]::InvariantCulture, [ref]$port) -or $port -lt 1 -or $port -gt 65535) { throw "PostgreSQL recovery port is invalid." }
if ([string]::IsNullOrWhiteSpace($username) -or [string]::IsNullOrWhiteSpace($password)) { throw "PostgreSQL recovery credentials are incomplete." }
foreach ($tool in @("psql", "createdb", "dropdb", "pg_dump", "pg_restore")) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool is required for the PostgreSQL recovery drill." }
}
$project = Join-Path $root "tests/SharpAccess.ProviderContractTests/SharpAccess.ProviderContractTests.csproj"
$artifacts = Join-Path $root "artifacts/operations/postgres-recovery"
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
$dump = Join-Path ([IO.Path]::GetTempPath()) "sharpaccess-postgres-recovery-$([Guid]::NewGuid().ToString('N')).dump"
$client = Get-ClientArguments $hostName $port $username
$maintenanceClient = $client + @("--maintenance-db", "postgres")
$previousPassword = $env:PGPASSWORD
$previousConnection = $env:SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING
$previousReset = $env:SHARPACCESS_PROVIDER_TEST_ALLOW_RESET
$previousRecovery = $env:SHARPACCESS_POSTGRES_RECOVERY_VERIFY
$operationFailure = $null
$cleanupFailures = [Collections.Generic.List[string]]::new()
try {
    $env:PGPASSWORD = $password
    if (-not $NoRestore) { Invoke-Native dotnet @("restore", $project, "--locked-mode") "PostgreSQL recovery drill restore failed." }
    if (-not $NoBuild) { Invoke-Native dotnet @("build", $project, "--configuration", $Configuration, "--no-restore", "-warnaserror") "PostgreSQL recovery drill build failed." }
    Invoke-Native "dropdb" ($maintenanceClient + @("--if-exists", "--force", $RestoredDatabase)) "Unable to remove the prior restored recovery database."
    Invoke-Native "dropdb" ($maintenanceClient + @("--if-exists", "--force", $SourceDatabase)) "Unable to remove the prior source recovery database."
    Invoke-Native "createdb" ($maintenanceClient + @("--template", "template0", $SourceDatabase)) "Unable to create the PostgreSQL recovery source database."
    $env:SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING = New-TestConnectionString $hostName $port $username $password $SourceDatabase
    $env:SHARPACCESS_PROVIDER_TEST_ALLOW_RESET = "true"
    Invoke-Native dotnet @("test", $project, "--configuration", $Configuration, "--no-restore", "--no-build", "--filter", "FullyQualifiedName=SharpAccess.ProviderContractTests.PostgresProviderContractTests.InitializationIsIdempotentAndSeedsAuthorizationCatalogOnce") "PostgreSQL recovery source initialization failed."
    $seedSql = "INSERT INTO auth_users(id,email,normalized_email,password_hash,email_verified_utc,is_active,failed_login_attempts,lockout_end_utc,security_version,created_utc,updated_utc) VALUES(gen_random_uuid(),'recovery@sharpaccess.local','RECOVERY@SHARPACCESS.LOCAL',NULL,CURRENT_TIMESTAMP,true,0,NULL,1,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP);"
    Invoke-Native "psql" ($client + @("--dbname", $SourceDatabase, "--set", "ON_ERROR_STOP=1", "--command", $seedSql)) "Unable to seed the PostgreSQL recovery source database."
    Invoke-Native "pg_dump" ($client + @("--format=custom", "--file", $dump, "--dbname", $SourceDatabase)) "PostgreSQL recovery dump failed."
    # SHA-256 records backup-artifact integrity; it is not used for password or secret derivation.
    $checksum = (Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash.ToLowerInvariant()
    Invoke-Native "createdb" ($maintenanceClient + @("--template", "template0", $RestoredDatabase)) "Unable to create the PostgreSQL restored database."
    Invoke-Native "pg_restore" ($client + @("--exit-on-error", "--no-owner", "--no-privileges", "--dbname", $RestoredDatabase, $dump)) "PostgreSQL recovery restore failed."
    $env:SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING = New-TestConnectionString $hostName $port $username $password $RestoredDatabase
    $env:SHARPACCESS_POSTGRES_RECOVERY_VERIFY = "true"
    Invoke-Native dotnet @("test", $project, "--configuration", $Configuration, "--no-restore", "--no-build", "--filter", "FullyQualifiedName=SharpAccess.ProviderContractTests.PostgresOperationalContractTests.RestoredDatabaseContainsCurrentSchemaAndRecoveryUser") "PostgreSQL restored-database verification failed."
    [ordered]@{ schemaVersion = 1; control = "postgres-logical-backup-restore"; provider = "SharpAccess.Postgres"; mode = "native-pg_dump-and-pg_restore"; sourceDatabase = $SourceDatabase; restoredDatabase = $RestoredDatabase; dumpSha256 = $checksum; status = "passed"; credentials = "redacted"; configuration = $Configuration; completedUtc = [DateTimeOffset]::UtcNow.ToString("O") } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $artifacts "postgres-recovery.json") -Encoding utf8 # DevSkim: ignore DS197836
}
catch { $operationFailure = $_; throw }
finally {
    foreach ($database in @($RestoredDatabase, $SourceDatabase)) {
        try { Invoke-Native "dropdb" ($maintenanceClient + @("--if-exists", "--force", $database)) "Unable to clean PostgreSQL recovery database $database." } catch { $cleanupFailures.Add($_.Exception.Message) }
    }
    Remove-Item -LiteralPath $dump -Force -ErrorAction SilentlyContinue
    Restore-EnvironmentValue "PGPASSWORD" $previousPassword
    Restore-EnvironmentValue "SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING" $previousConnection
    Restore-EnvironmentValue "SHARPACCESS_PROVIDER_TEST_ALLOW_RESET" $previousReset
    Restore-EnvironmentValue "SHARPACCESS_POSTGRES_RECOVERY_VERIFY" $previousRecovery
    if ($cleanupFailures.Count -gt 0) {
        $cleanupMessage = "PostgreSQL recovery cleanup failed: $($cleanupFailures -join ' | ')"
        if ($null -eq $operationFailure) { throw $cleanupMessage }
        Write-Warning $cleanupMessage
    }
}
Write-Host "PostgreSQL recovery drill passed. Evidence: artifacts/operations/postgres-recovery/postgres-recovery.json"
$global:LASTEXITCODE = 0
