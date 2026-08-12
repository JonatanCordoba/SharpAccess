# SharpAccess migrations

SharpAccess owns only `auth_*` objects and its migration ledgers. Migration execution must not modify host-owned objects.

## Modes

`AuthOptions.Migrations.Mode` supports `ApplyAtStartup`, `ValidateOnly`, `External`, and `GenerateScript`. Production/Staging/unknown environments default to fail-safe validation behavior as defined by current options policy.

Published migration IDs and SQL are immutable. `auth_schema_migration_checksums` stores normalized SHA-256 values; validation fails on modified/applied checksum drift, missing required checksums, or unknown migration IDs.

The active SQLite and PostgreSQL catalogs cover global/tenant authorization separation, immutable tenant ownership, cross-tenant grant correction, token-hash versioning, refresh authentication time, reconciliation, and bounded keyset-pagination indexes. Historical fixtures/real-engine upgrade tests protect compatibility.

## Principals and recovery

Where supported, separate a migration principal capable of SharpAccess-owned DDL/DML from a restricted runtime principal. Production `ValidateOnly` must work with the restricted runtime principal.

SQLite and PostgreSQL each own their provider-native migration dialect, serialization/locking, transaction, and rollback behavior. PostgreSQL public provider/migration behavior is already part of the Supported package; it is no longer blocked on promotion.

The checked-in migration CLI currently exposes the repository-supported commands documented by its PowerShell wrapper. Keep connection strings out of source and logs.

SQL Server and MySQL have no active migration projects/scripts. Future implementations require a new ADR and complete historical-upgrade evidence.
