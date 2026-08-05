using SharpAccess.Persistence;

namespace SharpAccess.Sqlite;

internal sealed partial class SqliteAuthStore(ISqliteAuthConnectionFactory connections) : IAuthDatabase
{
    private readonly ISqliteAuthConnectionFactory _connections = connections;
    private const string VerificationPurpose = "email_verification";
    private const string PasswordResetPurpose = "password_reset";
    private const string AdminRoleId = "10000000-0000-0000-0000-000000000001";
    private const string UserRoleId = "10000000-0000-0000-0000-000000000002";
    private const string TenantOwnerRoleId = "40000000-0000-0000-0000-000000000001";
    private const string TenantManagerRoleId = "40000000-0000-0000-0000-000000000002";
    private const string TenantMemberRoleId = "40000000-0000-0000-0000-000000000003";
    private const string TenantReadPermissionId = "30000000-0000-0000-0000-000000000001";
    private const string TenantMembersReadPermissionId = "30000000-0000-0000-0000-000000000002";
    private const string TenantMembersManagePermissionId = "30000000-0000-0000-0000-000000000003";
    private const string TenantRolesManagePermissionId = "30000000-0000-0000-0000-000000000004";
    private const string TenantOwnershipTransferPermissionId = "30000000-0000-0000-0000-000000000005";
}
