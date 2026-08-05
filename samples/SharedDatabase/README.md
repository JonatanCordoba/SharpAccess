# Shared database sample

This sample uses the host application database for both application data and SharpAccess data. SharpAccess still owns only its migration ledger and `auth_*` objects.

```csharp
using Microsoft.Data.Sqlite;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

string applicationDatabase = builder.Configuration.GetConnectionString("Application")
    ?? throw new InvalidOperationException("The application database is not configured.");

builder.Services.AddSharpAccess(builder.Configuration);
builder.Services.AddSqliteAccess(
    cancellationToken =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new SqliteConnection(applicationDatabase));
    });

WebApplication app = builder.Build();
await app.Services.InitializeSharpAccessAsync(app.Lifetime.ApplicationStopping);
app.UseSharpAccessExceptionHandling();
app.UseSharpAccessSecurityHeaders();
app.UseSharpAccess();
app.MapSharpAccessEndpoints();
await app.RunAsync();
```

The delegate creates one logical connection per SharpAccess operation. The host may use the same connection string in its own data-access layer, but it must not pass an active application transaction or a singleton open connection to SharpAccess.
