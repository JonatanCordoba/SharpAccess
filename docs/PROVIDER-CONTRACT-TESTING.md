# Provider-contract testing

This document defines the Windows-only validation procedure for the active Supported SQLite and PostgreSQL providers.

## Safety boundary

PostgreSQL contract tests may reset provider-owned `auth_*` objects only when the database is an approved `sharpaccess_contract_tests*` scratch database and `SHARPACCESS_PROVIDER_TEST_ALLOW_RESET=true`. Keep connection strings and credentials outside source, logs, workflow source, and retained evidence.

## SQLite

```powershell
dotnet test tests/SharpAccess.ProviderContractTests/SharpAccess.ProviderContractTests.csproj `
  --configuration Release `
  --filter "Provider=Sqlite"
```

SQLite contracts are always available and cover shared persistence behavior, migrations, transactions, token rotation, authorization, tenancy, pagination, cancellation, and failure semantics.

## PostgreSQL

```powershell
$env:SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING = '<approved scratch database connection string>'
$env:SHARPACCESS_PROVIDER_TEST_ALLOW_RESET = 'true'
$env:SHARPACCESS_POSTGRES_READINESS = 'true'

./scripts/provider-contracts.ps1 -RepositoryRoot $PWD -RequireConfigured
```

`-RequireConfigured` makes missing required PostgreSQL configuration fail rather than being mistaken for passing evidence.

The PostgreSQL suite covers shared behavior plus native types, bounded settings, cancellation/timeouts/error classification, advisory-lock contention, historical upgrades, restricted principals, keyset query plans, concurrency, and provider-specific regressions.

## Coverage, mutation, and recovery

`eng/ProviderCoverage.props` owns provider thresholds. PostgreSQL is Supported, so applicable future release revisions require its configured provider coverage plus the relevant mutation and native recovery evidence.

The retained `-PromotionGate` switch and `postgres-promotion.ps1` name are historical compatibility names; they do not mean PostgreSQL promotion is still pending.

Native recovery requires `psql`, `createdb`, `dropdb`, `pg_dump`, and `pg_restore` on Windows:

```powershell
./scripts/postgres-recovery-drill.ps1 -RepositoryRoot $PWD
```

## CI behavior

`.github/workflows/provider-contracts.yml` uses Windows runners only. SQLite participates in applicable pull requests; PostgreSQL real-engine work runs only on protected evidence paths with approved configuration. No service container is created.

SQL Server and MySQL are not active provider-test targets. Future reintroduction requires a new ADR and a complete new evidence boundary.
