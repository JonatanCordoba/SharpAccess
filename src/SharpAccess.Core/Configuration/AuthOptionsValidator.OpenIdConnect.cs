namespace SharpAccess.Configuration;

internal sealed partial class AuthOptionsValidator
{
    private static readonly string[] ReservedSharpAccessRoutePatterns =
    [
        "/auth/register",
        "/auth/verify-email",
        "/auth/resend-verification",
        "/auth/login",
        "/auth/change-password",
        "/auth/forgot-password",
        "/auth/reset-password",
        "/auth/refresh",
        "/auth/logout",
        "/auth/revoke",
        "/auth/me",
        "/auth/oauth/{provider}/challenge",
        "/auth/oauth/{provider}/exchange",
        "/admin/users",
        "/admin/users/{userId:guid}/status",
        "/admin/roles",
        "/admin/roles/{roleId:guid}",
        "/admin/permissions",
        "/admin/roles/{roleId:guid}/permissions",
        "/admin/roles/{roleId:guid}/permissions/{permissionId:guid}",
        "/admin/users/{userId:guid}/roles",
        "/admin/users/{userId:guid}/roles/{roleId:guid}",
        "/admin/audit-logs",
        "/tenants",
        "/tenants/{tenantId:guid}",
        "/tenants/{tenantId:guid}/owner",
        "/tenants/{tenantId:guid}/owner/transfer",
        "/tenants/{tenantId:guid}/members",
        "/tenants/{tenantId:guid}/members/{userId:guid}/roles"
    ];

    // Validates bounded keyed OpenID Connect providers and their protocol trust boundaries.
    private static void ValidateOpenIdConnectOptions(
        OpenIdConnectOptions options,
        bool isProduction,
        Dictionary<string, string> secretFingerprints,
        List<string> failures)
    {
        if (options.Providers.Count > 16)
        {
            failures.Add("OpenIdConnect.Providers cannot contain more than 16 providers.");
        }

        HashSet<string> callbackPaths = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string providerName, OpenIdConnectProviderOptions? provider) in options.Providers)
        {
            ValidateOpenIdConnectProvider(
                providerName,
                provider,
                callbackPaths,
                isProduction,
                secretFingerprints,
                failures);
        }
    }

    // Validates one configured provider while preserving the original failure order.
    private static void ValidateOpenIdConnectProvider(
        string providerName,
        OpenIdConnectProviderOptions? provider,
        HashSet<string> callbackPaths,
        bool isProduction,
        Dictionary<string, string> secretFingerprints,
        List<string> failures)
    {
        string field = $"OpenIdConnect.Providers[{providerName}]";
        if (!IsValidProviderName(providerName))
        {
            failures.Add($"{field} must use a lowercase provider name no longer than 64 characters containing only letters, digits, dots, underscores, or hyphens.");
        }

        if (provider is null)
        {
            failures.Add($"{field} cannot be null.");
            return;
        }

        if (!provider.Enabled)
        {
            return;
        }

        RequireText(provider.ClientId, $"{field}.ClientId", failures);
        ValidateSecret(
            provider.ClientSecret,
            $"{field}.ClientSecret",
            8,
            isProduction,
            secretFingerprints,
            failures);
        if (!Enum.IsDefined(provider.ClientAuthenticationMethod))
        {
            failures.Add($"{field}.ClientAuthenticationMethod must be ClientSecretPost or ClientSecretBasic.");
        }

        ValidateOpenIdConnectCallbackPath(provider.CallbackPath, $"{field}.CallbackPath", failures);
        ValidateUniqueCallbackPath(provider.CallbackPath, field, callbackPaths, failures);
        RequireHttpsUri(provider.AuthorizationEndpoint, $"{field}.AuthorizationEndpoint", failures);
        RequireHttpsUri(provider.TokenEndpoint, $"{field}.TokenEndpoint", failures);
        RequireHttpsUri(provider.JsonWebKeySetEndpoint, $"{field}.JsonWebKeySetEndpoint", failures);
        ValidateProviderHosts(provider, field, failures);
        ValidateProviderIssuers(provider.ValidIssuers, field, failures);
        ValidateProviderScopes(provider.Scopes, field, failures);
        ValidateProviderAlgorithms(provider.SigningAlgorithms, field, failures);
        ValidateProviderPrompt(provider.Prompt, field, failures);
    }

    // Requires callback paths to remain unique across enabled providers.
    private static void ValidateUniqueCallbackPath(
        string? callbackPath,
        string field,
        HashSet<string> callbackPaths,
        List<string> failures)
    {
        string normalizedCallbackPath = NormalizeRoutePath(callbackPath);
        if (!string.IsNullOrEmpty(normalizedCallbackPath)
            && !callbackPaths.Add(normalizedCallbackPath))
        {
            failures.Add($"{field}.CallbackPath must be unique across enabled providers.");
        }
    }

    // Rejects unsafe prompt values before they are added to authorization requests.
    private static void ValidateProviderPrompt(
        string? prompt,
        string field,
        List<string> failures)
    {
        if (prompt is null
            || prompt.Length > 128
            || prompt.Any(static character => char.IsControl(character) || character is '&' or '='))
        {
            failures.Add($"{field}.Prompt must be no longer than 128 characters and cannot contain controls, ampersands, or equals signs.");
        }
    }

    // Requires an exact literal callback path outside every route reserved by SharpAccess.
    private static void ValidateOpenIdConnectCallbackPath(
        string? value,
        string field,
        List<string> failures)
    {
        int initialFailureCount = failures.Count;
        ValidateLocalPath(value, field, failures);
        if (failures.Count != initialFailureCount)
        {
            return;
        }

        string callbackPath = value!;
        if (HasUnsafeCallbackPathSyntax(callbackPath))
        {
            failures.Add(
                $"{field} must be an exact literal path without route-template syntax, whitespace, percent escapes, or dot segments.");
            return;
        }

        if (ReservedSharpAccessRoutePatterns.Any(pattern => RoutePatternMatchesPath(pattern, callbackPath)))
        {
            failures.Add($"{field} cannot collide with a route reserved by SharpAccess.");
        }
    }

    // Reports whether a callback path contains templates, whitespace, escapes, or dot segments.
    private static bool HasUnsafeCallbackPathSyntax(string callbackPath) =>
        callbackPath.Any(static character =>
            char.IsWhiteSpace(character)
            || character is '{' or '}' or '*' or '[' or ']' or '%')
        || callbackPath.Split('/').Any(static segment => segment is "." or "..");

    // Matches a literal callback path against one bounded SharpAccess route pattern.
    internal static bool RoutePatternMatchesPath(string pattern, string path)
    {
        string[] patternSegments = NormalizeRoutePath(pattern).Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] pathSegments = NormalizeRoutePath(path).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return patternSegments.Length == pathSegments.Length
            && patternSegments.Zip(
                    pathSegments,
                    static (patternSegment, pathSegment) =>
                        IsRouteParameter(patternSegment)
                        || string.Equals(patternSegment, pathSegment, StringComparison.OrdinalIgnoreCase))
                .All(static matches => matches);
    }

    // Recognizes the parameter segments used by the internal route inventory.
    private static bool IsRouteParameter(string segment) =>
        segment.Length >= 2 && segment[0] == '{' && segment[^1] == '}';

    // Normalizes trailing slashes for ASP.NET route-collision comparisons.
    private static string NormalizeRoutePath(string? path) =>
        string.IsNullOrEmpty(path) || path == "/" ? path ?? string.Empty : path.TrimEnd('/');

    // Requires explicit valid hosts that contain every provider endpoint host.
    private static void ValidateProviderHosts(
        OpenIdConnectProviderOptions provider,
        string field,
        List<string> failures)
    {
        if (!TryCreateAllowedHostSet(provider.AllowedHosts, field, failures, out HashSet<string>? hosts))
        {
            return;
        }

        ValidateEndpointHost(provider.AuthorizationEndpoint, "AuthorizationEndpoint", hosts, field, failures);
        ValidateEndpointHost(provider.TokenEndpoint, "TokenEndpoint", hosts, field, failures);
        ValidateEndpointHost(provider.JsonWebKeySetEndpoint, "JsonWebKeySetEndpoint", hosts, field, failures);
    }

    // Parses and validates a bounded explicit host allowlist.
    private static bool TryCreateAllowedHostSet(
        IList<string>? allowedHosts,
        string field,
        List<string> failures,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out HashSet<string>? hosts)
    {
        hosts = null;
        if (allowedHosts is null || allowedHosts.Count == 0 || allowedHosts.Count > 16)
        {
            failures.Add($"{field}.AllowedHosts must contain between 1 and 16 explicit hosts.");
            return false;
        }

        hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? host in allowedHosts)
        {
            if (!IsValidAllowedHost(host))
            {
                failures.Add($"{field}.AllowedHosts contains an invalid host.");
                continue;
            }

            hosts.Add(host!);
        }

        return true;
    }

    // Reports whether a configured allowlist entry is a bounded bare DNS or IP host.
    private static bool IsValidAllowedHost(string? host) =>
        !string.IsNullOrWhiteSpace(host)
        && host.Length <= 253
        && string.Equals(host, host.Trim(), StringComparison.Ordinal)
        && !host.Contains('/')
        && !host.Contains(':')
        && Uri.CheckHostName(host) != UriHostNameType.Unknown;

    // Requires one absolute endpoint host to appear in the validated allowlist.
    private static void ValidateEndpointHost(
        Uri? endpoint,
        string endpointName,
        HashSet<string> hosts,
        string field,
        List<string> failures)
    {
        if (endpoint?.IsAbsoluteUri == true && !hosts.Contains(endpoint.IdnHost))
        {
            failures.Add($"{field}.{endpointName} host must appear in {field}.AllowedHosts.");
        }
    }

    // Validates bounded exact HTTPS issuers and explicitly configured legacy DNS issuer identifiers.
    private static void ValidateProviderIssuers(
        IList<string>? issuers,
        string field,
        List<string> failures)
    {
        if (issuers is null || issuers.Count == 0 || issuers.Count > 8)
        {
            failures.Add($"{field}.ValidIssuers must contain between 1 and 8 exact issuers.");
            return;
        }

        foreach (string? issuer in issuers)
        {
            if (!IsValidProviderIssuer(issuer))
            {
                failures.Add($"{field}.ValidIssuers must contain bounded exact HTTPS issuer or legacy DNS issuer identifiers.");
            }
        }
    }

    // Reports whether one issuer is a bounded legacy DNS name or exact HTTPS URI.
    private static bool IsValidProviderIssuer(string? issuer)
    {
        if (!HasValidIssuerText(issuer))
        {
            return false;
        }

        if (IsLegacyDnsIssuer(issuer))
        {
            return true;
        }

        return Uri.TryCreate(issuer, UriKind.Absolute, out Uri? issuerUri)
            && IsValidHttpsIssuerUri(issuerUri);
    }

    // Validates issuer text bounds, trimming, and control characters.
    private static bool HasValidIssuerText(
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? issuer) =>
        !string.IsNullOrWhiteSpace(issuer)
        && issuer.Length <= 512
        && string.Equals(issuer, issuer.Trim(), StringComparison.Ordinal)
        && !issuer.Any(char.IsControl);

    // Recognizes the explicitly supported legacy DNS issuer representation.
    private static bool IsLegacyDnsIssuer(string issuer) =>
        issuer.Contains('.', StringComparison.Ordinal)
        && Uri.CheckHostName(issuer) == UriHostNameType.Dns;

    // Requires an exact HTTPS issuer URI without credentials, query, or fragment.
    private static bool IsValidHttpsIssuerUri(Uri issuerUri) =>
        issuerUri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(issuerUri.Query)
        && string.IsNullOrEmpty(issuerUri.Fragment)
        && string.IsNullOrEmpty(issuerUri.UserInfo);

    // Requires bounded OIDC scopes containing both openid and email.
    private static void ValidateProviderScopes(
        IList<string>? scopes,
        string field,
        List<string> failures)
    {
        if (scopes is null || scopes.Count == 0 || scopes.Count > 16)
        {
            failures.Add($"{field}.Scopes must contain between 1 and 16 scopes.");
            return;
        }

        foreach (string? scope in scopes)
        {
            if (!IsValidProviderScope(scope))
            {
                failures.Add($"{field}.Scopes contains an invalid scope.");
            }
        }

        if (!scopes.Contains("openid", StringComparer.Ordinal)
            || !scopes.Contains("email", StringComparer.Ordinal))
        {
            failures.Add($"{field}.Scopes must contain openid and email.");
        }
    }

    // Reports whether a provider scope is a bounded non-whitespace token.
    private static bool IsValidProviderScope(string? scope) =>
        !string.IsNullOrWhiteSpace(scope)
        && scope.Length <= 64
        && !scope.Any(static character => char.IsControl(character) || char.IsWhiteSpace(character));

    // Restricts provider signing algorithms to the asymmetric allowlist.
    private static void ValidateProviderAlgorithms(
        IList<string>? algorithms,
        string field,
        List<string> failures)
    {
        string[] allowedAlgorithms = ["RS256", "PS256", "ES256"];
        if (algorithms is null || algorithms.Count == 0 || algorithms.Count > 8)
        {
            failures.Add($"{field}.SigningAlgorithms must contain between 1 and 8 algorithms.");
            return;
        }

        foreach (string? algorithm in algorithms)
        {
            if (algorithm is null || !allowedAlgorithms.Contains(algorithm, StringComparer.Ordinal))
            {
                failures.Add($"{field}.SigningAlgorithms supports only RS256, PS256, and ES256.");
            }
        }
    }

    // Restricts persistence and route provider keys to a bounded lowercase token.
    private static bool IsValidProviderName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 64
            || !IsValidProviderNameStart(value[0]))
        {
            return false;
        }

        return value.All(IsValidProviderNameCharacter);
    }

    // Reports whether the first provider-name character is lowercase alphanumeric.
    private static bool IsValidProviderNameStart(char value) =>
        value is >= 'a' and <= 'z'
        or >= '0' and <= '9';

    // Reports whether a provider-name character belongs to the bounded token alphabet.
    private static bool IsValidProviderNameCharacter(char value) =>
        IsValidProviderNameStart(value)
        || value is '.' or '_' or '-';
}
