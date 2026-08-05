using SharpAccess.Persistence;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

// Registers one provider store against the complete provider-neutral persistence capability surface.
internal static class AuthStoreServiceRegistration
{
    // Maps one scoped concrete store to every provider-neutral contract without duplicating registrations per provider.
    internal static IServiceCollection AddSharpAccessAuthStore<TStore>(this IServiceCollection services)
        where TStore : class,
            IAuthDatabase,
            IAuthSchemaManager,
            IAuthUserStore,
            IAuthOneTimeTokenStore,
            IAuthRefreshTokenStore,
            IAuthOAuthStore,
            IAuthAuthorizationStore,
            IAuthAuthorizationContextStore,
            IAuthGlobalAuthorizationStore,
            IAuthTenantAuthorizationStore,
            IAuthSessionStore,
            IAuthUserTenantStore,
            IAuthUserOneTimeTokenStore,
            IAuthRefreshSessionStore,
            IAuthOAuthPersistenceStore,
            IAuthAdministrationStore,
            IAuthTenantManagementStore,
            IAuthTenantStore,
            IAuthAuditStore,
            IAuthAdminSeedStore
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<TStore>();
        services.TryAddScoped<IAuthDatabase>(static provider => provider.GetRequiredService<TStore>());
        services.TryAddScoped<IAuthStore>(static provider => provider.GetRequiredService<IAuthDatabase>());

        AddIdentityContracts<TStore>(services);
        AddAuthorizationContracts<TStore>(services);
        AddSessionAndOAuthContracts<TStore>(services);
        AddAdministrationContracts<TStore>(services);
        return services;
    }

    // Registers identity, schema, token, and refresh-token capabilities.
    private static void AddIdentityContracts<TStore>(IServiceCollection services)
        where TStore : class,
            IAuthSchemaManager,
            IAuthUserStore,
            IAuthOneTimeTokenStore,
            IAuthRefreshTokenStore
    {
        TryAddStoreContract<TStore, IAuthSchemaManager>(services);
        TryAddStoreContract<TStore, IAuthUserStore>(services);
        TryAddStoreContract<TStore, IAuthOneTimeTokenStore>(services);
        TryAddStoreContract<TStore, IAuthRefreshTokenStore>(services);
    }

    // Registers global, tenant, and bounded authorization-context capabilities.
    private static void AddAuthorizationContracts<TStore>(IServiceCollection services)
        where TStore : class,
            IAuthAuthorizationStore,
            IAuthAuthorizationContextStore,
            IAuthGlobalAuthorizationStore,
            IAuthTenantAuthorizationStore
    {
        TryAddStoreContract<TStore, IAuthAuthorizationStore>(services);
        TryAddStoreContract<TStore, IAuthAuthorizationContextStore>(services);
        TryAddStoreContract<TStore, IAuthGlobalAuthorizationStore>(services);
        TryAddStoreContract<TStore, IAuthTenantAuthorizationStore>(services);
    }

    // Registers sessions, tenant membership, user tokens, refresh sessions, and OAuth persistence.
    private static void AddSessionAndOAuthContracts<TStore>(IServiceCollection services)
        where TStore : class,
            IAuthSessionStore,
            IAuthUserTenantStore,
            IAuthUserOneTimeTokenStore,
            IAuthRefreshSessionStore,
            IAuthOAuthStore,
            IAuthOAuthPersistenceStore
    {
        TryAddStoreContract<TStore, IAuthSessionStore>(services);
        TryAddStoreContract<TStore, IAuthUserTenantStore>(services);
        TryAddStoreContract<TStore, IAuthUserOneTimeTokenStore>(services);
        TryAddStoreContract<TStore, IAuthRefreshSessionStore>(services);
        TryAddStoreContract<TStore, IAuthOAuthStore>(services);
        TryAddStoreContract<TStore, IAuthOAuthPersistenceStore>(services);
    }

    // Registers administrative, tenant-management, audit, and seed capabilities.
    private static void AddAdministrationContracts<TStore>(IServiceCollection services)
        where TStore : class,
            IAuthAdministrationStore,
            IAuthTenantManagementStore,
            IAuthTenantStore,
            IAuthAuditStore,
            IAuthAdminSeedStore
    {
        TryAddStoreContract<TStore, IAuthAdministrationStore>(services);
        TryAddStoreContract<TStore, IAuthTenantManagementStore>(services);
        TryAddStoreContract<TStore, IAuthTenantStore>(services);
        TryAddStoreContract<TStore, IAuthAuditStore>(services);
        TryAddStoreContract<TStore, IAuthAdminSeedStore>(services);
    }

    // Maps one concrete provider store to one provider-neutral scoped contract.
    private static void TryAddStoreContract<TStore, TContract>(IServiceCollection services)
        where TStore : TContract
        where TContract : class
    {
        services.TryAddScoped<TContract>(static provider =>
        {
            TStore store = (TStore)provider.GetRequiredService(typeof(TStore));
            return store;
        });
    }
}
