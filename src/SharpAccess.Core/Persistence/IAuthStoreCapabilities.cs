namespace SharpAccess.Persistence;

// Supplies only the authorization-context and refresh-token capabilities used while issuing sessions.
internal interface IAuthSessionStore : IAuthAuthorizationContextStore, IAuthRefreshTokenStore
{
}

// Supplies user and tenant membership capabilities used during login and current-user validation.
internal interface IAuthUserTenantStore : IAuthUserStore, IAuthTenantStore
{
}

// Supplies user and one-time-token capabilities used by verification and password-reset workflows.
internal interface IAuthUserOneTimeTokenStore : IAuthUserStore, IAuthOneTimeTokenStore
{
}

// Supplies the user, tenant, and refresh-token capabilities used during session rotation and revocation.
internal interface IAuthRefreshSessionStore : IAuthUserStore, IAuthTenantStore, IAuthRefreshTokenStore
{
}

// Supplies the OAuth, user, tenant, and one-time-token capabilities used by OAuth orchestration.
internal interface IAuthOAuthPersistenceStore : IAuthOAuthStore, IAuthUserStore, IAuthTenantStore, IAuthOneTimeTokenStore
{
}

// Supplies only administration-facing user, global-authorization, and audit capabilities.
internal interface IAuthAdministrationStore : IAuthUserStore, IAuthGlobalAuthorizationStore, IAuthAuditStore
{
}

// Supplies tenant lifecycle and tenant-authorization capabilities used by tenant administration.
internal interface IAuthTenantManagementStore : IAuthTenantStore, IAuthTenantAuthorizationStore
{
}
