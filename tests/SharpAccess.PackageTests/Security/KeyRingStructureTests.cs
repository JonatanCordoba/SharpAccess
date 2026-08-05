namespace SharpAccess.PackageTests;

public sealed class KeyRingStructureTests
{
    // Requires public key-ring contracts, configured material handling, and validation to remain separate responsibilities.
    [Fact]
    public void KeyRingResponsibilitiesAreSeparated()
    {
        string root = FindRepositoryRoot();
        string contracts = File.ReadAllText(Path.Combine(root, "src", "SharpAccess.Core", "AccessTokenSigningKeyRing.cs"));
        string configured = File.ReadAllText(Path.Combine(root, "src", "SharpAccess.Core", "Tokens", "ConfiguredAccessTokenSigningKeyRing.cs"));
        string guard = File.ReadAllText(Path.Combine(root, "src", "SharpAccess.Core", "Tokens", "AccessTokenKeyRingGuard.cs"));

        Assert.Contains("public interface IAccessTokenSigningKeyRing", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfiguredAccessTokenSigningKeyRing", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("ValidateCertificate", contracts, StringComparison.Ordinal);
        Assert.Contains("base64:", configured, StringComparison.Ordinal);
        Assert.Contains("utf8:", configured, StringComparison.Ordinal);
        Assert.Contains("legacy Base64-or-UTF8 interpretation", configured, StringComparison.Ordinal);
        Assert.Contains("ValidateCertificate", guard, StringComparison.Ordinal);
        Assert.Contains("SecurityAlgorithms.HmacSha256", guard, StringComparison.Ordinal);
        Assert.Contains("SecurityAlgorithms.RsaSha256", guard, StringComparison.Ordinal);
        Assert.Contains("SecurityAlgorithms.EcdsaSha256", guard, StringComparison.Ordinal);
    }

    // Locates the repository root from the current test process.
    private static string FindRepositoryRoot()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "SharpAccess.sln"))) { return directory.FullName; }
                directory = directory.Parent;
            }
        }
        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
