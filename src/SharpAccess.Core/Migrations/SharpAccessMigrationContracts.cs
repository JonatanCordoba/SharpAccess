namespace SharpAccess.Configuration;

/// <summary>Selects how SharpAccess handles provider-owned schema migrations during application initialization.</summary>
public enum SharpAccessMigrationMode
{
    /// <summary>Applies pending provider-owned migrations during explicit SharpAccess initialization.</summary>
    ApplyAtStartup = 0,
    /// <summary>Validates that the provider-owned schema is current without changing it.</summary>
    ValidateOnly = 1,
    /// <summary>Leaves migration execution to an external deployment process.</summary>
    External = 2,
    /// <summary>Generates a provider-native migration script at the configured output path.</summary>
    GenerateScript = 3
}

/// <summary>Configures provider-neutral migration behavior without exposing a concrete database provider.</summary>
public sealed class SharpAccessMigrationOptions
{
    /// <summary>Gets or sets an explicit mode; null selects the environment-safe default.</summary>
    public SharpAccessMigrationMode? Mode { get; set; }

    /// <summary>Gets or sets the destination used when initialization runs in GenerateScript mode.</summary>
    public string? ScriptOutputPath { get; set; }
}
