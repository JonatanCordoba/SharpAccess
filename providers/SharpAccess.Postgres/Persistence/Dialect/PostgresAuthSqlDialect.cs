using System.Data;
using System.Data.Common;
using SharpAccess.Persistence;
using SharpAccess.Postgres.Migrations;

namespace SharpAccess.Postgres;

internal sealed class PostgresAuthSqlDialect : IAuthSqlDialect
{
    // Normalizes a logical parameter name to PostgreSQL's at-prefixed syntax.
    public string Parameter(string logicalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalName);
        string trimmed = logicalName.TrimStart('$', '@', ':');
        return "@" + trimmed;
    }
}
