# Public API

SharpAccess currently exposes a deliberately small, scope-explicit Windows package surface. The published prerelease is `0.9.0-rc.1`; stable `1.0.0` is future work.

## Core contracts

The reviewed Core surface includes configuration/migration/signing/password-risk/token/OIDC/rate-limit/security options; signing-key-ring contracts; rate-limit partition abstraction; schema status/migration operations; bounded paging; keyed OIDC options; middleware/security-header options; email/password-risk abstractions; global/tenant role-permission constants; scope-explicit authorization attributes; and Minimal API/initialization extensions.

Pre-v1 `DotNetAuth` aliases and unscoped authorization attributes are not part of the reviewed SharpAccess surface.

## Bounded collections

Administrative users, audit logs, roles, permissions, caller tenants, and tenant members use opaque forward-only cursor pagination with a reviewed 1–200 range and bounded/tamper-safe validation. Offset pagination is not part of the contract. Multi-instance hosts must share durable Data Protection keys for cursor continuity.

## Provider surface

`SharpAccess.Sqlite` exposes the approved SQLite options and `AddSqliteAccess` registration surface.

`SharpAccess.Postgres` is Supported and exposes the approved `PostgresAuthOptions` and `AddPostgresAccess` registration surface. SQL, migrations, stores, connection factories, dialects, transaction managers, and error classifiers remain internal.

SQL Server/MySQL package IDs, namespaces, options, and registrations are absent and deferred.

## Platform and enforcement

The supported platform is Windows; repository scripts/operational commands use PowerShell 7. No Bash/Linux/macOS/container consumer contract is provided.

`eng/public-api` owns reviewed exported-type baselines for active assemblies, and package tests reject accidental or retired surface changes. A future stable API freeze requires its own explicit stable stage.
