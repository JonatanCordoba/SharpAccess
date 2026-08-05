# Authorization attributes

SharpAccess 1.0 uses explicit global and active-tenant authorization attributes. Legacy unscoped aliases are removed.

## Attributes

- `[Authenticate]`: requires a valid identity from `SharpAccess.Jwt`.
- `[RequireGlobalRole("name")]`: requires at least one named global role.
- `[RequireGlobalPermission("permission")]`: requires one global permission.
- `[RequireAnyGlobalPermission(...)]`: requires at least one listed global permission.
- `[RequireAllGlobalPermissions(...)]`: requires every listed global permission.
- `[RequireTenantPermission("permission")]`: requires one active-tenant permission.
- `[RequireTenantRole("name")]`: requires one active-tenant role.
- `[RequireTenantOwner]`: requires ownership of the active tenant.
- `[RequireActiveTenant]`: requires an active tenant claim and route equality when the route contains the configured tenant parameter.
- `[RequireGlobalOrTenantPermission(global, tenant)]`: accepts a named global permission or the named active-tenant permission. The tenant branch is always bound to the route tenant.

## Minimal APIs

Attributes on delegates are applied during endpoint construction through the package mapping helpers.

```csharp
[RequireAllGlobalPermissions(
    AuthPermissions.UsersRead,
    AuthPermissions.RolesRead)]
static IResult Overview() => Results.Ok();

app.MapAttributedGet("/overview", Overview);
```

Available helpers are `MapAttributedGet`, `MapAttributedPost`, `MapAttributedPut`, `MapAttributedPatch`, and `MapAttributedDelete`.

## Tenant routes

```csharp
[Authenticate]
[RequireActiveTenant]
[RequireTenantPermission(TenantAuthPermissions.MembersRead)]
static IResult TenantResource(Guid tenantId) => Results.Ok(tenantId);

app.MapAttributedGet("/tenants/{tenantId:guid}/resource", TenantResource);
```

The route parameter name defaults to `tenantId` and can be changed in the `RequireActiveTenantAttribute` constructor.

## Middleware composition

`UseSharpAccess()` installs package-specific authentication middleware without selecting a host-wide exception policy or CSP by default.

Enterprise hosts may compose components explicitly:

```csharp
app.UseSharpAccessExceptionHandling();
app.UseSharpAccessSecurityHeaders(options =>
{
    options.ContentSecurityPolicy = "default-src 'self'; frame-ancestors 'none'";
});
app.UseSharpAccessCookieProtection();
app.UseSharpAccessRateLimiter();
app.UseSharpAccessAuthentication();
app.UseSharpAccessFreshAuthentication();
app.UseSharpAccessAuthorization();
```
