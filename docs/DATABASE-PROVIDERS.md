# Database providers

`SharpAccess.Core` is provider-neutral. A consuming host installs Core plus exactly one Supported relational provider package. `eng/ProviderStatus.props` owns active status.

## Active provider matrix

| Provider | Status | Validation model |
|---|---|---|
| `SharpAccess.Sqlite` | Supported | Always-on zero-infrastructure contracts and recovery evidence. |
| `SharpAccess.Postgres` | Supported | Native/managed real-engine validation on Windows plus continuing recovery and operational evidence. |

SQL Server and MySQL are not active source projects or packages and remain future roadmap candidates only.

## Provider rules

- Register exactly one Supported relational provider.
- Core does not reference a concrete database client.
- Providers own their ADO.NET dependency, connection/data-source handling, SQL, schema, migrations, transactions, and error classification.
- Provider projects may reference Core but not one another.
- PostgreSQL exposes the reviewed public `AddPostgresAccess` registration and `PostgresAuthOptions` surface.
- Provider infrastructure such as SQL, stores, connection factories, dialects, transaction managers, and error classifiers remains internal.

## Windows and connection policy

SharpAccess supports Windows only. Repository tooling uses PowerShell 7 and does not use containers.

SQLite uses in-process native SQLite dependencies. PostgreSQL uses `Npgsql` with a native Windows PostgreSQL installation or approved managed database. Host-managed sources/factories remain host-owned; SharpAccess owns only logical per-operation connections as defined by the provider API.

Connection strings and credentials must not appear in retained evidence.

## Authorization, ownership, and migration contracts

Active providers implement the same provider-neutral global/tenant authorization split, tenant-keyed security joins, atomic ownership transfer, refresh/session invalidation, ordered immutable migrations, historical upgrades, and bounded keyset pagination using provider-native SQL.

PostgreSQL promotion is historical; future changed PostgreSQL release revisions still require applicable real-engine, query-plan, restricted-principal, coverage/mutation, recovery, package, and consumer evidence.
