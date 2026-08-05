# Migrating pre-1.0 applications to SharpAccess 1.0

SharpAccess 1.0 intentionally removes pre-v1 compatibility aliases and legacy runtime fallbacks. Apply these changes before upgrading a consuming host.

## Registration and application APIs

| Removed API | SharpAccess 1.0 API |
|---|---|
| `AddDotNetAuth` | `AddSharpAccess` |
| `UseDotNetAuth` | `UseSharpAccess` |
| `MapDotNetAuthEndpoints` | `MapSharpAccessEndpoints` |
| `InitializeDotNetAuthAsync` | `InitializeSharpAccessAsync` |
| `SeedDotNetAuthAdminAsync` | `SeedSharpAccessAdminAsync` |
| `AddSqliteAuth` | `AddSqliteAccess` |

Configuration roots must use `SharpAccess`. SQLite configuration must use `SharpAccess:Sqlite`.

## Authorization attributes

| Removed attribute | SharpAccess 1.0 attribute |
|---|---|
| `RequireRole` | `RequireGlobalRole` |
| `RequirePermission` | `RequireGlobalPermission` |
| `RequireAnyPermission` | `RequireAnyGlobalPermission` |
| `RequireAllPermissions` | `RequireAllGlobalPermissions` |
| `RequireTenant` | `RequireActiveTenant` |

Choose a tenant-specific attribute whenever authority belongs to the active tenant.

## Runtime identifiers

The package accepts only:

- bearer scheme `SharpAccess.Jwt`;
- refresh cookie `sharpaccess_refresh`, unless the host configures another name;
- CSRF confirmation header `X-SharpAccess-CSRF`, unless the host configures another name.

The pre-v1 `DotNetAuth.Jwt`, `dotnet_auth_refresh`, and `X-DotNetAuth-CSRF` fallbacks are removed.

## Middleware behavior

`UseSharpAccess()` no longer selects host-wide exception handling or a fixed content security policy. Select those components explicitly or configure them through `SharpAccessMiddlewareOptions`.

```csharp
app.UseSharpAccess(options =>
{
    options.InstallExceptionHandler = true;
    options.InstallSecurityHeaders = true;
});
```

A fixed CSP is never chosen by default. Configure it explicitly through `UseSharpAccessSecurityHeaders`.
