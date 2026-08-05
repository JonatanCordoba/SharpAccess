# Dedicated authentication database sample

This sample gives SharpAccess a separate SQLite database and leaves the application database under independent host ownership.

```csharp
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddSharpAccess(builder.Configuration);
builder.Services.AddSqliteAccess(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("Authentication")
        ?? throw new InvalidOperationException("The authentication database is not configured.");
});

WebApplication app = builder.Build();
await app.Services.InitializeSharpAccessAsync(app.Lifetime.ApplicationStopping);
app.UseSharpAccessExceptionHandling();
app.UseSharpAccessSecurityHeaders();
app.UseSharpAccess();
app.MapSharpAccessEndpoints();
await app.RunAsync();
```

Example configuration:

```json
{
  "ConnectionStrings": {
    "Application": "Data Source=app.db",
    "Authentication": "Data Source=sharpaccess.db"
  }
}
```

SharpAccess creates and migrates only the authentication database selected by `AddSqliteAccess`. Backup, restore, file permissions, Data Protection keys, and deployment sequencing remain host responsibilities.
