namespace SharpAccess.Postgres;

/// <summary>Configures the PostgreSQL persistence provider for SharpAccess.</summary>
public sealed class PostgresAuthOptions
{
    /// <summary>Gets or sets the PostgreSQL connection string used by the SharpAccess provider.</summary>
    public string ConnectionString { get; set; } = string.Empty;
}
