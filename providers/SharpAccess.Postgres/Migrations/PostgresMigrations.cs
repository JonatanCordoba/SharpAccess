using System.Diagnostics.CodeAnalysis;

namespace SharpAccess.Postgres.Migrations;

internal sealed record PostgresMigration(string Id, string Sql);

[ExcludeFromCodeCoverage]
internal static class PostgresMigrations
{
    internal static PostgresMigration[] All { get; } =
    [
        new("001_initial_schema", InitialSchema),
        new("002_builtin_authorization_catalog", BuiltInAuthorizationCatalog),
        new("003_split_one_time_tokens", SplitOneTimeTokens),
        new("004_user_listing_created_index", UserListingCreatedIndex)
    ];

    private const string InitialSchema = """
        CREATE TABLE auth_users(
            id uuid PRIMARY KEY,
            email text NOT NULL,
            normalized_email text NOT NULL UNIQUE,
            password_hash text NULL,
            email_verified_utc timestamptz NULL,
            is_active boolean NOT NULL,
            failed_login_attempts integer NOT NULL DEFAULT 0 CHECK(failed_login_attempts >= 0),
            lockout_end_utc timestamptz NULL,
            security_version integer NOT NULL DEFAULT 1 CHECK(security_version > 0),
            created_utc timestamptz NOT NULL,
            updated_utc timestamptz NOT NULL
        );
        CREATE INDEX ix_auth_users_created ON auth_users(created_utc DESC,id);

        CREATE TABLE auth_roles(
            id uuid PRIMARY KEY,
            name text NOT NULL,
            normalized_name text NOT NULL UNIQUE,
            description text NOT NULL,
            is_system boolean NOT NULL,
            created_utc timestamptz NOT NULL
        );

        CREATE TABLE auth_permissions(
            id uuid PRIMARY KEY,
            name text NOT NULL UNIQUE,
            description text NOT NULL,
            created_utc timestamptz NOT NULL
        );

        CREATE TABLE auth_tenants(
            id uuid PRIMARY KEY,
            name text NOT NULL,
            slug text NOT NULL UNIQUE,
            created_utc timestamptz NOT NULL
        );

        CREATE TABLE auth_tenant_memberships(
            tenant_id uuid NOT NULL,
            user_id uuid NOT NULL,
            created_utc timestamptz NOT NULL,
            PRIMARY KEY(tenant_id,user_id),
            FOREIGN KEY(tenant_id) REFERENCES auth_tenants(id) ON DELETE CASCADE,
            FOREIGN KEY(user_id) REFERENCES auth_users(id) ON DELETE CASCADE
        );

        CREATE TABLE auth_user_roles(
            id uuid PRIMARY KEY,
            user_id uuid NOT NULL,
            role_id uuid NOT NULL,
            tenant_id uuid NULL,
            created_utc timestamptz NOT NULL,
            FOREIGN KEY(user_id) REFERENCES auth_users(id) ON DELETE CASCADE,
            FOREIGN KEY(role_id) REFERENCES auth_roles(id) ON DELETE CASCADE,
            FOREIGN KEY(tenant_id) REFERENCES auth_tenants(id) ON DELETE CASCADE
        );
        CREATE UNIQUE INDEX ux_auth_user_roles_scope_global
            ON auth_user_roles(user_id,role_id) WHERE tenant_id IS NULL;
        CREATE UNIQUE INDEX ux_auth_user_roles_scope_tenant
            ON auth_user_roles(user_id,role_id,tenant_id) WHERE tenant_id IS NOT NULL;
        CREATE INDEX ix_auth_user_roles_user ON auth_user_roles(user_id,tenant_id);

        CREATE TABLE auth_role_permissions(
            role_id uuid NOT NULL,
            permission_id uuid NOT NULL,
            created_utc timestamptz NOT NULL,
            PRIMARY KEY(role_id,permission_id),
            FOREIGN KEY(role_id) REFERENCES auth_roles(id) ON DELETE CASCADE,
            FOREIGN KEY(permission_id) REFERENCES auth_permissions(id) ON DELETE CASCADE
        );

        CREATE TABLE auth_refresh_tokens(
            id uuid PRIMARY KEY,
            user_id uuid NOT NULL,
            token_hash text NOT NULL UNIQUE,
            family_id uuid NOT NULL,
            security_version integer NOT NULL,
            ip_address text NULL,
            user_agent text NULL,
            created_utc timestamptz NOT NULL,
            expires_utc timestamptz NOT NULL,
            revoked_utc timestamptz NULL,
            replaced_by_token_id uuid NULL,
            FOREIGN KEY(user_id) REFERENCES auth_users(id) ON DELETE CASCADE,
            FOREIGN KEY(replaced_by_token_id) REFERENCES auth_refresh_tokens(id)
        );
        CREATE INDEX ix_auth_refresh_tokens_family ON auth_refresh_tokens(family_id);
        CREATE INDEX ix_auth_refresh_tokens_user ON auth_refresh_tokens(user_id,revoked_utc);

        CREATE TABLE auth_one_time_tokens(
            id uuid PRIMARY KEY,
            user_id uuid NOT NULL,
            purpose text NOT NULL,
            token_hash text NOT NULL UNIQUE,
            created_utc timestamptz NOT NULL,
            expires_utc timestamptz NOT NULL,
            consumed_utc timestamptz NULL,
            FOREIGN KEY(user_id) REFERENCES auth_users(id) ON DELETE CASCADE
        );
        CREATE INDEX ix_auth_one_time_tokens_user_purpose
            ON auth_one_time_tokens(user_id,purpose,consumed_utc);

        CREATE TABLE auth_oauth_accounts(
            id uuid PRIMARY KEY,
            user_id uuid NOT NULL,
            provider text NOT NULL,
            provider_subject text NOT NULL,
            created_utc timestamptz NOT NULL,
            FOREIGN KEY(user_id) REFERENCES auth_users(id) ON DELETE CASCADE,
            UNIQUE(provider,provider_subject)
        );
        CREATE INDEX ix_auth_oauth_accounts_user ON auth_oauth_accounts(user_id);

        CREATE TABLE auth_oauth_states(
            id uuid PRIMARY KEY,
            provider text NOT NULL,
            state_hash text NOT NULL UNIQUE,
            protected_code_verifier text NOT NULL,
            return_url text NOT NULL,
            created_utc timestamptz NOT NULL,
            expires_utc timestamptz NOT NULL,
            consumed_utc timestamptz NULL
        );
        CREATE INDEX ix_auth_oauth_states_expiry ON auth_oauth_states(expires_utc,consumed_utc);

        CREATE TABLE auth_security_audit_logs(
            id uuid PRIMARY KEY,
            created_utc timestamptz NOT NULL,
            event_type text NOT NULL,
            user_id uuid NULL,
            tenant_id uuid NULL,
            ip_address text NULL,
            user_agent text NULL,
            detail text NULL,
            FOREIGN KEY(user_id) REFERENCES auth_users(id) ON DELETE SET NULL,
            FOREIGN KEY(tenant_id) REFERENCES auth_tenants(id) ON DELETE SET NULL
        );
        CREATE INDEX ix_auth_audit_created ON auth_security_audit_logs(created_utc DESC);
        CREATE INDEX ix_auth_audit_user ON auth_security_audit_logs(user_id,created_utc DESC);
        CREATE INDEX ix_auth_audit_tenant ON auth_security_audit_logs(tenant_id,created_utc DESC);
        """;

    private const string BuiltInAuthorizationCatalog = """
        INSERT INTO auth_roles(id,name,normalized_name,description,is_system,created_utc) VALUES
            ('10000000-0000-0000-0000-000000000001'::uuid,'Admin','ADMIN','Full administrative access.',true,'2026-01-01T00:00:00.0000000+00:00'::timestamptz),
            ('10000000-0000-0000-0000-000000000002'::uuid,'User','USER','Standard authenticated user access.',true,'2026-01-01T00:00:00.0000000+00:00'::timestamptz),
            ('10000000-0000-0000-0000-000000000003'::uuid,'Manager','MANAGER','Delegated management access.',true,'2026-01-01T00:00:00.0000000+00:00'::timestamptz);

        INSERT INTO auth_permissions(id,name,description,created_utc) VALUES
            ('20000000-0000-0000-0000-000000000001'::uuid,'users.read','Read user records.','2026-01-01T00:00:00.0000000+00:00'::timestamptz),
            ('20000000-0000-0000-0000-000000000002'::uuid,'users.manage','Create and manage users.','2026-01-01T00:00:00.0000000+00:00'::timestamptz),
            ('20000000-0000-0000-0000-000000000003'::uuid,'roles.read','Read role records.','2026-01-01T00:00:00.0000000+00:00'::timestamptz),
            ('20000000-0000-0000-0000-000000000004'::uuid,'roles.manage','Create and manage roles.','2026-01-01T00:00:00.0000000+00:00'::timestamptz),
            ('20000000-0000-0000-0000-000000000005'::uuid,'permissions.read','Read permission records.','2026-01-01T00:00:00.0000000+00:00'::timestamptz),
            ('20000000-0000-0000-0000-000000000006'::uuid,'permissions.manage','Manage role permission assignments.','2026-01-01T00:00:00.0000000+00:00'::timestamptz),
            ('20000000-0000-0000-0000-000000000007'::uuid,'sessions.manage','Revoke user sessions.','2026-01-01T00:00:00.0000000+00:00'::timestamptz),
            ('20000000-0000-0000-0000-000000000008'::uuid,'audit.read','Read security audit records.','2026-01-01T00:00:00.0000000+00:00'::timestamptz),
            ('20000000-0000-0000-0000-000000000009'::uuid,'tenants.read','Read tenant records and members.','2026-01-01T00:00:00.0000000+00:00'::timestamptz),
            ('20000000-0000-0000-0000-000000000010'::uuid,'tenants.manage','Create and manage tenants.','2026-01-01T00:00:00.0000000+00:00'::timestamptz),
            ('20000000-0000-0000-0000-000000000011'::uuid,'profile.read','Read the current profile.','2026-01-01T00:00:00.0000000+00:00'::timestamptz),
            ('20000000-0000-0000-0000-000000000012'::uuid,'profile.update','Update the current profile.','2026-01-01T00:00:00.0000000+00:00'::timestamptz);

        INSERT INTO auth_role_permissions(role_id,permission_id,created_utc)
            SELECT '10000000-0000-0000-0000-000000000001'::uuid,id,'2026-01-01T00:00:00.0000000+00:00'::timestamptz
            FROM auth_permissions;

        INSERT INTO auth_role_permissions(role_id,permission_id,created_utc)
            SELECT '10000000-0000-0000-0000-000000000002'::uuid,id,'2026-01-01T00:00:00.0000000+00:00'::timestamptz
            FROM auth_permissions WHERE name IN ('profile.read','profile.update','tenants.read');

        INSERT INTO auth_role_permissions(role_id,permission_id,created_utc)
            SELECT '10000000-0000-0000-0000-000000000003'::uuid,id,'2026-01-01T00:00:00.0000000+00:00'::timestamptz
            FROM auth_permissions WHERE name IN (
                'users.read','roles.read','permissions.read','tenants.read','tenants.manage','profile.read','profile.update');
        """;

    private const string SplitOneTimeTokens = """
        CREATE TEMP TABLE auth_migration_003_guard(
            unsupported_purpose_count integer NOT NULL CHECK(unsupported_purpose_count = 0)
        ) ON COMMIT DROP;
        INSERT INTO auth_migration_003_guard(unsupported_purpose_count)
            SELECT COUNT(*) FROM auth_one_time_tokens
            WHERE purpose NOT IN ('email_verification','password_reset')
              AND purpose NOT LIKE 'oauth_exchange:%';

        CREATE TABLE auth_email_verification_tokens(
            id uuid PRIMARY KEY,
            user_id uuid NOT NULL,
            purpose text NOT NULL CHECK(purpose='email_verification'),
            token_hash text NOT NULL UNIQUE,
            created_utc timestamptz NOT NULL,
            expires_utc timestamptz NOT NULL,
            consumed_utc timestamptz NULL,
            FOREIGN KEY(user_id) REFERENCES auth_users(id) ON DELETE CASCADE
        );
        CREATE INDEX ix_auth_email_verification_tokens_user
            ON auth_email_verification_tokens(user_id,consumed_utc);

        CREATE TABLE auth_password_reset_tokens(
            id uuid PRIMARY KEY,
            user_id uuid NOT NULL,
            purpose text NOT NULL CHECK(purpose='password_reset'),
            token_hash text NOT NULL UNIQUE,
            created_utc timestamptz NOT NULL,
            expires_utc timestamptz NOT NULL,
            consumed_utc timestamptz NULL,
            FOREIGN KEY(user_id) REFERENCES auth_users(id) ON DELETE CASCADE
        );
        CREATE INDEX ix_auth_password_reset_tokens_user
            ON auth_password_reset_tokens(user_id,consumed_utc);

        CREATE TABLE auth_oauth_exchange_codes(
            id uuid PRIMARY KEY,
            user_id uuid NOT NULL,
            purpose text NOT NULL CHECK(purpose LIKE 'oauth_exchange:%'),
            token_hash text NOT NULL UNIQUE,
            created_utc timestamptz NOT NULL,
            expires_utc timestamptz NOT NULL,
            consumed_utc timestamptz NULL,
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
        """;

    private const string UserListingCreatedIndex = """
        CREATE INDEX IF NOT EXISTS ix_auth_users_created ON auth_users(created_utc DESC,id);
        """;
}
