using System.Diagnostics.CodeAnalysis;

namespace SharpAccess.Sqlite.Migrations;

[ExcludeFromCodeCoverage]
internal static class SqliteTokenHashVersionMigration
{
    internal const string Id = "010_token_hash_key_versions";
    internal const string Sql = """
        ALTER TABLE auth_refresh_tokens
            ADD COLUMN hash_key_version TEXT NOT NULL DEFAULT 'legacy';
        ALTER TABLE auth_email_verification_tokens
            ADD COLUMN hash_key_version TEXT NOT NULL DEFAULT 'legacy';
        ALTER TABLE auth_password_reset_tokens
            ADD COLUMN hash_key_version TEXT NOT NULL DEFAULT 'legacy';
        ALTER TABLE auth_oauth_exchange_codes
            ADD COLUMN hash_key_version TEXT NOT NULL DEFAULT 'legacy';
        ALTER TABLE auth_oauth_states
            ADD COLUMN hash_key_version TEXT NOT NULL DEFAULT 'legacy';

        CREATE INDEX ix_auth_refresh_tokens_hash_version
            ON auth_refresh_tokens(hash_key_version,token_hash);
        CREATE INDEX ix_auth_email_verification_tokens_hash_version
            ON auth_email_verification_tokens(hash_key_version,token_hash);
        CREATE INDEX ix_auth_password_reset_tokens_hash_version
            ON auth_password_reset_tokens(hash_key_version,token_hash);
        CREATE INDEX ix_auth_oauth_exchange_codes_hash_version
            ON auth_oauth_exchange_codes(hash_key_version,token_hash);
        CREATE INDEX ix_auth_oauth_states_hash_version
            ON auth_oauth_states(hash_key_version,state_hash);
        """;
}
