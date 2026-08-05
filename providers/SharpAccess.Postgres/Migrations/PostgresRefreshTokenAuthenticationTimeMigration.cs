using System.Diagnostics.CodeAnalysis;

namespace SharpAccess.Postgres.Migrations;

[ExcludeFromCodeCoverage]
internal static class PostgresRefreshTokenAuthenticationTimeMigration
{
    internal const string Id = "011_refresh_token_authenticated_utc";
    internal const string Sql = """
        ALTER TABLE auth_refresh_tokens
            ADD COLUMN authenticated_utc timestamptz NULL;

        UPDATE auth_refresh_tokens
        SET authenticated_utc=created_utc;

        ALTER TABLE auth_refresh_tokens
            ALTER COLUMN authenticated_utc SET NOT NULL;
        """;
}
