# Public API

SharpAccess 1.0 exposes a deliberately small, scope-explicit Windows package surface.

## Core contracts

The reviewed Core surface includes:

- `AuthOptions` and nested migration, signing, password-risk, token, OIDC, rate-limit, and bounded-security options;
- `IAccessTokenSigningKeyRing`, `AccessTokenSigningKey`, and `AccessTokenVerificationKey`;
- `IAuthRateLimitPartitionKeyProvider`;
- `SharpAccessSchemaStatus` and explicit migration/validation/status operations;
- `SharpAccessPageRequest` and `SharpAccessPage<T>`;
- keyed `OpenIdConnectProviderOptions` and bounded client-authentication methods;
- middleware and security-header options;
- email and password-risk abstractions;
- global and tenant role/permission constants;
- explicit global, active-tenant, and owner authorization attributes;
- Minimal API mapping and initialization extensions.

Pre-v1 `DotNetAuth` aliases and unscoped authorization attributes are not part of 1.0.

## Bounded collections

Administrative users, audit logs, roles, permissions, caller tenants, and tenant members use opaque forward-only cursor pagination.

- Default page size: 100.
- Allowed range: 1–200.
- Invalid, oversized, tampered, expired-key, cross-collection, cross-user, or cross-tenant cursors return sanitized `400 invalid_page` before a provider call.
- Successful responses have `{ "items": [...], "nextCursor": "..." }`.
- Cursors are opaque Data Protection values and must be returned unchanged.
- Multi-instance hosts must share and durably persist Data Protection keys.
- Offset pagination is not part of the stable contract.

## Provider surface

`SharpAccess.Sqlite` exposes only the approved SQLite options and `AddSqliteAccess` registration surface.

`SharpAccess.Postgres` exposes the approved `PostgresAuthOptions` and `AddPostgresAccess` registration surface in the coordinated promotion revision. SQL, migrations, stores, connection factories, dialects, transaction managers, error classifiers, and other provider infrastructure remain internal.

SQL Server and MySQL package IDs, namespaces, options, and registration methods are not active public API. They remain future roadmap candidates only.

## Platform contract

Published 1.0 support is Windows-only. Repository scripts and supported operational commands use PowerShell 7. No Bash, Linux, macOS, or container-oriented consumer contract is provided.

## Enforcement

Files under `eng/public-api` define reviewed exported type baselines for active assemblies. Package tests compare built assemblies with those files and reject removed aliases, accidental provider registrations, and retired SQL Server/MySQL surface.