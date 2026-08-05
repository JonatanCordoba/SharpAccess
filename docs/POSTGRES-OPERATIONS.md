# PostgreSQL operational readiness

## Status

`SharpAccess.Postgres` is **Supported** in the coordinated promotion revision. This document defines supported setup and operational evidence while `docs/POSTGRES-PROMOTION.md` owns the exact-revision promotion procedure.

## Supported environment

PostgreSQL engineering and release evidence runs on Windows with PowerShell 7. Use either:

- a native Windows PostgreSQL installation; or
- an approved managed scratch database.

Docker, Compose, service containers, and Bash are not supported.

## Connection ownership and pooling

Connection-string registration creates one provider-owned `NpgsqlDataSource` for the service-provider lifetime. Logical connections open and dispose per operation. Host-supplied `NpgsqlDataSource` instances remain host-owned. Host connection delegates retain the same one-logical-connection-per-operation contract.

Provider-owned connection strings normalize the session time zone to UTC and set `SharpAccess.Postgres` as the application name when absent. The validator rejects unsafe or unbounded settings, including invalid pool bounds, excessive pool sizes, unbounded timeouts, disabled cancellation cleanup, detailed error/parameter logging, multiplexing, and session-reset bypass.

## Timeouts, cancellation, and TLS

Connection, command, and cancellation timeouts must be bounded positive values. Caller cancellation and command timeout remain independent controls.

Production should use TLS with server identity validation, normally `SSL Mode=VerifyFull`, a trusted root, and a matching host name. Trust bypass and plaintext transport are local diagnostics only.

## Native types

Identifiers use `uuid`, booleans use `boolean`, and UTC instants use `timestamptz`. Provider contracts prove lossless GUID, Boolean, and UTC timestamp round trips.

## Transactions and concurrency

SharpAccess owns asynchronous begin, commit, rollback, and disposal. Rollback failures do not replace the original operation exception. SQLSTATE `40001` maps to serialization failure and `40P01` to deadlock. SharpAccess does not add hidden retries around multi-step authentication transactions.

## Migration serialization

Runtime migration uses a process lock plus transaction-level PostgreSQL advisory locking. Runtime acquisition fails closed when another owner holds the lock. Generated external scripts use a bounded local lock timeout before the blocking advisory lock.

## Restricted principals

Use separate principals:

- migration principal: connect/schema plus DDL/DML only for `auth_*` objects and ledgers;
- runtime principal: connect/schema plus required DML/read rights, without object creation or host-object access.

The readiness suite proves `ValidateOnly` succeeds through the restricted runtime role and DDL fails with SQLSTATE `42501`.

## Query plans

Real-engine `EXPLAIN (FORMAT JSON)` evidence must show keyset-pagination queries can use intended indexes. Evidence is tied to the reviewed PostgreSQL version and dataset and is not a permanent planner guarantee.

## Native backup and restore

Install the PostgreSQL client tools so `psql`, `createdb`, `dropdb`, `pg_dump`, and `pg_restore` are on `PATH`.

The drill uses source and restored databases beginning with `sharpaccess_contract_tests_`, initializes deterministic data, creates a custom-format dump, hashes it, restores it, validates schema/checksums/data, records redacted JSON, and removes both databases and the temporary archive.

```powershell
$env:SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING = '<approved scratch database connection string>'
$env:SHARPACCESS_PROVIDER_TEST_ALLOW_RESET = 'true'
./scripts/postgres-recovery-drill.ps1 -RepositoryRoot $PWD
```

The operator account may create/drop only approved scratch databases. Runtime credentials are insufficient.

## Evidence command

```powershell
$env:SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING = '<approved scratch database connection string>'
$env:SHARPACCESS_PROVIDER_TEST_ALLOW_RESET = 'true'
$env:SHARPACCESS_POSTGRES_READINESS = 'true'

./scripts/postgres-promotion.ps1 -RepositoryRoot $PWD
```

The PostgreSQL promotion gate requires public-surface approval, package creation and validation, consumer smoke, exact-revision provider contracts, coverage, mutation, and recovery evidence. Protected OIDC and the controlled performance baseline remain separate stable-release gates.