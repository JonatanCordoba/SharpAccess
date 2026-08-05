using System.Data;
using System.Data.Common;
using SharpAccess.Persistence;
using SharpAccess.Sqlite.Migrations;

namespace SharpAccess.Sqlite;

internal sealed class SqliteAuthSqlDialect : IAuthSqlDialect
{
    // Normalizes a logical parameter name to SQLite's dollar-prefixed syntax.
    public string Parameter(string logicalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalName);
        string trimmed = logicalName.TrimStart('$', '@', ':');
        return "$" + trimmed;
    }
}
