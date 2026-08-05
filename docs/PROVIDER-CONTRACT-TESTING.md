# Provider-contract testing

This document defines the Windows-only validation procedure for the active SQLite and PostgreSQL providers. It does not promote PostgreSQL by itself.

## Safety boundary

PostgreSQL contract tests reset provider-owned `auth_*` objects. They require both:

1. a connection string targeting `sharpaccess_contract_tests` or a database beginning with `sharpaccess_contract_tests_`;
2. `SHARPACCESS_PROVIDER_TEST_ALLOW_RESET=true`.

Use a dedicated scratch database and a least-privilege test account scoped to that database. Never commit connection strings or place them in scripts, project files, test settings, workflow source, logs, or retained evidence.

## Supported environment

- Windows.
- PowerShell 7.
- .NET 10.
- No Bash, Docker, Compose, or service containers.
- PostgreSQL through a native Windows installation or approved managed scratch database.

## SQLite contracts

SQLite contracts are always available:

```powershell
dotnet test tests/SharpAccess.ProviderContractTests/SharpAccess.ProviderContractTests.csproj `
  --configuration Release `
  --filter "Provider=Sqlite"
```

The shared suite covers registration, migrations, transaction behavior, token rotation, authorization, tenancy, pagination, cancellation, and provider-neutral failure semantics.

## PostgreSQL contracts

```powershell
$env:SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING = `
    'Host=localhost;Database=sharpaccess_contract_tests;Username=sharpaccess;Password=<secret>'
$env:SHARPACCESS_PROVIDER_TEST_ALLOW_RESET = 'true'
$env:SHARPACCESS_POSTGRES_READINESS = 'true'

./scripts/provider-contracts.ps1 `
    -RepositoryRoot $PWD `
    -RequireConfigured
```

`-RequireConfigured` makes a missing connection string fail instead of being classified as environment-blocked.

The PostgreSQL suite includes bounded provider settings, native type round trips, cancellation, timeout and error classification, advisory-lock contention, historical upgrades, restricted principals, keyset query-plan evidence, and the shared behavioral contracts.

## Coverage and promotion

```powershell
./scripts/provider-coverage.ps1 `
    -RepositoryRoot $PWD `
    -Provider Postgres `
    -PromotionGate
```

A promotion run uses the reviewed promotion thresholds in `eng/ProviderCoverage.props`. Coverage is one required input, not sufficient promotion evidence.

## Native recovery

Install PostgreSQL client tools so `psql`, `createdb`, `dropdb`, `pg_dump`, and `pg_restore` are available on `PATH`, then run:

```powershell
./scripts/postgres-recovery-drill.ps1 -RepositoryRoot $PWD
```

The drill creates separate approved source and restored scratch databases, seeds deterministic data, performs a custom-format logical backup, restores it, verifies schema and data, records hashes without credentials, and cleans both databases.

## CI behavior

`.github/workflows/provider-contracts.yml` uses Windows runners only.

- SQLite runs for applicable pull requests.
- PostgreSQL runs only with protected configuration on non-PR evidence paths.
- Native PostgreSQL client tools are installed on the Windows runner.
- No service container is created.
- A selected PostgreSQL run fails rather than silently skipping.

## Pagination contract

Active providers implement separate first-page and continuation query shapes. Continuation uses a sargable tuple seek equivalent to:

```text
created_utc < afterCreated
OR (created_utc = afterCreated AND id > afterId)
```

The continuation order must match the initial order. Offset pagination and nullable-boundary guard shapes are not accepted.

## Promotion boundary

PostgreSQL becomes Supported only after complete implementation, public-surface approval, package creation, consumer smoke validation, migrations, recovery, operational documentation, coverage, security review, and exact-revision release-candidate evidence are complete and `eng/ProviderStatus.props` is intentionally changed.

SQL Server and MySQL are not active provider-test targets. Their future reintroduction requirements are recorded in `docs/ROADMAP.md` and ADR 0020.
