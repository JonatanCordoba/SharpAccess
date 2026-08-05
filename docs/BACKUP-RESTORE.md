# Backup and restore

SharpAccess owns authentication data inside provider-owned `auth_*` objects. The consuming host owns schedules, retention, encryption, storage access, restore authorization, recovery objectives, and production tooling.

## Active provider status

- `SharpAccess.Sqlite`: Supported; deterministic offline recovery evidence is included.
- `SharpAccess.Postgres`: Supported; native logical backup/restore evidence is mandatory for its exact promotion revision and release evidence.

SQL Server and MySQL are not active providers. They have no active recovery scripts or release obligations.

## SQLite controlled offline backup

Writable file databases use WAL mode. A safe controlled copy must not ignore uncheckpointed WAL frames.

1. Stop or quiesce every writer.
2. Dispose application service providers and logical connections.
3. Open a non-pooled maintenance connection.
4. Run `PRAGMA wal_checkpoint(TRUNCATE);` and require success.
5. Run `PRAGMA integrity_check;` and require `ok`.
6. Copy the main database file to protected backup storage.
7. Retain required configuration and historical key versions separately.
8. Record revision, database identity, time, operator, and checksum without secrets.

For online backup, use the SQLite online backup API or a reviewed transactionally consistent snapshot procedure.

## SQLite restore

1. Stop the host.
2. Preserve failed files for investigation.
3. Restore into a protected empty location.
4. Remove stale `-wal` and `-shm` files only after verifying they are not part of the backup set.
5. Run `PRAGMA integrity_check;`.
6. Start in `ValidateOnly` migration mode.
7. Validate migration and checksum ledgers.
8. Run login, refresh, tenant-isolation, administration, and audit smoke checks.
9. Record outcome and recovery-point loss.

Keys, peppers, OAuth credentials, SMTP credentials, and Data Protection keys require independent protected recovery procedures.

Run the repository drill on Windows:

```powershell
./scripts/recovery-drill.ps1 -RepositoryRoot $PWD
```

Evidence is written under `artifacts/operations/recovery-drill`.

## PostgreSQL logical backup and restore

PostgreSQL recovery evidence uses native Windows PostgreSQL client tools and approved scratch databases. It does not use Docker or service containers.

Required commands on `PATH`:

- `psql`;
- `createdb`;
- `dropdb`;
- `pg_dump`;
- `pg_restore`.

Configure an approved connection and run:

```powershell
$env:SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING = '<approved scratch database connection string>'
$env:SHARPACCESS_PROVIDER_TEST_ALLOW_RESET = 'true'

./scripts/postgres-recovery-drill.ps1 -RepositoryRoot $PWD
```

The drill:

- creates separate `sharpaccess_contract_tests_recovery` and `_restored` databases;
- initializes and seeds deterministic data;
- writes a custom-format `pg_dump` archive;
- hashes the temporary archive;
- restores with `pg_restore`;
- validates schema, checksum ledger, and seeded data;
- writes redacted evidence to `artifacts/operations/postgres-recovery/postgres-recovery.json`;
- deletes temporary databases and archive.

This is release evidence, not a production recovery plan. Production PostgreSQL operation must separately define continuous backup or snapshots, WAL archiving/PITR where required, encryption, access control, retention, restore isolation, version compatibility, recovery objectives, operator review, and application smoke validation.