# Backup and restore

SharpAccess owns authentication data inside provider-owned `auth_*` objects. The consuming host owns schedules, retention, encryption, storage access, restore authorization, recovery objectives, and production tooling.

## Active provider status

- `SharpAccess.Sqlite`: Supported; deterministic offline recovery evidence is included.
- `SharpAccess.Postgres`: Supported; native logical backup/restore evidence is a continuing obligation on applicable future provider/release revisions.

SQL Server and MySQL are not active providers.

## SQLite controlled backup/restore

Writable file databases use WAL mode. A controlled backup must quiesce writers, dispose provider resources, checkpoint WAL, run `PRAGMA integrity_check`, copy the protected main database, and retain required configuration/key material separately. Restore into a protected location, validate integrity/migration ledgers, and run authentication/tenant/admin/audit smoke checks.

```powershell
./scripts/recovery-drill.ps1 -RepositoryRoot $PWD
```

## PostgreSQL logical backup/restore

PostgreSQL recovery evidence uses native Windows tools (`psql`, `createdb`, `dropdb`, `pg_dump`, `pg_restore`) and approved `sharpaccess_contract_tests_*` scratch databases; it does not use Docker or service containers.

```powershell
$env:SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING = '<approved scratch database connection string>'
$env:SHARPACCESS_PROVIDER_TEST_ALLOW_RESET = 'true'
./scripts/postgres-recovery-drill.ps1 -RepositoryRoot $PWD
```

The drill creates deterministic source/restored databases, writes/hashes a custom-format dump, restores it, validates schema/checksum/data, emits redacted evidence, and cleans temporary resources.

Repository recovery evidence is not a production recovery plan. Production PostgreSQL must separately define managed backup/snapshot/WAL-PITR policy as appropriate, encryption, access control, retention, restore isolation, compatibility, RPO/RTO, operator review, and application smoke validation.
