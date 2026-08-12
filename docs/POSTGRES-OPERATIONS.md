# PostgreSQL operational readiness

## Status

`SharpAccess.Postgres` is **Supported**. This document owns continuing PostgreSQL operational guidance. `POSTGRES-PROMOTION.md` and ADR 0021 retain historical promotion context; promotion is not pending.

## Supported environment

PostgreSQL engineering and release evidence runs on Windows with PowerShell 7 using either a native Windows PostgreSQL installation or an approved managed scratch database. Docker, Compose, service containers, Bash, Linux, and macOS are not supported evidence paths.

## Connection ownership and pooling

Connection-string registration creates a provider-owned `NpgsqlDataSource` for the service-provider lifetime. Logical connections open/dispose per operation. Host-supplied `NpgsqlDataSource` instances remain host-owned.

Provider-owned connection strings normalize session time zone to UTC and apply the SharpAccess application name when absent. Validation rejects unsafe/unbounded settings such as invalid pool bounds, excessive pool sizes, unbounded timeouts, disabled cancellation cleanup, detailed error/parameter logging, multiplexing, and session-reset bypass.

## Timeouts, cancellation, TLS, and native types

Connection, command, and cancellation timeouts are bounded positive values. Caller cancellation and command timeout remain independent controls.

Production PostgreSQL should use TLS with server identity validation, normally `SSL Mode=VerifyFull`, a trusted root, and a matching host name. Trust bypass and plaintext transport are diagnostics only.

Identifiers use `uuid`, booleans `boolean`, and UTC instants `timestamptz`; provider contracts verify lossless round trips.

## Transactions, migrations, and restricted principals

SharpAccess owns asynchronous begin/commit/rollback/disposal. SQLSTATE `40001` maps to serialization failure and `40P01` to deadlock. Rollback failures must not replace the original operation exception.

Runtime migration uses process coordination plus PostgreSQL advisory locking. Use separate migration and runtime principals; the readiness suite proves `ValidateOnly` works with the restricted runtime role and DDL fails as expected.

## Query plans and recovery

Real-engine `EXPLAIN (FORMAT JSON)` evidence must demonstrate the intended bounded keyset-query index behavior on the reviewed environment. This is a continuing Supported-provider obligation, not a one-time promotion artifact.

Native recovery uses `psql`, `createdb`, `dropdb`, `pg_dump`, and `pg_restore` with approved `sharpaccess_contract_tests_*` scratch databases. The drill seeds deterministic data, creates/hashes a custom-format dump, restores it, validates schema/data, emits redacted evidence, and cleans temporary resources.

```powershell
$env:SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING = '<approved scratch database connection string>'
$env:SHARPACCESS_PROVIDER_TEST_ALLOW_RESET = 'true'
$env:SHARPACCESS_POSTGRES_READINESS = 'true'

./scripts/postgres-promotion.ps1 -RepositoryRoot $PWD
```

The historical script name remains the aggregate entry point for applicable continuing PostgreSQL evidence. A future release revision must satisfy the then-current provider obligations; the published RC1 evidence is not a permanent verification of later code.
