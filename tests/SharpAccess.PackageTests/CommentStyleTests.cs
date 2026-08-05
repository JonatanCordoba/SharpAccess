using System.Text.RegularExpressions;

namespace SharpAccess.PackageTests;

public sealed partial class CommentStyleTests
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        "artifacts",
        "bin",
        "obj"
    };

    private static readonly HashSet<string> ApprovedXmlDocumentationFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "providers/SharpAccess.Postgres/Configuration/PostgresAuthOptions.cs",
        "providers/SharpAccess.Postgres/DependencyInjection/PostgresServiceCollectionExtensions.cs",
        "providers/SharpAccess.Sqlite/Configuration/SqliteAuthOptions.cs",
        "providers/SharpAccess.Sqlite/DependencyInjection/SqliteServiceCollectionExtensions.cs",
        "src/SharpAccess.Core/Abstractions/IEmailSender.cs",
        "src/SharpAccess.Core/AccessTokenSigningKeyRing.cs",
        "src/SharpAccess.Core/Attributes/AuthAttributes.cs",
        "src/SharpAccess.Core/AuthConstants.cs",
        "src/SharpAccess.Core/Authorization/AttributedEndpointExtensions.cs",
        "src/SharpAccess.Core/Configuration/AdminSeedOptions.cs",
        "src/SharpAccess.Core/Configuration/AuthOptions.cs",
        "src/SharpAccess.Core/Configuration/SharpAccessMiddlewareOptions.cs",
        "src/SharpAccess.Core/Extensions/AuthApplicationExtensions.cs",
        "src/SharpAccess.Core/Extensions/AuthServiceCollectionExtensions.cs",
        "src/SharpAccess.Core/IPasswordRiskValidator.cs",
        "src/SharpAccess.Core/Migrations/SharpAccessMigrationContracts.cs",
        "src/SharpAccess.Core/Migrations/SharpAccessSchemaStatus.cs",
        "src/SharpAccess.Core/Pagination.cs",
        "src/SharpAccess.Core/Security/AuthRateLimitPartitionKeyProvider.cs"
    };

    // Verifies that XML documentation is limited to the reviewed public package surface.
    [Fact]
    public void HandwrittenCSharpUsesApprovedCommentStyles()
    {
        string root = FindRepositoryRoot();
        string[] files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => IsHandwrittenSource(root, file))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(files);
        HashSet<string> observedApprovedFiles = new(StringComparer.OrdinalIgnoreCase);
        foreach (string file in files)
        {
            string relativePath = Path.GetRelativePath(root, file).Replace('\\', '/');
            string text = File.ReadAllText(file);
            if (ApprovedXmlDocumentationFiles.Contains(relativePath))
            {
                Assert.True(
                    XmlDocumentationComment().IsMatch(text),
                    $"Approved public-surface documentation is missing: {relativePath}");
                observedApprovedFiles.Add(relativePath);
            }
            else
            {
                Assert.False(
                    XmlDocumentationComment().IsMatch(text),
                    $"XML documentation is not approved for this handwritten source: {relativePath}");
                Assert.False(
                    SummaryTag().IsMatch(text),
                    $"Summary tags are not approved for this handwritten source: {relativePath}");
            }

            Assert.False(
                ConvertedXmlMetadataComment().IsMatch(text),
                $"Converted XML metadata comments are forbidden: {relativePath}");
        }

        Assert.Equal(
            ApprovedXmlDocumentationFiles.Order(StringComparer.OrdinalIgnoreCase),
            observedApprovedFiles.Order(StringComparer.OrdinalIgnoreCase));
    }

    // Excludes generated output and local tooling directories from handwritten-source validation.
    private static bool IsHandwrittenSource(string root, string file)
    {
        string relativePath = Path.GetRelativePath(root, file);
        string[] segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        return !segments.Any(ExcludedDirectories.Contains);
    }

    // Finds the repository root from the built test assembly.
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SharpAccess.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the SharpAccess repository root.");
    }

    // Matches C# XML documentation comments while allowing ordinary comments with four or more slashes.
    [GeneratedRegex(@"^\s*///(?!/)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex XmlDocumentationComment();

    // Matches correct or commonly mistyped summary tags without relying on casing.
    [GeneratedRegex(@"<\s*/?\s*summ?ary\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SummaryTag();

    // Matches metadata lines mechanically converted from XML param, returns, typeparam, value, exception, or remarks tags.
    [GeneratedRegex(
        @"^\s*//\s*(?:Parameter\s+\S+\s*:|Returns\s*:|Type\s+parameter\s+\S+\s*:|Value\s*:|Exception\s+\S+\s*:|Remarks\s*:)",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ConvertedXmlMetadataComment();
}
