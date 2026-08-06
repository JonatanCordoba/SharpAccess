# Authorization

SharpAccess separates global authorization from tenant authorization. The scopes are not flattened or implicitly interchangeable.

## Contexts

`GlobalAuthorizationContext` contains global roles and global permissions only.

`TenantAuthorizationContext` contains:

- the selected active tenant identifier;
- the persisted owner projection;
- tenant roles;
- tenant permissions for that tenant only.

## Policy rules

- Every `/admin/*` endpoint uses a global permission policy.
- Tenant permissions cannot authorize global administration.
- A global permission does not automatically authorize a tenant operation.
- Cross-tenant authority is accepted only by an endpoint with an explicit global-or-tenant policy.
- Tenant claims must match the route-bound active tenant.

## Roles and permissions

Global and tenant catalogs are separate. Tenant-owned keys and joins include `tenant_id` where required. Providers must not join tenant roles or permissions by identifier without also matching the tenant.

## Tenant ownership

Ownership transfer is a provider transaction that verifies the caller, proposed owner, active membership, and same-tenant scope; updates ownership and roles; invalidates affected authorization and refresh sessions; and records canonical audit evidence.

## Attributes and Minimal APIs

SharpAccess exposes explicit global, active-tenant, and owner authorization attributes plus Minimal API policy helpers. Unscoped pre-v1 authorization aliases are not part of the stable surface.

## Invalidation

Role, permission, membership, ownership, and relevant account-state changes increment persisted versions and invalidate stale sessions so old claims cannot silently retain authority.

## References

- [Authorization model](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/AUTHORIZATION.md)
- [Authorization attributes](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/ATTRIBUTES.md)
- [Threat model](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/THREAT_MODEL.md)
- [Database provider authorization contract](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/DATABASE-PROVIDERS.md)
