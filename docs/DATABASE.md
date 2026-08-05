# Database schema

Active providers apply ordered migrations and record them in `auth_schema_migrations`. Initialization is serialized to prevent concurrent migration races. SQLite and PostgreSQL implement the same provider-neutral ownership and security contracts using provider-native SQL.

## Principal tables

- `auth_users`: normalized email, password metadata, verification, lockout, active state, and security version.
- `auth_global_roles`, `auth_global_permissions`, `auth_global_role_permissions`, `auth_global_user_roles`: global authorization.
- `auth_tenants`, `auth_tenant_memberships`, `auth_tenant_roles`, `auth_tenant_permissions`, assignment tables, and `auth_tenant_owners`: tenant membership, authorization, and ownership.
- `auth_refresh_tokens`: keyed hashes, family/replacement links, authentication time, security version, bounded network metadata, expiry, and revocation.
- `auth_email_verification_tokens`, `auth_password_reset_tokens`, `auth_oauth_exchange_codes`: keyed, expiring, single-purpose token hashes.
- `auth_oauth_states`: protected state/verifier context, nonce, return URL, and expiry.
- `auth_oauth_accounts`: external subject linkage.
- `auth_security_audit_logs`: bounded event, actor, tenant, network metadata, details, and timestamp.
- `auth_schema_migration_checksums`: immutable migration integrity evidence.

User deletion is not exposed; deactivation preserves audit and authorization history.

## Token storage

Passwords use Argon2id with random salt and a recorded pepper version. Refresh, verification, reset, state, and exchange tokens are not stored in plaintext. Lookup tokens use HMAC-SHA-256 under a versioned host-owned key.

## Migration policy

Do not edit an applied migration. Add an ordered migration and test empty creation plus historical upgrade. Production uses a restricted migration principal and starts in `ValidateOnly` with a runtime principal.

Migration `012_pagination_indexes` aligns bounded lists with keyset order:

- users, audit records, global roles, and global permissions: `(created_utc DESC, id ASC)`;
- tenant lists: `(user_id, created_utc DESC, tenant_id ASC)`;
- tenant members: `(tenant_id, created_utc DESC, user_id ASC)`.

Queries select at most `limit + 1` logical items and construct the next cursor from the last emitted item. Tenant pagination intentionally uses membership creation time. Callers must not infer provider-independent GUID ordering.

SQL Server and MySQL have no active schema or migration implementation. Future providers must establish new schema, historical-upgrade, query-plan, and recovery evidence.

## Backup and restore

SQLite backups must handle WAL consistently. PostgreSQL uses provider-native logical or managed backup procedures. Keep signing keys, token-hashing keys, password peppers, OAuth credentials, SMTP credentials, and Data Protection keys outside the database with separate recovery controls.
