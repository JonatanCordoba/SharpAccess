namespace SharpAccess.Persistence;

// Marks the complete provider store while capability interfaces own individual operations.
internal interface IAuthStore :
    IAuthUserStore,
    IAuthOneTimeTokenStore,
    IAuthRefreshTokenStore,
    IAuthOAuthStore,
    IAuthAuthorizationStore,
    IAuthTenantStore,
    IAuthAuditStore,
    IAuthAdminSeedStore,
    IAuthSessionStore,
    IAuthUserTenantStore,
    IAuthUserOneTimeTokenStore,
    IAuthRefreshSessionStore,
    IAuthOAuthPersistenceStore,
    IAuthAdministrationStore,
    IAuthTenantManagementStore,
    IAuthSchemaManager
{
}
