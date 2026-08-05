using System.Diagnostics.CodeAnalysis;

namespace SharpAccess.Sqlite.Migrations;

[ExcludeFromCodeCoverage]
internal static class SqliteRefreshTokenAuthenticationTimeMigration
{
    internal const string Id = "011_refresh_token_authenticated_utc";
    internal const string Sql = """
        ALTER TABLE auth_refresh_tokens
            ADD COLUMN authenticated_utc TEXT NOT NULL
            DEFAULT '1970-01-01T00:00:00.0000000+00:00';

        UPDATE auth_refresh_tokens
        SET authenticated_utc=created_utc;
        """;
}
