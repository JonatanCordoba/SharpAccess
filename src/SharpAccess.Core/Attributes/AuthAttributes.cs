using Microsoft.AspNetCore.Authorization;

namespace SharpAccess.Attributes;

/// <summary>Requires authentication through the SharpAccess JWT bearer scheme.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AuthenticateAttribute : AuthorizeAttribute
{
    /// <summary>Creates an authentication requirement for the package scheme.</summary>
    public AuthenticateAttribute() => AuthenticationSchemes = AuthConstants.AuthenticationScheme;
}

/// <summary>Requires at least one named global role through the SharpAccess JWT bearer scheme.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequireGlobalRoleAttribute : AuthorizeAttribute
{
    /// <summary>Creates a global role requirement.</summary>
    /// <param name="roles">One or more bounded, printable global role names. At least one role must match.</param>
    /// <exception cref="System.ArgumentException">No valid role is supplied.</exception>
    /// <exception cref="System.ArgumentNullException">The roles array is null.</exception>
    public RequireGlobalRoleAttribute(params string[] roles)
    {
        string[] validated = AuthorizationMetadataValidator.ValidateNames(roles, 100, allowComma: false, nameof(roles));
        AuthenticationSchemes = AuthConstants.AuthenticationScheme;
        Roles = string.Join(',', validated);
    }
}

/// <summary>Requires one global permission.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequireGlobalPermissionAttribute : Attribute
{
    /// <summary>Creates a global permission requirement.</summary>
    /// <param name="permission">The required bounded, printable global permission name.</param>
    /// <exception cref="System.ArgumentException">The permission name is invalid.</exception>
    public RequireGlobalPermissionAttribute(string permission) =>
        Permission = AuthorizationMetadataValidator.ValidateName(permission, 150, allowComma: true, nameof(permission));

    /// <summary>Gets the required global permission.</summary>
    public string Permission { get; }
}

/// <summary>Requires any one of the supplied global permissions.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequireAnyGlobalPermissionAttribute : Attribute
{
    /// <summary>Creates an any-global-permission requirement.</summary>
    /// <param name="permissions">One or more global permission names; satisfying any one is sufficient.</param>
    /// <exception cref="System.ArgumentException">No valid permission is supplied.</exception>
    /// <exception cref="System.ArgumentNullException">The permissions array is null.</exception>
    public RequireAnyGlobalPermissionAttribute(params string[] permissions) =>
        Permissions = AuthorizationMetadataValidator.ValidateNames(permissions, 150, allowComma: true, nameof(permissions));

    /// <summary>Gets the accepted global permission names.</summary>
    public IReadOnlyList<string> Permissions { get; }
}

/// <summary>Requires every supplied global permission.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequireAllGlobalPermissionsAttribute : Attribute
{
    /// <summary>Creates an all-global-permissions requirement.</summary>
    /// <param name="permissions">The global permission names that must all be present.</param>
    /// <exception cref="System.ArgumentException">No valid permission is supplied.</exception>
    /// <exception cref="System.ArgumentNullException">The permissions array is null.</exception>
    public RequireAllGlobalPermissionsAttribute(params string[] permissions) =>
        Permissions = AuthorizationMetadataValidator.ValidateNames(permissions, 150, allowComma: true, nameof(permissions));

    /// <summary>Gets the required global permission names.</summary>
    public IReadOnlyList<string> Permissions { get; }
}

/// <summary>Requires one active-tenant permission.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequireTenantPermissionAttribute : Attribute
{
    /// <summary>Creates an active-tenant permission requirement.</summary>
    /// <param name="permission">The required bounded, printable active-tenant permission name.</param>
    /// <exception cref="System.ArgumentException">The permission name is invalid.</exception>
    public RequireTenantPermissionAttribute(string permission) =>
        Permission = AuthorizationMetadataValidator.ValidateName(permission, 150, allowComma: true, nameof(permission));

    /// <summary>Gets the required active-tenant permission.</summary>
    public string Permission { get; }
}

/// <summary>Requires one permission from either the global or active-tenant catalog by deliberate endpoint choice.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequireGlobalOrTenantPermissionAttribute : Attribute
{
    /// <summary>Creates an explicit cross-scope permission requirement.</summary>
    /// <param name="globalPermission">The global permission that independently satisfies the requirement.</param>
    /// <param name="tenantPermission">The tenant permission accepted only for the route-bound active tenant.</param>
    /// <exception cref="System.ArgumentException">Either permission name is invalid.</exception>
    public RequireGlobalOrTenantPermissionAttribute(string globalPermission, string tenantPermission)
    {
        GlobalPermission = AuthorizationMetadataValidator.ValidateName(
            globalPermission,
            150,
            allowComma: true,
            nameof(globalPermission));
        TenantPermission = AuthorizationMetadataValidator.ValidateName(
            tenantPermission,
            150,
            allowComma: true,
            nameof(tenantPermission));
    }

    /// <summary>Gets the accepted global permission.</summary>
    public string GlobalPermission { get; }

    /// <summary>Gets the accepted active-tenant permission.</summary>
    public string TenantPermission { get; }
}

/// <summary>Requires one named active-tenant role.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequireTenantRoleAttribute : Attribute
{
    /// <summary>Creates an active-tenant role requirement.</summary>
    /// <param name="role">The required bounded, printable active-tenant role name.</param>
    /// <exception cref="System.ArgumentException">The role name is invalid.</exception>
    public RequireTenantRoleAttribute(string role) =>
        Role = AuthorizationMetadataValidator.ValidateName(role, 100, allowComma: false, nameof(role));

    /// <summary>Gets the required active-tenant role.</summary>
    public string Role { get; }
}

/// <summary>Requires the caller to own the active tenant.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireTenantOwnerAttribute : Attribute
{
}

/// <summary>Requires an active tenant claim and optionally matches it to a route value.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireActiveTenantAttribute : Attribute
{
    /// <summary>Creates a tenant requirement using the default tenantId route parameter.</summary>
    public RequireActiveTenantAttribute()
    {
    }

    /// <summary>Creates a tenant requirement using a specific route parameter.</summary>
    /// <param name="routeParameterName">The route-value key whose GUID must match the active tenant claim.</param>
    /// <exception cref="System.ArgumentException">The route parameter name is not a bounded identifier.</exception>
    public RequireActiveTenantAttribute(string routeParameterName)
    {
        RouteParameterName = AuthorizationMetadataValidator.IsValidRouteParameter(routeParameterName)
            ? routeParameterName
            : throw new ArgumentException("Route parameter names must be bounded identifier values.", nameof(routeParameterName));
    }

    /// <summary>Gets the optional route parameter name checked against the tenant claim.</summary>
    public string RouteParameterName { get; } = "tenantId";
}

internal static class AuthorizationMetadataValidator
{
    /// <summary>Validates one role or permission name before it becomes endpoint metadata.</summary>
    internal static string ValidateName(string? value, int maximumLength, bool allowComma, string parameterName) =>
        IsValidName(value, maximumLength, allowComma)
            ? value!.Trim()
            : throw new ArgumentException("Authorization names must be nonempty, bounded, printable values.", parameterName);

    /// <summary>Validates a role or permission name before it becomes endpoint metadata.</summary>
    internal static bool IsValidName(string? value, int maximumLength, bool allowComma)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        return trimmed.Length <= maximumLength
            && !trimmed.Any(char.IsControl)
            && (allowComma || !trimmed.Contains(','));
    }

    /// <summary>Validates and copies authorization names so callers cannot mutate endpoint metadata later.</summary>
    internal static string[] ValidateNames(
        string[] values,
        int maximumLength,
        bool allowComma,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0 || values.Any(value => !IsValidName(value, maximumLength, allowComma)))
        {
            throw new ArgumentException("At least one bounded, printable authorization name is required.", parameterName);
        }

        return values.Select(static value => value.Trim()).Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Validates a route-value key without accepting route syntax or control characters.</summary>
    internal static bool IsValidRouteParameter(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 100
        && value.All(static character => char.IsLetterOrDigit(character) || character == '_');
}
