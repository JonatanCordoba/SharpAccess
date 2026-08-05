# Authorization model

SharpAccess treats global and tenant authorization as separate security domains. They use different persistence catalogs, provider contracts, JWT claim types, endpoint policies, and mutation paths.

## Contexts

`GlobalAuthorizationContext` contains only global roles and global permissions.

`TenantAuthorizationContext` contains the selected active tenant identifier, the persisted owner projection, tenant roles, and tenant permissions for that tenant only.

`EffectiveAuthorizationContext` contains one global context, zero or one active-tenant context, and the persisted authorization version. It never exposes a flattened permission collection.

## Catalogs

Global authorization uses:

- `auth_global_roles`;
- `auth_global_permissions`;
- `auth_global_role_permissions`;
- `auth_global_user_roles`.

Tenant authorization uses:

- `auth_tenant_roles`;
- `auth_tenant_permissions`;
- `auth_tenant_role_permissions`;
- `auth_tenant_user_roles`;
- `auth_tenant_owners`.

Tenant catalog keys always include `tenant_id`. Global role or permission identifiers are not valid tenant assignments, and tenant identifiers are not valid global assignments.

## Claims

| Claim | Meaning |
|---|---|
| `global_role` | One global role. |
| `global_permission` | One global permission. |
| `tid` | The active tenant identifier. |
| `tenant_role` | One role in the active tenant. |
| `tenant_permission` | One permission in the active tenant. |
| `tenant_owner` | The tenant identifier owned by the caller; it must equal `tid`. |
| `ver` | Persisted user security version. |
| `auth_ver` | Persisted authorization version used to reject stale contexts. |

SharpAccess does not emit the legacy `role` or `permission` claims. A tenant role or permission is never copied into a global claim.

## Policies

- `RequireGlobalPermission` accepts only a named `global_permission` claim.
- `RequireTenantPermission` accepts only a named `tenant_permission` claim and requires `tid` to equal the route tenant.
- `RequireTenantOwner` requires a valid `tenant_owner` claim for the same active tenant.
- `RequireActiveTenant` requires the active tenant to match the route tenant.
- `RequireGlobalOrTenantPermission` is reserved for deliberate endpoints that name both the accepted global permission and the accepted tenant permission.

Every `/admin/*` endpoint uses a global permission policy. Tenant permissions cannot authorize administration endpoints.

A global permission does not automatically authorize a tenant operation. Cross-tenant authority is accepted only at an endpoint with an explicit global-or-tenant policy, such as reading a tenant through `tenants.read` or `tenant.read`.

The standard global `User` role contains only self-service profile permissions. It does not receive `tenants.read`. Cross-tenant read authority is reserved for explicitly privileged global roles or custom assignments.

## Tenant ownership

Each tenant has exactly one row in `auth_tenant_owners`. The same user also holds the tenant-scoped, system-owned `Owner` role.

The `Owner` role:

- cannot be created, renamed, assigned, or removed through ordinary role APIs;
- contains every required built-in tenant permission, including `tenant.owner.transfer`;
- changes only through the ownership-transfer operation.

Ownership transfer:

1. verifies the current persisted owner under a provider-specific lock;
2. verifies the proposed owner is an active member of the same tenant;
3. writes a transfer-started audit event;
4. moves the owner record and immutable `Owner` role in one transaction;
5. gives the previous owner the standard `Member` role when needed;
6. invalidates both users' authorization and refresh sessions;
7. writes a transfer-completed audit event.

The operation rejects transfer to the same user, a non-member, a different tenant's member, or a caller who is no longer the persisted owner.

## Provider requirements

Every provider must implement `GetEffectiveAuthorizationContextAsync` without combining scopes. Provider-contract tests verify:

- global assignments appear only in the global context;
- tenant assignments appear only in the selected tenant context;
- a global role cannot be assigned through the tenant path;
- the immutable `Owner` role cannot be assigned or removed through ordinary tenant-role methods;
- tenant assignments do not cross tenant boundaries;
- owners have the immutable `Owner` role;
- ownership transfer moves the owner record and role atomically;
- affected authorization versions and refresh sessions are invalidated.

## Upgrade behavior

Migration `005_split_global_tenant_authorization` separates the historical shared catalog into global and tenant catalogs, derives initial tenant ownership from the earliest valid `tenant_created` audit record or deterministic earliest membership, increments every user security version, and revokes active refresh tokens.

Migration `006_add_immutable_tenant_owner_role` creates the system `Owner` role for existing tenants, grants its required tenant permissions, and assigns it to the persisted owner.

Migration `007_remove_standard_user_cross_tenant_read` removes the historical `tenants.read` grant from the standard global `User` role. Existing explicitly privileged global roles retain their approved cross-tenant permissions.

Applications must expect users to authenticate again after this migration set. Upgrade validation must confirm one owner row and one `Owner` role assignment per tenant before accepting traffic.
