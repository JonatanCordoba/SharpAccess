namespace SharpAccess;

/// <summary>Contains stable authentication and authorization constants exposed to consuming applications.</summary>
public static class AuthConstants
{
    /// <summary>Gets the JWT bearer authentication scheme registered by SharpAccess.</summary>
    public const string AuthenticationScheme = "SharpAccess.Jwt";

    /// <summary>Gets the default refresh-token cookie name.</summary>
    public const string DefaultRefreshTokenCookieName = "sharpaccess_refresh";

    /// <summary>Gets the default CSRF confirmation header name.</summary>
    public const string DefaultCsrfHeaderName = "X-SharpAccess-CSRF";

    /// <summary>Gets the claim used for global role names.</summary>
    public const string GlobalRoleClaim = "global_role";

    /// <summary>Gets the claim used for global permission names.</summary>
    public const string GlobalPermissionClaim = "global_permission";

    /// <summary>Gets the claim used for active-tenant role names.</summary>
    public const string TenantRoleClaim = "tenant_role";

    /// <summary>Gets the claim used for active-tenant permission names.</summary>
    public const string TenantPermissionClaim = "tenant_permission";

    /// <summary>Gets the claim that identifies the active tenant owner.</summary>
    public const string TenantOwnerClaim = "tenant_owner";

    /// <summary>Gets the claim used for the active tenant identifier.</summary>
    public const string TenantClaim = "tid";

    /// <summary>Gets the claim used for the user&apos;s security version.</summary>
    public const string SecurityVersionClaim = "ver";

    /// <summary>Gets the claim used for the persisted authorization version.</summary>
    public const string AuthorizationVersionClaim = "auth_ver";

    /// <summary>Gets the immutable time of the primary credential authentication that created the session.</summary>
    public const string AuthenticationTimeClaim = "auth_time";
}

/// <summary>Contains the built-in global role names seeded by supported providers.</summary>
public static class AuthRoles
{
    /// <summary>Gets the global administrator role name.</summary>
    public const string Admin = "Admin";

    /// <summary>Gets the standard global user role name.</summary>
    public const string User = "User";

    /// <summary>Gets the delegated global manager role name.</summary>
    public const string Manager = "Manager";
}

/// <summary>Contains the built-in tenant role names seeded independently for each tenant.</summary>
public static class TenantAuthRoles
{
    /// <summary>Gets the immutable owner role projection.</summary>
    public const string Owner = "Owner";

    /// <summary>Gets the tenant manager role name.</summary>
    public const string Manager = "Manager";

    /// <summary>Gets the standard tenant member role name.</summary>
    public const string Member = "Member";
}

/// <summary>Contains the built-in global permission names seeded by supported providers.</summary>
public static class AuthPermissions
{
    /// <summary>Gets the global permission to read users.</summary>
    public const string UsersRead = "users.read";

    /// <summary>Gets the global permission to manage users.</summary>
    public const string UsersManage = "users.manage";

    /// <summary>Gets the global permission to read roles.</summary>
    public const string RolesRead = "roles.read";

    /// <summary>Gets the global permission to manage roles.</summary>
    public const string RolesManage = "roles.manage";

    /// <summary>Gets the global permission to read permissions.</summary>
    public const string PermissionsRead = "permissions.read";

    /// <summary>Gets the global permission to manage permissions.</summary>
    public const string PermissionsManage = "permissions.manage";

    /// <summary>Gets the global permission to manage sessions.</summary>
    public const string SessionsManage = "sessions.manage";

    /// <summary>Gets the global permission to read audit events.</summary>
    public const string AuditRead = "audit.read";

    /// <summary>Gets the global permission to read tenants across tenant boundaries.</summary>
    public const string TenantsRead = "tenants.read";

    /// <summary>Gets the global permission to manage tenants across tenant boundaries.</summary>
    public const string TenantsManage = "tenants.manage";

    /// <summary>Gets the global permission to read the current profile.</summary>
    public const string ProfileRead = "profile.read";

    /// <summary>Gets the global permission to update the current profile.</summary>
    public const string ProfileUpdate = "profile.update";

    /// <summary>Gets every built-in global permission name.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        UsersRead,
        UsersManage,
        RolesRead,
        RolesManage,
        PermissionsRead,
        PermissionsManage,
        SessionsManage,
        AuditRead,
        TenantsRead,
        TenantsManage,
        ProfileRead,
        ProfileUpdate
    ];
}

/// <summary>Contains the built-in active-tenant permission names seeded for each tenant.</summary>
public static class TenantAuthPermissions
{
    /// <summary>Gets the permission to read one active tenant.</summary>
    public const string TenantRead = "tenant.read";

    /// <summary>Gets the permission to read active-tenant members.</summary>
    public const string MembersRead = "tenant.members.read";

    /// <summary>Gets the permission to add and manage active-tenant members.</summary>
    public const string MembersManage = "tenant.members.manage";

    /// <summary>Gets the permission to manage active-tenant role assignments.</summary>
    public const string RolesManage = "tenant.roles.manage";

    /// <summary>Gets the owner-only permission to transfer active-tenant ownership.</summary>
    public const string OwnershipTransfer = "tenant.owner.transfer";

    /// <summary>Gets every built-in tenant permission name.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        TenantRead,
        MembersRead,
        MembersManage,
        RolesManage,
        OwnershipTransfer
    ];
}
