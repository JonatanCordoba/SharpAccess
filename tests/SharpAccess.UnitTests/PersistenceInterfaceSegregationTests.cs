using System.Reflection;
using SharpAccess.OAuth;
using SharpAccess.Persistence;
using SharpAccess.Services;

namespace SharpAccess.UnitTests;

public sealed class PersistenceInterfaceSegregationTests
{
    // Verifies that application services no longer request the aggregate provider store.
    [Fact]
    public void ApplicationServiceConstructorsDoNotDependOnAggregateAuthStore()
    {
        Type[] serviceTypes =
        [
            typeof(AuditService),
            typeof(AuthSessionIssuer),
            typeof(RegistrationUseCase),
            typeof(PasswordLoginUseCase),
            typeof(RefreshSessionUseCase),
            typeof(CurrentUserUseCase),
            typeof(PasswordChangeUseCase),
            typeof(PasswordResetUseCase),
            typeof(EmailVerificationUseCase),
            typeof(AdministrationService),
            typeof(TenantService),
            typeof(OAuthService)
        ];

        foreach (Type serviceType in serviceTypes)
        {
            ConstructorInfo constructor = Assert.Single(serviceType.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
            Assert.DoesNotContain(
                constructor.GetParameters(),
                parameter => parameter.ParameterType == typeof(IAuthStore));
        }
    }

    // Verifies the principal services request responsibility-specific persistence interfaces.
    [Fact]
    public void PrincipalServicesUseResponsibilitySpecificPersistenceInterfaces()
    {
        AssertConstructorContains<AuthSessionIssuer, IAuthSessionStore>();
        AssertConstructorContains<PasswordLoginUseCase, IAuthUserTenantStore>();
        AssertConstructorContains<RefreshSessionUseCase, IAuthRefreshSessionStore>();
        AssertConstructorContains<AdministrationService, IAuthAdministrationStore>();
        AssertConstructorContains<TenantService, IAuthTenantManagementStore>();
        AssertConstructorContains<OAuthService, IAuthOAuthPersistenceStore>();
    }

    // Verifies one service constructor contains the expected persistence contract.
    private static void AssertConstructorContains<TService, TContract>()
    {
        ConstructorInfo constructor = Assert.Single(typeof(TService).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        Assert.Contains(
            constructor.GetParameters(),
            parameter => parameter.ParameterType == typeof(TContract));
    }
}
