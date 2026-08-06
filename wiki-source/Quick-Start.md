# Quick Start

This is the smallest supported host shape. It assumes Windows, .NET 10, and one selected DB provider.

## 1. Install packages

Use either the SQLite or PostgreSQL package set from [Installation](Installation).

## 2. Configure services and endpoints

```csharp
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddSharpAccess(builder.Configuration, options =>
{
    options.Features.PasswordAuthentication = true;
    options.Features.Registration = true;
    options.Features.PasswordReset = true;
    options.Features.RefreshTokens = true;
});

// Choose exactly one:
builder.Services.AddSqliteAccess(builder.Configuration);
// builder.Services.AddPostgresAccess(builder.Configuration);

WebApplication app = builder.Build();

await app.Services.InitializeSharpAccessAsync(
    app.Lifetime.ApplicationStopping);

app.UseSharpAccessExceptionHandling();
app.UseSharpAccessSecurityHeaders();
app.UseSharpAccess();
app.MapSharpAccessEndpoints();

await app.RunAsync();
```

## 3. Provide minimal configuration

```json
{
  "SharpAccess": {
    "BaseUri": "https://app.example.com",
    "JwtIssuer": "example-auth",
    "JwtAudience": "example-clients",
    "AccessTokenSigning": {
      "ActiveKeyId": "2026-08",
      "HmacSha256Keys": {
        "2026-08": {
          "Key": "<protected-secret>",
          "ActivatedUtc": "2026-08-01T00:00:00Z"
        }
      }
    },
    "TokenHashing": {
      "CurrentKeyVersion": "v1",
      "Keys": {
        "v1": "<protected-secret>"
      }
    },
    "Passwords": {
      "CurrentPepperVersion": "v1",
      "Peppers": {
        "v1": "<protected-secret>"
      }
    },
    "RateLimits": {
      "PartitionKey": "<dedicated-protected-secret>"
    },
    "Features": {
      "PasswordAuthentication": true,
      "Registration": true,
      "PasswordReset": true,
      "RefreshTokens": true,
      "Administration": false,
      "Tenancy": false
    }
  },
  "ConnectionStrings": {
    "Auth": "<host-owned DB connection string>"
  }
}
```

> [!CAUTION]
> Never commit signing keys, token-hashing keys, password peppers, rate-limit partition keys, OIDC credentials, Data Protection certificates, or DB connection strings.

## 4. Run the sample

From PowerShell 7:

```powershell
dotnet restore SharpAccess.sln --locked-mode
dotnet run --project samples/SharpAccess.SampleApi/SharpAccess.SampleApi.csproj
```

The sample stores its first-run local secrets in Windows Credential Manager for the current user and does not write them into tracked configuration.

## Next

- [Configuration](Configuration)
- [Authentication](Authentication)
- [SQLite Provider](SQLite-Provider)
- [PostgreSQL Provider](PostgreSQL-Provider)
- [Sample host documentation](https://github.com/JonatanCordoba/SharpAccess/blob/main/samples/SharpAccess.SampleApi/README.md)
