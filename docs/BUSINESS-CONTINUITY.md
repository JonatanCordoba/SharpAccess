# Business continuity and recovery

SharpAccess continuity depends on the Windows host, selected provider, secret stores, email/OIDC dependencies, deployment, and monitoring environment. This document defines minimum planning/evidence, not an SLA.

## Host objectives

Each host defines authentication RTO, persistence/audit RPO, tolerated email/OIDC outage, database/secret/signing/Data Protection recovery procedures, rollback/fail-safe behavior, and accountable escalation paths.

## SQLite exercise

The deterministic Windows drill initializes a file database, creates/verifies an account, quiesces provider resources, checkpoints and backs up the database, simulates loss, restores, validates the provider, and proves login:

```powershell
./scripts/recovery-drill.ps1 -RepositoryRoot $PWD
```

## PostgreSQL exercise

PostgreSQL is Supported. Native `pg_dump`/`pg_restore` recovery evidence is a continuing requirement on applicable provider/release revisions:

```powershell
$env:SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING = '<approved scratch database connection string>'
$env:SHARPACCESS_PROVIDER_TEST_ALLOW_RESET = 'true'
./scripts/postgres-recovery-drill.ps1 -RepositoryRoot $PWD
```

Production continuity must additionally address managed backups/snapshots, WAL/PITR where required, encryption, restore isolation, version compatibility, credentials, and application smoke validation.

SQL Server and MySQL are not active providers.

## Exercise policy

Run repository/provider-native exercises at the configured operational cadence and after material persistence/infrastructure changes. Record revision, backup/restore time, outcome, operator, findings, and remediation without secrets. Protect backups at least as strongly as source data and test application rollback separately from database restoration.

A failed or expired required drill blocks a new release-readiness claim until corrected. Published RC1 recovery evidence remains historical evidence for RC1 and is not rerun merely because documentation changed.
