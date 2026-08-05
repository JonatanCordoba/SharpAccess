# SharpAccess migrations

SharpAccess owns only `auth_*` objects and its migration ledgers. Migration execution must not modify host-owned objects.

## Modes

`AuthOptions.Migrations.Mode` supports:

- `ApplyAtStartup`: apply pending provider migrations before use.
- `ValidateOnly`: read-only ledger/checksum validation; fail on missing, pending, unknown, or modified schema.
- `External`: perform no startup schema operation; deployment automation must migrate first.
- `GenerateScript`: produce provider-native SQL without executing it.

When unset, Development/Test use `ApplyAtStartup`; Production, Staging, and unknown environments use `ValidateOnly`.

## Explicit APIs

```csharp
await app.Services.MigrateSharpAccessAsync(cancellationToken);
await app.Services.ValidateSharpAccessSchemaAsync(cancellationToken);
SharpAccessSchemaStatus status = await app.Services.GetSharpAccessSchemaStatusAsync(cancellationToken);
string script = await app.Services.GenerateSharpAccessMigrationScriptAsync(cancellationToken);
```

Status output never contains SQL, connection details, or user data.

## Immutable catalog

Published migration IDs and SQL are immutable. `auth_schema_migration_checksums` stores normalized SHA-256 values. Validation fails when applied SQL changes, checksums are missing, or unknown migration IDs appear.

The active SQLite and PostgreSQL catalogs include:

- global/tenant authorization separation;
- immutable tenant ownership;
- cross-tenant grant correction;
- reconciliation reports containing aggregate counts only;
- token-hash key version tags without raw secrets;
- refresh-token original authentication time;
- bounded keyset-pagination indexes.

SQLite historical fixtures and PostgreSQL real-engine upgrade tests protect compatibility.

## Principals

Where supported, separate:

1. a migration principal that may create/alter SharpAccess-owned objects and write ledgers;
2. a runtime principal with only required DML/read permissions plus ledger reads.

Production `ValidateOnly` must work with the restricted runtime principal.

## Repository tool

The checked-in migration CLI currently supports SQLite on Windows:

```powershell
./scripts/sharpaccess-migrations.ps1 -Command migrate -ConnectionString 'Data Source=auth.db' -RepositoryRoot $PWD
./scripts/sharpaccess-migrations.ps1 -Command validate -ConnectionString 'Data Source=auth.db' -RepositoryRoot $PWD
./scripts/sharpaccess-migrations.ps1 -Command status -ConnectionString 'Data Source=auth.db' -RepositoryRoot $PWD
./scripts/sharpaccess-migrations.ps1 -Command script -ConnectionString 'Data Source=auth.db' -OutputPath ./artifacts/sharpaccess.sql -RepositoryRoot $PWD
```

PostgreSQL owns its migration dialect and real-engine catalog, but public migration-tool exposure remains blocked until promotion.

SQL Server and MySQL have no active migration projects or scripts. Future implementations require a new ADR and complete historical-upgrade evidence.

## Recovery

SQLite applies ledger bootstrap, checksum baselines, DDL, and ledger records transactionally. PostgreSQL uses provider-native transactional DDL and migration locking. Failed attempts must preserve the original exception, roll back completely, and remain retryable.

Before deployment: back up the database, review generated SQL, migrate using the migration principal, validate using the runtime principal, and retain sanitized status evidence.
