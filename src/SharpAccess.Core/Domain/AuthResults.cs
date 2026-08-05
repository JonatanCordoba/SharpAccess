namespace SharpAccess.Domain;

internal enum AuthError
{
    None,
    InvalidInput,
    Conflict,
    Unauthorized,
    Forbidden,
    NotFound,
    Disabled,
    ExternalProviderFailure
}

internal sealed record ServiceResult<T>(T? Value, AuthError Error, string? Code = null)
{
    public bool Succeeded => Error == AuthError.None;

    // Creates a successful service result.
    public static ServiceResult<T> Success(T value) => new(value, AuthError.None);

    // Creates a failed service result without exposing sensitive detail.
    public static ServiceResult<T> Failure(AuthError error, string code) => new(default, error, code);
}

internal sealed record SessionTokens(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresUtc);

internal sealed record GlobalAuthorizationContext(
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

internal sealed record TenantAuthorizationContext(
    Guid TenantId,
    bool IsOwner,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

internal sealed record EffectiveAuthorizationContext(
    GlobalAuthorizationContext Global,
    TenantAuthorizationContext? Tenant,
    long AuthorizationVersion);

internal sealed record UserContext(
    Guid Id,
    string Email,
    bool EmailVerified,
    EffectiveAuthorizationContext Authorization,
    int SecurityVersion)
{
    // Preserves global-only construction for existing internal tests without creating tenant authorization.
    internal UserContext(
        Guid id,
        string email,
        bool emailVerified,
        IReadOnlyList<string> globalRoles,
        IReadOnlyList<string> globalPermissions,
        Guid? tenantId,
        int securityVersion)
        : this(
            id,
            email,
            emailVerified,
            new EffectiveAuthorizationContext(
                new GlobalAuthorizationContext(globalRoles, globalPermissions),
                tenantId.HasValue
                    ? new TenantAuthorizationContext(tenantId.Value, false, [], [])
                    : null,
                securityVersion),
            securityVersion)
    {
    }

    internal IReadOnlyList<string> GlobalRoles => Authorization.Global.Roles;
    internal IReadOnlyList<string> GlobalPermissions => Authorization.Global.Permissions;
    internal Guid? TenantId => Authorization.Tenant?.TenantId;
    internal bool IsTenantOwner => Authorization.Tenant?.IsOwner ?? false;
    internal IReadOnlyList<string> TenantRoles => Authorization.Tenant?.Roles ?? [];
    internal IReadOnlyList<string> TenantPermissions => Authorization.Tenant?.Permissions ?? [];
    internal long AuthorizationVersion => Authorization.AuthorizationVersion;

    // Keeps existing internal global-only callers explicit at the underlying authorization model.
    internal IReadOnlyList<string> Roles => GlobalRoles;

    // Keeps existing internal global-only callers explicit at the underlying authorization model.
    internal IReadOnlyList<string> Permissions => GlobalPermissions;
}
