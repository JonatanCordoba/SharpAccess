namespace SharpAccess.Persistence;

// Defines provider-neutral categories for database failures that application code may reason about.
internal enum AuthDatabaseErrorCategory
{
    UniqueConstraint = 1,
    ForeignKeyConstraint = 2,
    SerializationFailure = 3,
    Deadlock = 4,
    Timeout = 5,
    ConnectionFailure = 6,
    PermissionDenied = 7,
    SchemaMismatch = 8,
    Unknown = 0
}

internal interface IAuthDatabaseErrorClassifier
{
    // Maps a provider exception to a bounded provider-neutral category.
    AuthDatabaseErrorCategory Classify(Exception exception);
}
