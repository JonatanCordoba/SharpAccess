# Database providers

`SharpAccess.Core` is provider-neutral. A consuming host installs Core plus exactly one supported relational provider package.

`eng/ProviderStatus.props` owns active status.

## Active provider matrix

| Provider | Status | Validation model | Release role |
|---|---|---|---|
| `SharpAccess.Sqlite` | Supported | Always-on, zero-infrastructure validation. | Initial stable cohort and reference provider. |
| `SharpAccess.Postgres` | Supported | Native or managed real-engine validation on Windows. | Initial stable server-provider path. |

SQL Server and MySQL are not active source projects or packages. They remain future roadmap candidates only.

## Provider rules

- Register exactly one supported relational provider.
- Core must not reference a concrete database client.
- Active providers own their ADO.NET dependency, connection/data-source handling, SQL, schema, migrations, transactions, and error classification.
- Provider projects may reference Core but not one another.
- PostgreSQL exposes `AddPostgresAccess` and participates in supported package creation in the coordinated promotion revision.
- A provider is not accepted as Supported until every applicable evidence gate in `PROVIDER-STATUS.md` is complete on the exact revision.

## Windows and connection policy

SharpAccess supports Windows only. Repository tooling uses PowerShell 7 and does not use containers.

- SQLite uses in-process native SQLite dependencies.
- PostgreSQL uses `Npgsql` with a native Windows PostgreSQL installation or an approved managed database.
- Host-managed connections and data sources remain supported where defined by the provider API.
- Connection strings and credentials remain host-owned secrets and must not appear in retained evidence.

## Authorization schema contract

Active providers implement separate global and tenant catalogs:

| Scope | Required tables |
|---|---|
| Global | `auth_global_roles`, `auth_global_permissions`, `auth_global_role_permissions`, `auth_global_user_roles` |
| Tenant | `auth_tenant_roles`, `auth_tenant_permissions`, `auth_tenant_role_permissions`, `auth_tenant_user_roles`, `auth_tenant_owners` |

Tenant-owned primary, unique, and foreign keys include `tenant_id` where required. A provider must not join tenant roles or permissions by identifier without also matching the tenant.

`IAuthAuthorizationStore.GetEffectiveAuthorizationContextAsync` returns global and tenant data separately. Providers must not flatten those security domains.

## Ownership transaction contract

Each provider implements ownership transfer atomically:

1. serialize the persisted owner row;
2. verify the caller is the current owner;
3. verify the proposed owner is an active member of the same tenant;
4. update `auth_tenant_owners`;
5. update immutable `Owner` and fallback `Member` role assignments;
6. increment affected security versions and revoke active refresh sessions;
7. commit or roll back the complete operation.

Provider-specific locking is allowed, but observable behavior and failure semantics must match the shared contract.

## Migration contract

The immutable migration catalog includes the global/tenant authorization split, tenant owner role, cross-tenant grant correction, token-hash versioning, refresh-token authentication time, authorization reconciliation, and bounded-pagination indexes.

SQLite fixtures and PostgreSQL real-engine tests must prove historical upgrades preserve authorization and owner invariants.

## Source organization

Each active provider uses responsibility-bearing `Configuration`, `DependencyInjection`, `Persistence`, `Migrations`, `Stores`, `Internal`, and `Properties` directories. Mechanically identical internal registration mappings may be linked from `providers/Shared`; provider-specific behavior remains in the owning project.