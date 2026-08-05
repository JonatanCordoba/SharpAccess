using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Persistence;
using Microsoft.Extensions.Options;

namespace SharpAccess.Services;

internal sealed class CurrentUserUseCase(
    IAuthUserTenantStore store,
    IAuthSessionIssuer sessions,
    IOptions<AuthOptions> options) : ICurrentUserUseCase
{
    private readonly AuthOptions _options = options.Value;

    // Loads the authenticated profile and current tenant authorization context.
    public async Task<ServiceResult<UserContext>> GetMeAsync(
        Guid userId,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return ServiceResult<UserContext>.Failure(AuthError.Unauthorized, "invalid_user");
        }

        AuthUser? user = await store.FindUserByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is null || !user.IsActive || !user.EmailVerifiedUtc.HasValue)
        {
            return ServiceResult<UserContext>.Failure(AuthError.Unauthorized, "invalid_user");
        }

        if (tenantId.HasValue
            && (!_options.Features.Tenancy
                || !await store.IsTenantMemberAsync(userId, tenantId.Value, cancellationToken).ConfigureAwait(false)))
        {
            return ServiceResult<UserContext>.Failure(AuthError.Forbidden, "tenant_access_denied");
        }

        return ServiceResult<UserContext>.Success(
            await sessions.BuildContextAsync(user, tenantId, cancellationToken).ConfigureAwait(false));
    }
}
