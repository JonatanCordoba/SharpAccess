using SharpAccess.Persistence;
using Microsoft.Data.Sqlite;

namespace SharpAccess.Sqlite;

internal sealed class SqliteAuthDatabaseErrorClassifier : IAuthDatabaseErrorClassifier
{
    // Maps a SQLite exception to a provider-neutral category.
    public AuthDatabaseErrorCategory Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is TimeoutException)
        {
            return AuthDatabaseErrorCategory.Timeout;
        }

        return exception is SqliteException sqlite
            ? ClassifyCodes(sqlite.SqliteErrorCode, sqlite.SqliteExtendedErrorCode)
            : AuthDatabaseErrorCategory.Unknown;
    }

    // Maps SQLite result and extended-result codes without exposing them to Core.
    internal static AuthDatabaseErrorCategory ClassifyCodes(int errorCode, int extendedErrorCode) =>
        extendedErrorCode switch
        {
            1555 or 2067 => AuthDatabaseErrorCategory.UniqueConstraint,
            787 => AuthDatabaseErrorCategory.ForeignKeyConstraint,
            _ => errorCode switch
            {
                5 or 6 => AuthDatabaseErrorCategory.Timeout,
                10 or 14 => AuthDatabaseErrorCategory.ConnectionFailure,
                8 or 23 => AuthDatabaseErrorCategory.PermissionDenied,
                1 => AuthDatabaseErrorCategory.SchemaMismatch,
                _ => AuthDatabaseErrorCategory.Unknown
            }
        };
}
