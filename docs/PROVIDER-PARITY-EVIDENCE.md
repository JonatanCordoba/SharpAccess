# Provider evidence and release classification

## Purpose

This document classifies execution evidence for the active Core, SQLite, and PostgreSQL cohort. Repository controls are not successful evidence until the exact reviewed revision produces a retained passing result.

`eng/ProviderStatus.props` owns active status.

## Active release impact

| Package or provider | Current status | Stable 1.0 impact |
|---|---|---|
| `SharpAccess.Core` | Supported | Required. |
| `SharpAccess.Sqlite` | Supported | Required and the zero-infrastructure reference path. |
| `SharpAccess.Postgres` | Supported | Required initial stable server-provider path; exact-revision promotion evidence remains mandatory. |

SQL Server and MySQL are not active providers. Their previous implementations and evidence obligations were removed. They remain future roadmap candidates in `docs/ROADMAP.md`; no unresolved SQL Server or MySQL item blocks the initial release.

## Evidence vocabulary

- **Initial-release required**: must pass before Core, SQLite, or PostgreSQL stable publication.
- **Environment-blocked**: the control exists, but required infrastructure or protected configuration is unavailable. This is not success.
- **Not applicable**: the capability does not apply, with the reason recorded.
- **Repository control present**: implementation exists; execution has not been proven.
- **Retained execution evidence required**: the exact revision must complete the command or workflow and retain its result.

## Capability matrix

| Capability | SQLite | PostgreSQL |
|---|---|---|
| Shared provider contracts | Always available and initial-release required. | Real-engine run required; selected execution must not skip. |
| Historical upgrades | SQLite fixtures and migration matrix required. | Real-engine historical upgrade evidence required. |
| Restricted principals | File and operating-system access guidance applies. | Required with `SHARPACCESS_POSTGRES_READINESS=true`. |
| Query-plan evidence | SQLite query-plan tests required. | Native `EXPLAIN (FORMAT JSON)` evidence required. |
| Recovery | Deterministic SQLite offline recovery required. | Native `pg_dump`/`pg_restore` drill required. |
| Provider coverage | Supported-provider threshold required. | Promotion threshold required for the coordinated support revision and retained as provider evidence. |
| Package consumer | Required. | Required from the coordinated promotion revision onward. |
| Public registration | `AddSqliteAccess` supported. | `AddPostgresAccess` supported in the coordinated promotion revision. |
| Operational documentation | Required. | Required through `POSTGRES-OPERATIONS.md` and `POSTGRES-PROMOTION.md`. |

## Windows execution boundary

Provider evidence is produced on Windows with PowerShell 7. Docker, Compose, service containers, and Bash are not part of the evidence model.

PostgreSQL tests use either a native Windows PostgreSQL installation or an approved managed scratch database. Recovery evidence additionally requires native `psql`, `createdb`, `dropdb`, `pg_dump`, and `pg_restore` tools.

## Required commands

```powershell
./scripts/verify-local.ps1 -RepositoryRoot $PWD
```

```powershell
$env:SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING = '<approved scratch database connection string>'
$env:SHARPACCESS_PROVIDER_TEST_ALLOW_RESET = 'true'
$env:SHARPACCESS_POSTGRES_READINESS = 'true'

./scripts/postgres-promotion.ps1 -RepositoryRoot $PWD
```

Never retain credentials or connection strings in logs or evidence.

## Completion boundary

The PostgreSQL provider phase is evidence-complete only when the exact committed promotion revision has:

1. successful real-engine contracts, restricted-principal readiness, historical upgrades, concurrency, cancellation, timeout, SQLSTATE, and query-plan evidence;
2. successful promotion coverage, PostgreSQL-specific mutation, native recovery, package validation, package-consumer, and SBOM evidence;
3. no provider-status, public-registration, package-catalog, or API-baseline drift;
4. retained artifact locations, hashes, and reviewer approval.

Protected OIDC, controlled performance, canonical export, and publication remain independent initial-release gates.