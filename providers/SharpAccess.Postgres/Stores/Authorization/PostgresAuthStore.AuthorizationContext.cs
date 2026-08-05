using SharpAccess.Domain;

namespace SharpAccess.Postgres;

internal sealed partial class PostgresAuthStore
{
    private sealed class EffectiveAuthorizationAccumulator
    {
        private readonly Guid? _tenantId;
        private readonly List<string> _globalRoles = [];
        private readonly List<string> _globalPermissions = [];
        private readonly List<string> _tenantRoles = [];
        private readonly List<string> _tenantPermissions = [];
        private bool _isOwner;
        private long _authorizationVersion;

        public EffectiveAuthorizationAccumulator(Guid? tenantId)
        {
            _tenantId = tenantId;
        }

        public void Add(string kind, string name, long authorizationVersion)
        {
            _authorizationVersion = authorizationVersion;
            switch (kind)
            {
                case "global_role":
                    _globalRoles.Add(name);
                    break;
                case "global_permission":
                    _globalPermissions.Add(name);
                    break;
                case "tenant_owner":
                    _isOwner = true;
                    break;
                case "tenant_role":
                    _tenantRoles.Add(name);
                    break;
                case "tenant_permission":
                    _tenantPermissions.Add(name);
                    break;
            }
        }

        public EffectiveAuthorizationContext Build()
        {
            TenantAuthorizationContext? tenant = _tenantId.HasValue
                ? new TenantAuthorizationContext(
                    _tenantId.Value,
                    _isOwner,
                    _tenantRoles.Distinct(StringComparer.Ordinal).ToArray(),
                    _tenantPermissions.Distinct(StringComparer.Ordinal).ToArray())
                : null;

            return new EffectiveAuthorizationContext(
                new GlobalAuthorizationContext(
                    _globalRoles.Distinct(StringComparer.Ordinal).ToArray(),
                    _globalPermissions.Distinct(StringComparer.Ordinal).ToArray()),
                tenant,
                _authorizationVersion);
        }
    }
}
