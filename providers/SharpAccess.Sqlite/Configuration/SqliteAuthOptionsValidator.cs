using Microsoft.Extensions.Options;

namespace SharpAccess.Sqlite;

internal sealed class SqliteAuthOptionsValidator : IValidateOptions<SqliteAuthOptions>
{
    // Requires an explicit SQLite connection string.
    public ValidateOptionsResult Validate(string? name, SqliteAuthOptions options) =>
        string.IsNullOrWhiteSpace(options.ConnectionString)
            ? ValidateOptionsResult.Fail("SqliteAuthOptions.ConnectionString is required.")
            : ValidateOptionsResult.Success;
}
