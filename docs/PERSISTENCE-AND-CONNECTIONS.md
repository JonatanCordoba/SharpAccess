# Persistence and connection ownership

SharpAccess supports shared-database and dedicated-authentication-database deployment without coupling Core to a host ORM or application transaction.

## Ownership

SharpAccess owns only:

- `auth_schema_migrations` and checksum ledgers;
- tables, indexes, and constraints whose names begin with `auth_`;
- commands and transactions executed for SharpAccess operations.

The host owns all other database objects, database provisioning, credentials, pools/data sources, backup policy, and infrastructure.

## Connection lifecycle

Active providers support provider-owned connection-string registration and host-managed logical connection creation.

A host-managed delegate returns a distinct logical connection per invocation. SharpAccess disposes that logical connection after the operation but does not own or dispose a captured host pool/data source. Do not return a permanently open singleton connection or share an application transaction.

## SQLite

Supported connection-string registration:

```csharp
builder.Services.AddSqliteAccess(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("Application")
        ?? throw new InvalidOperationException("The application database is not configured.");
});
```

Supported host-managed logical connection creation:

```csharp
string connectionString = builder.Configuration.GetConnectionString("Application")
    ?? throw new InvalidOperationException("The application database is not configured.");

builder.Services.AddSqliteAccess(cancellationToken =>
{
    cancellationToken.ThrowIfCancellationRequested();
    return ValueTask.FromResult(new Microsoft.Data.Sqlite.SqliteConnection(connectionString));
});
```

## PostgreSQL

The supported PostgreSQL provider accepts a configured connection string, a host-owned `NpgsqlDataSource`, or a logical-connection delegate.

Provider-owned connection string:

```csharp
builder.Services.AddPostgresAccess(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("Application")
        ?? throw new InvalidOperationException("The application database is not configured.");
});
```

Configuration binding:

```csharp
builder.Services.AddPostgresAccess(builder.Configuration);
```

The configuration binder first reads `SharpAccess:Postgres`, then `PostgresAccess`, and otherwise treats the supplied configuration object as the provider section.

Host-owned data source:

```csharp
string connectionString = builder.Configuration.GetConnectionString("Application")
    ?? throw new InvalidOperationException("The application database is not configured.");

Npgsql.NpgsqlDataSource dataSource = Npgsql.NpgsqlDataSource.Create(connectionString);
builder.Services.AddSingleton(dataSource);
builder.Services.AddPostgresAccess(dataSource);
```

Registering the data source separately makes the host service provider responsible for its lifetime. `AddPostgresAccess(dataSource)` does not transfer ownership.

Host-managed logical connection creation:

```csharp
string connectionString = builder.Configuration.GetConnectionString("Application")
    ?? throw new InvalidOperationException("The application database is not configured.");

builder.Services.AddPostgresAccess(async cancellationToken =>
{
    Npgsql.NpgsqlConnection connection = new(connectionString);
    await connection.OpenAsync(cancellationToken);
    return connection;
});
```

PostgreSQL operation and evidence run on Windows using a native installation or approved managed database. No container lifecycle is part of connection ownership.

SQL Server and MySQL have no active connection registration, dependencies, or projects. Future reintroduction requires a new ADR.

## Provider-neutral failure categories

Providers map native failures to internal categories:

- `UniqueConstraint`;
- `ForeignKeyConstraint`;
- `SerializationFailure`;
- `Deadlock`;
- `Timeout`;
- `ConnectionFailure`;
- `PermissionDenied`;
- `SchemaMismatch`;
- `Unknown`.

Core does not catch or depend on concrete provider exception types.

## Transaction boundaries

SharpAccess owns each security-sensitive command and transaction.

| Boundary | Required atomic behavior |
|---|---|
| Account creation | Account and initial verification token together. |
| One-time-token replacement | Invalidate previous token and create replacement together. |
| Password change | Update password/security version and revoke refresh sessions together. |
| Refresh rotation/reuse | Revoke current, insert replacement, and revoke family on replay atomically. |
| Tenant creation | Tenant, membership, owner record, role catalog, and assignment together. |
| Ownership transfer | Lock owner state, validate membership, move owner/role, invalidate contexts together. |
| Role/permission assignment | Assignment and authorization-version invalidation together. |
| User deactivation | Account state change and refresh-session revocation together. |
| Migration | Claim and apply ordered migration in one provider-owned transaction when supported. |

Rollback failures must not replace the original operation exception. Cancellation propagates through connection, command, reader, transaction, commit, rollback, and asynchronous disposal.

## Deliberate non-integration

SharpAccess 1.0 does not expose an Entity Framework `DbContext`, accept an application transaction, or enlist in a host unit of work. Coordinate cross-boundary work through state machines, idempotency, or an outbox.