# Installation

SharpAccess requires Windows, PowerShell 7 for repository operations, and .NET 10 selected by the repository `global.json`.

## Choose one package set

### SQLite

```powershell
dotnet add package SharpAccess.Core --version 0.9.0-rc.1
dotnet add package SharpAccess.Sqlite --version 0.9.0-rc.1
```

### PostgreSQL

```powershell
dotnet add package SharpAccess.Core --version 0.9.0-rc.1
dotnet add package SharpAccess.Postgres --version 0.9.0-rc.1
```

> [!WARNING]
> Install and register exactly one supported DB provider as primary persistence. Do not register SQLite and PostgreSQL together.

## Register SharpAccess

```csharp
builder.Services.AddSharpAccess(builder.Configuration);

// Choose exactly one:
builder.Services.AddSqliteAccess(builder.Configuration);
// builder.Services.AddPostgresAccess(builder.Configuration);
```

Initialize the schema before serving requests:

```csharp
await app.Services.InitializeSharpAccessAsync(
    app.Lifetime.ApplicationStopping);
```

Then add the SharpAccess middleware and endpoints:

```csharp
app.UseSharpAccessExceptionHandling();
app.UseSharpAccessSecurityHeaders();
app.UseSharpAccess();
app.MapSharpAccessEndpoints();
```

## Package status

`SharpAccess.Core`, `SharpAccess.Sqlite`, and `SharpAccess.Postgres` are the supported package cohort. SQL Server and MySQL are roadmap-only and must not be presented as available provider packages.

## More detail

- [NuGet packaging and consumer setup](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/NUGET-PACKAGE.md)
- [Public API](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/PUBLIC-API.md)
- [Database providers](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/DATABASE-PROVIDERS.md)
- [Quick Start](Quick-Start)
