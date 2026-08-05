namespace SharpAccess.Configuration;

/// <summary>Configures an explicitly requested local development or test administrator seed.</summary>
public sealed class AdminSeedOptions
{
    /// <summary>Gets or sets the administrator email address.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Gets or sets the administrator password.</summary>
    public string Password { get; set; } = string.Empty;
}
