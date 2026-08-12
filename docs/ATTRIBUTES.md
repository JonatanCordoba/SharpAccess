# Authorization attributes

SharpAccess uses explicit global and active-tenant authorization attributes. The published prerelease is `0.9.0-rc.1`; this is current API documentation, not a claim that stable `1.0.0` has shipped.

## Attributes

- `[Authenticate]`: requires a valid SharpAccess JWT identity.
- `[RequireGlobalRole]`, `[RequireGlobalPermission]`, `[RequireAnyGlobalPermission]`, `[RequireAllGlobalPermissions]`: global authorization only.
- `[RequireTenantPermission]`, `[RequireTenantRole]`: active-tenant authorization only.
- `[RequireTenantOwner]`: requires ownership of the active tenant.
- `[RequireActiveTenant]`: requires active-tenant context and route equality where configured.
- `[RequireGlobalOrTenantPermission]`: deliberate endpoint policy naming both accepted scopes.

Attributes on Minimal API delegates are applied through the package mapping helpers (`MapAttributedGet/Post/Put/Patch/Delete`).

Tenant route policies bind tenant claims to the active/route tenant; tenant permissions cannot satisfy global administration policies.

`UseSharpAccess()` installs the package authentication/authorization middleware composition. Hosts may compose the individual SharpAccess exception/security/cookie/rate-limit/authentication/fresh-auth/authorization components when they need explicit ordering and policy control.
