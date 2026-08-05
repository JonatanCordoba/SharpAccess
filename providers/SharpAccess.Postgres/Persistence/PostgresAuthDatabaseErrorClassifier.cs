using SharpAccess.Persistence;
using Npgsql;

namespace SharpAccess.Postgres;

internal sealed class PostgresAuthDatabaseErrorClassifier : IAuthDatabaseErrorClassifier
{
    private static readonly Dictionary<string, AuthDatabaseErrorCategory> SqlStateCategories =
        new(StringComparer.Ordinal)
        {
            ["23505"] = AuthDatabaseErrorCategory.UniqueConstraint,
            ["23503"] = AuthDatabaseErrorCategory.ForeignKeyConstraint,
            ["40001"] = AuthDatabaseErrorCategory.SerializationFailure,
            ["40P01"] = AuthDatabaseErrorCategory.Deadlock,
            ["55P03"] = AuthDatabaseErrorCategory.Timeout,
            ["57014"] = AuthDatabaseErrorCategory.Timeout,
            ["53300"] = AuthDatabaseErrorCategory.ConnectionFailure,
            ["57P01"] = AuthDatabaseErrorCategory.ConnectionFailure,
            ["57P02"] = AuthDatabaseErrorCategory.ConnectionFailure,
            ["57P03"] = AuthDatabaseErrorCategory.ConnectionFailure,
            ["42501"] = AuthDatabaseErrorCategory.PermissionDenied,
            ["42P01"] = AuthDatabaseErrorCategory.SchemaMismatch,
            ["42P07"] = AuthDatabaseErrorCategory.SchemaMismatch,
            ["42703"] = AuthDatabaseErrorCategory.SchemaMismatch,
            ["42710"] = AuthDatabaseErrorCategory.SchemaMismatch,
            ["3F000"] = AuthDatabaseErrorCategory.SchemaMismatch
        };

    // Maps a PostgreSQL exception to a provider-neutral category.
    public AuthDatabaseErrorCategory Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is TimeoutException)
        {
            return AuthDatabaseErrorCategory.Timeout;
        }

        if (exception is PostgresException postgres)
        {
            return ClassifySqlState(postgres.SqlState);
        }

        if (exception is NpgsqlException npgsql)
        {
            return npgsql.InnerException is TimeoutException
                ? AuthDatabaseErrorCategory.Timeout
                : AuthDatabaseErrorCategory.ConnectionFailure;
        }

        return AuthDatabaseErrorCategory.Unknown;
    }

    // Maps PostgreSQL SQLSTATE values without exposing provider exceptions to Core.
    internal static AuthDatabaseErrorCategory ClassifySqlState(string sqlState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlState);
        if (sqlState.StartsWith("08", StringComparison.Ordinal))
        {
            return AuthDatabaseErrorCategory.ConnectionFailure;
        }

        return SqlStateCategories.TryGetValue(sqlState, out AuthDatabaseErrorCategory category)
            ? category
            : AuthDatabaseErrorCategory.Unknown;
    }
}
