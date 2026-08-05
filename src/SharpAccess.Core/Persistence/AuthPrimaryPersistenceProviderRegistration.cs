namespace SharpAccess.Persistence;

internal sealed class AuthPrimaryPersistenceProviderRegistration
{
    public AuthPrimaryPersistenceProviderRegistration(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A provider name is required.", nameof(name));
        }

        Name = name.Trim();
    }

    public string Name { get; }
}
