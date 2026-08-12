# Provider evidence and release classification

## Purpose

This document classifies continuing execution evidence for the active Core, SQLite, and PostgreSQL cohort. `eng/ProviderStatus.props` owns current status.

Repository controls are not successful execution evidence until the exact reviewed revision produces the required passing result.

## Active cohort

| Package/provider | Current status | Continuing evidence role |
|---|---|---|
| `SharpAccess.Core` | Supported | Required in active package, quality, security, and release evidence. |
| `SharpAccess.Sqlite` | Supported | Always-on zero-infrastructure reference/provider evidence. |
| `SharpAccess.Postgres` | Supported | Real-engine contracts, coverage, mutations, migrations, restricted principals, query plans, recovery, package and consumer evidence on applicable revisions. |

SQL Server and MySQL are deferred and absent from the active implementation. Reintroduction requires a new ADR and full evidence plan.

## Evidence vocabulary

- **Required**: must pass for the selected scope/revision.
- **Environment-blocked**: required infrastructure/configuration is unavailable; this is not success.
- **Not run by request**: deliberately omitted from exploratory execution; this is not success.
- **Not applicable**: the capability does not apply, with an explicit reason.
- **Repository control present**: implementation exists; execution has not yet been proven for the selected revision.
- **Retained execution evidence**: the exact revision completed the required command/workflow and retained its result.

## Provider capability matrix

| Capability | SQLite | PostgreSQL |
|---|---|---|
| Provider contracts | Always available | Required real-engine run when selected |
| Historical upgrades | Fixture matrix | Real-engine historical upgrades |
| Restricted principals | File/OS guidance | Required readiness evidence |
| Query plans | SQLite query-plan evidence | Native `EXPLAIN (FORMAT JSON)` evidence |
| Recovery | Deterministic offline drill | Native `pg_dump`/`pg_restore` drill |
| Coverage | Supported-provider threshold | Supported-provider threshold and aggregate evidence |
| Mutations | Reference-provider invariants | PostgreSQL-specific invariants plus shared scope |
| Package consumer | Required | Required |
| Public registration | `AddSqliteAccess` | `AddPostgresAccess` |

## Windows boundary

Provider evidence is produced on Windows with PowerShell 7. Docker, Compose, service containers, Bash, Linux, and macOS are not part of the supported evidence model.

PostgreSQL uses a native Windows installation or an approved managed scratch database. Recovery additionally requires native `psql`, `createdb`, `dropdb`, `pg_dump`, and `pg_restore` tools.

## Historical promotion versus continuing support

PostgreSQL promotion to Supported is historical and is documented by `POSTGRES-PROMOTION.md` and ADR 0021. The retained script name `postgres-promotion.ps1` is historical terminology; its aggregate checks remain useful as continuing Supported-provider evidence.

RC1 proved the published RC1 revision. Future changed release revisions must satisfy the then-applicable continuing PostgreSQL evidence again; RC1 evidence is not a permanent verification of later code.
