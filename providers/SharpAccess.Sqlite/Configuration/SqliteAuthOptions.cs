namespace SharpAccess.Sqlite;

/// <summary>Configures the SQLite persistence provider for SharpAccess.</summary>
public sealed class SqliteAuthOptions
{
    /// <summary>Gets or sets the SQLite connection string.</summary>
    public string ConnectionString { get; set; } = string.Empty;
}
