namespace SharpAccess.Configuration;

/// <summary>Selects which SharpAccess middleware components the convenience pipeline installs.</summary>
public sealed class SharpAccessMiddlewareOptions
{
    /// <summary>Gets or sets whether SharpAccess installs its exception handler and bare-status ProblemDetails boundary.</summary>
    public bool InstallExceptionHandler { get; set; }

    /// <summary>Gets or sets whether SharpAccess installs package security headers.</summary>
    public bool InstallSecurityHeaders { get; set; }

    /// <summary>Gets or sets whether SharpAccess protects cookie-backed refresh and logout mutations.</summary>
    public bool InstallCookieProtection { get; set; } = true;

    /// <summary>Gets or sets whether SharpAccess installs its endpoint rate limiter.</summary>
    public bool InstallRateLimiter { get; set; } = true;

    /// <summary>Gets or sets whether SharpAccess installs ASP.NET Core authentication middleware.</summary>
    public bool InstallAuthentication { get; set; } = true;

    /// <summary>Gets or sets whether SharpAccess enforces recent authentication for sensitive mutations.</summary>
    public bool InstallFreshAuthentication { get; set; } = true;

    /// <summary>Gets or sets whether SharpAccess installs ASP.NET Core authorization middleware.</summary>
    public bool InstallAuthorization { get; set; } = true;
}

/// <summary>Configures the optional security-header middleware without imposing a fixed host CSP.</summary>
public sealed class SharpAccessSecurityHeadersOptions
{
    /// <summary>Gets or sets the X-Content-Type-Options value, or null to omit the header.</summary>
    public string? ContentTypeOptions { get; set; } = "nosniff";

    /// <summary>Gets or sets the X-Frame-Options value, or null to omit the header.</summary>
    public string? FrameOptions { get; set; } = "DENY";

    /// <summary>Gets or sets the Referrer-Policy value, or null to omit the header.</summary>
    public string? ReferrerPolicy { get; set; } = "no-referrer";

    /// <summary>Gets or sets the Permissions-Policy value, or null to omit the header.</summary>
    public string? PermissionsPolicy { get; set; } = "camera=(), microphone=(), geolocation=()";

    /// <summary>Gets or sets the Content-Security-Policy value. The default is null so host policy is never silently selected.</summary>
    public string? ContentSecurityPolicy { get; set; }
}
