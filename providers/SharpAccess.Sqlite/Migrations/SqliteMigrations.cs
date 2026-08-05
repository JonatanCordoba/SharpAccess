using System.Diagnostics.CodeAnalysis;

namespace SharpAccess.Sqlite.Migrations;

internal sealed record SqliteMigration(string Id, string Sql);

[ExcludeFromCodeCoverage]
internal static class SqliteMigrations
{
    internal static SqliteMigration[] All { get; } =
    [
        new("001_initial_schema", InitialSchema),
        new("002_builtin_authorization_catalog", BuiltInAuthorizationCatalog),
        new("003_split_one_time_tokens", SplitOneTimeTokens),
        new("004_user_listing_created_index", UserListingCreatedIndex)
    ];

    private const string InitialSchema = """
        CREATE TABLE auth_users(
            id TEXT PRIMARY KEY,
            email TEXT NOT NULL,
            normalized_email TEXT NOT NULL UNIQUE,
            password_hash TEXT NULL,
            email_verified_utc TEXT NULL,
            is_active INTEGER NOT NULL CHECK(is_active IN (0,1)),
            failed_login_attempts INTEGER NOT NULL DEFAULT 0 CHECK(failed_login_attempts >= 0),
            lockout_end_utc TEXT NULL,
            security_version INTEGER NOT NULL DEFAULT 1 CHECK(security_version > 0),
            created_utc TEXT NOT NULL,
            updated_utc TEXT NOT NULL
        );
        CREATE INDEX ix_auth_users_created ON auth_users(created_utc DESC,id);

        CREATE TABLE auth_roles(
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            normalized_name TEXT NOT NULL UNIQUE,
            description TEXT NOT NULL,
            is_system INTEGER NOT NULL CHECK(is_system IN (0,1)),
            created_utc TEXT NOT NULL
        );

        CREATE TABLE auth_permissions(
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL UNIQUE,
            description TEXT NOT NULL,
            created_utc TEXT NOT NULL
        );

        CREATE TABLE auth_tenants(
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            slug TEXT NOT NULL UNIQUE,
            created_utc TEXT NOT NULL
        );

        CREATE TABLE auth_tenant_memberships(
            tenant_id TEXT NOT NULL,
            user_id TEXT NOT NULL,
            created_utc TEXT NOT NULL,
            PRIMARY KEY(tenant_id,user_id),
            FOREIGN KEY(tenant_id) REFERENCES auth_tenants(id) ON DELETE CASCADE,
            FOREIGN KEY(user_id) REFERENCES auth_users(id) ON DELETE CASCADE
        );

        CREATE TABLE auth_user_roles(
            id TEXT PRIMARY KEY,
            user_id TEXT NOT NULL,
            role_id TEXT NOT NULL,
            tenant_id TEXT NULL,
            created_utc TEXT NOT NULL,
            FOREIGN KEY(user_id) REFERENCES auth_users(id) ON DELETE CASCADE,
            FOREIGN KEY(role_id) REFERENCES auth_roles(id) ON DELETE CASCADE,
            FOREIGN KEY(tenant_id) REFERENCES auth_tenants(id) ON DELETE CASCADE
        );
        CREATE UNIQUE INDEX ux_auth_user_roles_scope
            ON auth_user_roles(user_id,role_id,IFNULL(tenant_id,''));
        CREATE INDEX ix_auth_user_roles_user ON auth_user_roles(user_id,tenant_id);

        CREATE TABLE auth_role_permissions(
            role_id TEXT NOT NULL,
            permission_id TEXT NOT NULL,
            created_utc TEXT NOT NULL,
            PRIMARY KEY(role_id,permission_id),
            FOREIGN KEY(role_id) REFERENCES auth_roles(id) ON DELETE CASCADE,
            FOREIGN KEY(permission_id) REFERENCES auth_permissions(id) ON DELETE CASCADE
        );

        CREATE TABLE auth_refresh_tokens(
            id TEXT PRIMARY KEY,
            user_id TEXT NOT NULL,
            token_hash TEXT NOT NULL UNIQUE,
            family_id TEXT NOT NULL,
            security_version INTEGER NOT NULL,
            ip_address TEXT NULL,
            user_agent TEXT NULL,
            created_utc TEXT NOT NULL,
            expires_utc TEXT NOT NULL,
            revoked_utc TEXT NULL,
            replaced_by_token_id TEXT NULL,
            FOREIGN KEY(user_id) REFERENCES auth_users(id) ON DELETE CASCADE,
            FOREIGN KEY(replaced_by_token_id) REFERENCES auth_refresh_tokens(id)
        );
        CREATE INDEX ix_auth_refresh_tokens_family ON auth_refresh_tokens(family_id);
        CREATE INDEX ix_auth_refresh_tokens_user ON auth_refresh_tokens(user_id,revoked_utc);

        CREATE TABLE auth_one_time_tokens(
            id TEXT PRIMARY KEY,
            user_id TEXT NOT NULL,
            purpose TEXT NOT NULL,
            token_hash TEXT NOT NULL UNIQUE,
            created_utc TEXT NOT NULL,
            expires_utc TEXT NOT NULL,
            consumed_utc TEXT NULL,
            FOREIGN KEY(user_id) REFERENCES auth_users(id) ON DELETE CASCADE
        );
        CREATE INDEX ix_auth_one_time_tokens_user_purpose
            ON auth_one_time_tokens(user_id,purpose,consumed_utc);

        CREATE TABLE auth_oauth_accounts(
            id TEXT PRIMARY KEY,
            user_id TEXT NOT NULL,
            provider TEXT NOT NULL,
            provider_subject TEXT NOT NULL,
            created_utc TEXT NOT NULL,
            FOREIGN KEY(user_id) REFERENCES auth_users(id) ON DELETE CASCADE,
            UNIQUE(provider,provider_subject)
        );
        CREATE INDEX ix_auth_oauth_accounts_user ON auth_oauth_accounts(user_id);

        CREATE TABLE auth_oauth_states(
            id TEXT PRIMARY KEY,
            provider TEXT NOT NULL,
            state_hash TEXT NOT NULL UNIQUE,
            protected_code_verifier TEXT NOT NULL,
            return_url TEXT NOT NULL,
            created_utc TEXT NOT NULL,
            expires_utc TEXT NOT NULL,
            consumed_utc TEXT NULL
        );
        CREATE INDEX ix_auth_oauth_states_expiry ON auth_oauth_states(expires_utc,consumed_utc);

        CREATE TABLE auth_security_audit_logs(
            id TEXT PRIMARY KEY,
            created_utc TEXT NOT NULL,
            event_type TEXT NOT NULL,
            user_id TEXT NULL,
            tenant_id TEXT NULL,
            ip_address TEXT NULL,
            user_agent TEXT NULL,
            detail TEXT NULL,
            FOREIGN KEY(user_id) REFERENCES auth_users(id) ON DELETE SET NULL,
            FOREIGN KEY(tenant_id) REFERENCES auth_tenants(id) ON DELETE SET NULL
        );
        CREATE INDEX ix_auth_audit_created ON auth_security_audit_logs(created_utc DESC);
        CREATE INDEX ix_auth_audit_user ON auth_security_audit_logs(user_id,created_utc DESC);
        CREATE INDEX ix_auth_audit_tenant ON auth_security_audit_logs(tenant_id,created_utc DESC);
        """;

    private const string SplitOneTimeTokens = """
        CREATE TEMP TABLE auth_migration_003_guard(
            unsupported_purpose_count INTEGER NOT NULL CHECK(unsupported_purpose_count = 0)
        );
        INSERT INTO auth_migration_003_guard(unsupported_purpose_count)
            SELECT COUNT(*) FROM auth_one_time_tokens
            WHERE purpose NOT IN ('email_verification','password_reset')
              AND purpose NOT LIKE 'oauth_exchange:%';

        CREATE TABLE auth_email_verification_tokens(
            id TEXT PRIMARY KEY,
            user_id TEXT NOT NULL,
            purpose TEXT NOT NULL CHECK(purpose='email_verification'),
            token_hash TEXT NOT NULL UNIQUE,
            created_utc TEXT NOT NULL,
            expires_utc TEXT NOT NULL,
            consumed_utc TEXT NULL,
            FOREIGN KEY(user_id) REFERENCES auth_users(id) ON DELETE CASCADE
        );
        CREATE INDEX ix_auth_email_verification_tokens_user
            ON auth_email_verification_tokens(user_id,consumed_utc);

        CREATE TABLE auth_password_reset_tokens(
            id TEXT PRIMARY KEY,
            user_id TEXT NOT NULL,
            purpose TEXT NOT NULL CHECK(purpose='password_reset'),
            token_hash TEXT NOT NULL UNIQUE,
            created_utc TEXT NOT NULL,
            expires_utc TEXT NOT NULL,
            consumed_utc TEXT NULL,
            FOREIGN KEY(user_id) REFERENCES auth_users(id) ON DELETE CASCADE
        );
        CREATE INDEX ix_auth_password_reset_tokens_user
            ON auth_password_reset_tokens(user_id,consumed_utc);

        CREATE TABLE auth_oauth_exchange_codes(
            id TEXT PRIMARY KEY,
            user_id TEXT NOT NULL,
            purpose TEXT NOT NULL CHECK(purpose LIKE 'oauth_exchange:%'),
            token_hash TEXT NOT NULL UNIQUE,
            created_utc TEXT NOT NULL,
            expires_utc TEXT NOT NULL,
            consumed_utc TEXT NULL,
            FOREIGN KEY(user_id) REFERENCES auth_users(id) ON DELETE CASCADE
        );
        CREATE INDEX ix_auth_oauth_exchange_codes_user_purpose
            ON auth_oauth_exchange_codes(user_id,purpose,consumed_utc);

        INSERT INTO auth_email_verification_tokens
            SELECT * FROM auth_one_time_tokens WHERE purpose='email_verification';
        INSERT INTO auth_password_reset_tokens
            SELECT * FROM auth_one_time_tokens WHERE purpose='password_reset';
        INSERT INTO auth_oauth_exchange_codes
            SELECT * FROM auth_one_time_tokens WHERE purpose LIKE 'oauth_exchange:%';

        DROP TABLE auth_one_time_tokens;
        DROP TABLE auth_migration_003_guard;
        """;

    private const string UserListingCreatedIndex = """
        CREATE INDEX IF NOT EXISTS ix_auth_users_created ON auth_users(created_utc DESC,id);
        """;

    private const string BuiltInAuthorizationCatalog = """
        INSERT INTO auth_roles(id,name,normalized_name,description,is_system,created_utc) VALUES
            ('10000000-0000-0000-0000-000000000001','Admin','ADMIN','Full administrative access.',1,'2026-01-01T00:00:00.0000000+00:00'),
            ('10000000-0000-0000-0000-000000000002','User','USER','Standard authenticated user access.',1,'2026-01-01T00:00:00.0000000+00:00'),
            ('10000000-0000-0000-0000-000000000003','Manager','MANAGER','Delegated management access.',1,'2026-01-01T00:00:00.0000000+00:00');

        INSERT INTO auth_permissions(id,name,description,created_utc) VALUES
            ('20000000-0000-0000-0000-000000000001','users.read','Read user records.','2026-01-01T00:00:00.0000000+00:00'),
            ('20000000-0000-0000-0000-000000000002','users.manage','Create and manage users.','2026-01-01T00:00:00.0000000+00:00'),
            ('20000000-0000-0000-0000-000000000003','roles.read','Read role records.','2026-01-01T00:00:00.0000000+00:00'),
            ('20000000-0000-0000-0000-000000000004','roles.manage','Create and manage roles.','2026-01-01T00:00:00.0000000+00:00'),
            ('20000000-0000-0000-0000-000000000005','permissions.read','Read permission records.','2026-01-01T00:00:00.0000000+00:00'),
            ('20000000-0000-0000-0000-000000000006','permissions.manage','Manage role permission assignments.','2026-01-01T00:00:00.0000000+00:00'),
            ('20000000-0000-0000-0000-000000000007','sessions.manage','Revoke user sessions.','2026-01-01T00:00:00.0000000+00:00'),
            ('20000000-0000-0000-0000-000000000008','audit.read','Read security audit records.','2026-01-01T00:00:00.0000000+00:00'),
            ('20000000-0000-0000-0000-000000000009','tenants.read','Read tenant records and members.','2026-01-01T00:00:00.0000000+00:00'),
            ('20000000-0000-0000-0000-000000000010','tenants.manage','Create and manage tenants.','2026-01-01T00:00:00.0000000+00:00'),
            ('20000000-0000-0000-0000-000000000011','profile.read','Read the current profile.','2026-01-01T00:00:00.0000000+00:00'),
            ('20000000-0000-0000-0000-000000000012','profile.update','Update the current profile.','2026-01-01T00:00:00.0000000+00:00');

        INSERT INTO auth_role_permissions(role_id,permission_id,created_utc)
            SELECT '10000000-0000-0000-0000-000000000001',id,'2026-01-01T00:00:00.0000000+00:00'
            FROM auth_permissions;

        INSERT INTO auth_role_permissions(role_id,permission_id,created_utc)
            SELECT '10000000-0000-0000-0000-000000000002',id,'2026-01-01T00:00:00.0000000+00:00'
            FROM auth_permissions WHERE name IN ('profile.read','profile.update','tenants.read');

        INSERT INTO auth_role_permissions(role_id,permission_id,created_utc)
            SELECT '10000000-0000-0000-0000-000000000003',id,'2026-01-01T00:00:00.0000000+00:00'
            FROM auth_permissions WHERE name IN (
                'users.read','roles.read','permissions.read','tenants.read','tenants.manage','profile.read','profile.update');
        """;
}
