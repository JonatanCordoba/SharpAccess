# Business continuity and recovery

SharpAccess continuity depends on the Windows host, selected provider, secret stores, email/OIDC dependencies, and the host’s deployment and monitoring environment. This document defines minimum planning and repository evidence; it is not a service-level agreement.

## Required host objectives

Each host defines and approves:

- authentication recovery time objective;
- persistence and audit recovery point objective;
- maximum acceptable email and OIDC dependency outage;
- database, secret-store, signing-key, and Data Protection recovery procedures;
- rollback and fail-safe behavior;
- accountable operators and escalation paths.

Do not infer production objectives from samples or tests.

## SQLite exercise

The deterministic Windows drill:

1. initializes a file database;
2. creates and verifies an account;
3. closes all provider resources;
4. checkpoints and creates an offline backup;
5. simulates active-file loss;
6. restores the backup;
7. validates the restored provider;
8. proves login.

```powershell
./scripts/recovery-drill.ps1 -RepositoryRoot $PWD
```

Evidence is written under `artifacts/operations/recovery-drill`. This does not replace encrypted backups, off-site retention, integrity checks, online-backup procedures, or regular production exercises.

## PostgreSQL exercise

PostgreSQL remains internal until promotion. Release evidence requires the native Windows `pg_dump`/`pg_restore` drill against approved scratch databases:

```powershell
$env:SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING = '<approved scratch database connection string>'
$env:SHARPACCESS_PROVIDER_TEST_ALLOW_RESET = 'true'
./scripts/postgres-recovery-drill.ps1 -RepositoryRoot $PWD
```

Production PostgreSQL continuity must additionally address managed backups or snapshots, WAL/PITR where required, encryption, restore isolation, version compatibility, credentials, and application smoke validation.

SQL Server and MySQL are not active providers and have no current recovery claim. Future reintroduction requires new native Windows recovery evidence.

## Exercise policy

- Run repository drills within the cadence in `eng/OperationalReadiness.props`.
- Run provider-native restore exercises before launch and after material schema/infrastructure changes.
- Record revision, backup/restore times, outcome, operator, findings, and remediation using `docs/templates/RECOVERY-DRILL.md`.
- Protect backups at least as strongly as source data.
- Verify Data Protection keys, signing keys, token-hashing keys, and historical password peppers are recoverable.
- Test application rollback separately from database restoration.

A failed or expired drill blocks release-readiness claims until corrected and rerun. Do not weaken or bypass recovery evidence.
