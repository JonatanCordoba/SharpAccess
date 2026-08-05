namespace SharpAccess.SampleApi;

internal sealed record SampleModule(
    string Id,
    string DisplayName,
    string Description,
    string RoleName,
    string PermissionName,
    string Icon);

internal static class SampleModuleCatalog
{
    internal static IReadOnlyList<SampleModule> All { get; } =
    [
        new(
            "users",
            "User workspace",
            "Read the sample user directory and account state.",
            "Sample Module - Users",
            AuthPermissions.UsersRead,
            "people"),
        new(
            "tenants",
            "Tenant workspace",
            "Read tenant records available to a privileged sample operator.",
            "Sample Module - Tenants",
            AuthPermissions.TenantsRead,
            "domain"),
        new(
            "roles",
            "Role workspace",
            "Read the global role catalog used by the sample console.",
            "Sample Module - Roles",
            AuthPermissions.RolesRead,
            "badge"),
        new(
            "audit",
            "Audit workspace",
            "Read bounded security audit events from the sample database.",
            "Sample Module - Audit",
            AuthPermissions.AuditRead,
            "history")
    ];
}
