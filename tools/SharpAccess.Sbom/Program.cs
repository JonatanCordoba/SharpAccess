using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

internal sealed record GeneratorOptions(
    string RepositoryRoot,
    string OutputDirectory,
    string PackagesDirectory,
    Uri RepositoryUrl,
    string RepositoryIdentity,
    string Revision,
    string PackageVersion,
    DateTimeOffset CreatedUtc,
    bool StablePublication,
    IReadOnlySet<string> RequiredPackageArchives,
    IReadOnlyList<string> ProjectPaths);

internal sealed record ProjectMetadata(
    string Id,
    string Version,
    string Description,
    string License,
    string ProjectPath);

internal sealed record Dependency(string Id, string Version);

internal static partial class Program
{
    private const string DevelopmentRepositoryUrl =
        "https://github.com/JonatanCordoba/dotnet-auth";
    private const string CanonicalRepositoryUrl =
        "https://github.com/JonatanCordoba/SharpAccess";

    private static readonly string[] DefaultProjectPaths =
    [
        "src/SharpAccess.Core/SharpAccess.Core.csproj",
        "providers/SharpAccess.Sqlite/SharpAccess.Sqlite.csproj",
        "providers/SharpAccess.Postgres/SharpAccess.Postgres.csproj"
    ];

    private static readonly IReadOnlyDictionary<string, string>
        ProviderStatusProperties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SharpAccess.Core"] = "SharpAccessCoreStatus",
            ["SharpAccess.Sqlite"] = "SharpAccessSqliteStatus",
            ["SharpAccess.Postgres"] = "SharpAccessPostgresStatus"
        };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static int Main(string[] args)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException(
                    "SharpAccess SBOM generation is supported on Windows only.");
            }

            GeneratorOptions options = ParseOptions(args);
            Directory.CreateDirectory(options.OutputDirectory);
            List<JsonObject> evidence = [];

            foreach (string projectPath in options.ProjectPaths.Order(StringComparer.Ordinal))
            {
                ProjectMetadata project = ReadProject(
                    projectPath,
                    options.RepositoryRoot,
                    options.PackageVersion);
                List<Dependency> dependencies = ReadDependencies(
                    Path.Combine(Path.GetDirectoryName(projectPath)!, "packages.lock.json"));
                string? archive = ResolveArchive(options, project);
                string rootHashSource = archive ?? projectPath;
                string rootHash = HashFile(rootHashSource);
                string rootRef = $"pkg:nuget/{project.Id}@{project.Version}";

                JsonObject cyclone = BuildCycloneDx(
                    options,
                    project,
                    dependencies,
                    rootRef,
                    rootHash);
                JsonObject spdx = BuildSpdx(
                    options,
                    project,
                    dependencies,
                    rootRef,
                    rootHash);
                ValidateCycloneDx(cyclone, project.Id, project.Version);
                ValidateSpdx(spdx, project.Id, project.Version);

                string cycloneName = $"{project.Id}.cyclonedx.json";
                string spdxName = $"{project.Id}.spdx.json";
                string cyclonePath = Path.Combine(options.OutputDirectory, cycloneName);
                string spdxPath = Path.Combine(options.OutputDirectory, spdxName);
                WriteJson(cyclonePath, cyclone);
                WriteJson(spdxPath, spdx);
                evidence.Add(new JsonObject
                {
                    ["package"] = project.Id,
                    ["version"] = project.Version,
                    ["dependencyCount"] = dependencies.Count,
                    ["rootHashSource"] = Path.GetRelativePath(
                        options.RepositoryRoot,
                        rootHashSource).Replace('\\', '/'),
                    ["rootSha256"] = rootHash,
                    ["cycloneDx"] = EvidenceFile(
                        cycloneName,
                        cyclonePath,
                        "CycloneDX",
                        "1.6"),
                    ["spdx"] = EvidenceFile(
                        spdxName,
                        spdxPath,
                        "SPDX",
                        "2.3")
                });
            }

            JsonObject manifest = new()
            {
                ["schemaVersion"] = 3,
                ["repository"] = options.RepositoryUrl.AbsoluteUri.TrimEnd('/'),
                ["repositoryIdentity"] = options.RepositoryIdentity,
                ["publicationMode"] = options.StablePublication
                    ? "stable"
                    : "release-candidate",
                ["supportedPlatform"] = "Windows",
                ["revision"] = options.Revision,
                ["packageVersion"] = options.PackageVersion,
                ["createdUtc"] = options.CreatedUtc
                    .ToUniversalTime()
                    .ToString("O", CultureInfo.InvariantCulture),
                ["packages"] = new JsonArray(
                    evidence
                        .OrderBy(node => node["package"]!.GetValue<string>(), StringComparer.Ordinal)
                        .Select(node => (JsonNode)node)
                        .ToArray())
            };
            WriteJson(Path.Combine(options.OutputDirectory, "sbom-evidence.json"), manifest);
            Console.WriteLine(
                $"Generated deterministic CycloneDX 1.6 and SPDX 2.3 documents for {evidence.Count} active package roots.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"SBOM generation failed: {exception.Message}");
            return 1;
        }
    }

    private static GeneratorOptions ParseOptions(string[] args)
    {
        string repositoryRoot = Path.GetFullPath(
            RequireValue(args, "--repository-root"));
        if (!File.Exists(Path.Combine(repositoryRoot, "SharpAccess.sln")))
        {
            throw new DirectoryNotFoundException("The repository root is invalid.");
        }

        string outputDirectory = Path.GetFullPath(
            RequireValue(args, "--output-directory"));
        string packagesDirectory = Path.GetFullPath(
            RequireValue(args, "--packages-directory"));
        (Uri repositoryUrl, string repositoryIdentity) =
            ParseRepositoryIdentity(RequireValue(args, "--repository-url"));
        string revision = RequireValue(args, "--revision").ToLowerInvariant();
        if (!RevisionPattern().IsMatch(revision))
        {
            throw new ArgumentException(
                "--revision must be a full 40-character hexadecimal Git revision.");
        }

        string head = RunGit(repositoryRoot, "rev-parse", "HEAD")
            .Trim()
            .ToLowerInvariant();
        if (!string.Equals(revision, head, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Revision must equal checked-out HEAD {head}.");
        }

        if (OptionalValue(args, "--created-utc") is not null)
        {
            throw new ArgumentException(
                "CreatedUtc is derived from Git and must not be supplied.");
        }

        DateTimeOffset createdUtc = DateTimeOffset.Parse(
            RunGit(repositoryRoot, "show", "-s", "--format=%cI", revision).Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal).ToUniversalTime();
        string packageVersion = ReadPackageVersion(repositoryRoot);

        List<string> requestedProjects = Values(args, "--package-project");
        if (requestedProjects.Count == 0)
        {
            requestedProjects.AddRange(DefaultProjectPaths);
        }

        string[] normalizedProjects = requestedProjects
            .Select(path => Path.GetFullPath(Path.Combine(repositoryRoot, path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();
        HashSet<string> expectedProjects = DefaultProjectPaths
            .Select(path => Path.GetFullPath(Path.Combine(repositoryRoot, path)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (normalizedProjects.Length != 3
            || normalizedProjects.Any(path => !File.Exists(path))
            || !expectedProjects.SetEquals(normalizedProjects))
        {
            throw new InvalidOperationException(
                "Exactly the Core, SQLite, and PostgreSQL package projects must be present.");
        }

        HashSet<string> requiredArchives = Values(args, "--require-package-archive")
            .ToHashSet(StringComparer.Ordinal);
        bool stablePublication = args.Contains(
            "--stable-publication",
            StringComparer.Ordinal);
        if (args.Contains("--require-all-package-archives", StringComparer.Ordinal))
        {
            requiredArchives.UnionWith(ProviderStatusProperties.Keys);
        }

        string[] unknownArchives = requiredArchives
            .Except(ProviderStatusProperties.Keys, StringComparer.Ordinal)
            .ToArray();
        if (unknownArchives.Length != 0)
        {
            throw new ArgumentException(
                $"Unknown required package archive roots: {string.Join(", ", unknownArchives)}");
        }

        Dictionary<string, string> statuses = ReadProviderStatuses(repositoryRoot);
        if (stablePublication
            && (!string.Equals(
                    repositoryIdentity,
                    "canonical-public",
                    StringComparison.Ordinal)
                || !requiredArchives.SetEquals(ProviderStatusProperties.Keys)
                || statuses.Values.Any(
                    status => !string.Equals(
                        status,
                        "Supported",
                        StringComparison.Ordinal))))
        {
            throw new InvalidOperationException(
                "Stable publication requires the canonical repository, all three package archives, and Supported status for Core, SQLite, and PostgreSQL.");
        }

        return new GeneratorOptions(
            repositoryRoot,
            outputDirectory,
            packagesDirectory,
            repositoryUrl,
            repositoryIdentity,
            revision,
            packageVersion,
            createdUtc,
            stablePublication,
            requiredArchives,
            normalizedProjects);
    }

    private static string ReadPackageVersion(string repositoryRoot)
    {
        string path = Path.Combine(repositoryRoot, "eng", "Version.props");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "The authoritative package-version file is missing.",
                path);
        }

        XDocument document = XDocument.Load(path);
        string version = document.Descendants("SharpAccessVersion")
            .SingleOrDefault()?
            .Value
            .Trim()
            ?? throw new InvalidOperationException(
                "SharpAccessVersion is missing from eng/Version.props.");
        if (!SemanticVersionPattern().IsMatch(version))
        {
            throw new InvalidOperationException(
                $"SharpAccessVersion is not a valid semantic version: {version}");
        }

        return version;
    }

    private static ProjectMetadata ReadProject(
        string projectPath,
        string repositoryRoot,
        string packageVersion)
    {
        XDocument document = XDocument.Load(projectPath);
        string Required(string name) => document
            .Descendants(name)
            .Select(element => element.Value.Trim())
            .FirstOrDefault(value => value.Length != 0)
            ?? throw new InvalidOperationException(
                $"{name} is missing from {projectPath}.");

        if (document.Descendants("Version").Any())
        {
            throw new InvalidOperationException(
                $"Package project must inherit the authoritative version instead of declaring Version: {projectPath}");
        }

        return new ProjectMetadata(
            Required("PackageId"),
            packageVersion,
            Required("Description"),
            Required("PackageLicenseExpression"),
            Path.GetRelativePath(repositoryRoot, projectPath).Replace('\\', '/'));
    }

    private static List<Dependency> ReadDependencies(string lockPath)
    {
        if (!File.Exists(lockPath))
        {
            throw new FileNotFoundException("Package lock file is missing.", lockPath);
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(lockPath));
        JsonElement frameworks = document.RootElement.GetProperty("dependencies");
        JsonElement framework = frameworks
            .EnumerateObject()
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .First()
            .Value;
        return framework
            .EnumerateObject()
            .Select(property => new Dependency(
                property.Name,
                property.Value.TryGetProperty("resolved", out JsonElement resolved)
                    ? resolved.GetString() ?? "unknown"
                    : "unknown"))
            .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Version, StringComparer.Ordinal)
            .ToList();
    }

    private static string? ResolveArchive(
        GeneratorOptions options,
        ProjectMetadata project)
    {
        string archive = Path.Combine(
            options.PackagesDirectory,
            $"{project.Id}.{project.Version}.nupkg");
        if (File.Exists(archive))
        {
            return archive;
        }
        if (options.RequiredPackageArchives.Contains(project.Id))
        {
            throw new FileNotFoundException(
                $"Required package archive is missing: {archive}");
        }
        return null;
    }

    private static JsonObject BuildCycloneDx(
        GeneratorOptions options,
        ProjectMetadata project,
        IReadOnlyList<Dependency> dependencies,
        string rootRef,
        string rootHash)
    {
        JsonArray components = new(
            dependencies.Select(dependency => (JsonNode)new JsonObject
            {
                ["type"] = "library",
                ["bom-ref"] = $"pkg:nuget/{dependency.Id}@{dependency.Version}",
                ["name"] = dependency.Id,
                ["version"] = dependency.Version,
                ["purl"] = $"pkg:nuget/{dependency.Id}@{dependency.Version}"
            }).ToArray());
        JsonArray dependsOn = new(
            dependencies
                .Select(dependency =>
                    (JsonNode)$"pkg:nuget/{dependency.Id}@{dependency.Version}")
                .ToArray());
        return new JsonObject
        {
            ["bomFormat"] = "CycloneDX",
            ["specVersion"] = "1.6",
            ["serialNumber"] =
                $"urn:uuid:{DeterministicGuid($"{options.Revision}|{project.Id}|cyclonedx")}",
            ["version"] = 1,
            ["metadata"] = new JsonObject
            {
                ["timestamp"] = options.CreatedUtc.ToString(
                    "O",
                    CultureInfo.InvariantCulture),
                ["component"] = new JsonObject
                {
                    ["type"] = "library",
                    ["bom-ref"] = rootRef,
                    ["name"] = project.Id,
                    ["version"] = project.Version,
                    ["description"] = project.Description,
                    ["purl"] = rootRef,
                    ["hashes"] = new JsonArray(
                        new JsonObject
                        {
                            ["alg"] = "SHA-256",
                            ["content"] = rootHash
                        }),
                    ["licenses"] = new JsonArray(
                        new JsonObject
                        {
                            ["license"] = new JsonObject
                            {
                                ["id"] = project.License
                            }
                        })
                }
            },
            ["components"] = components,
            ["dependencies"] = new JsonArray(
                new JsonObject
                {
                    ["ref"] = rootRef,
                    ["dependsOn"] = dependsOn
                })
        };
    }

    private static JsonObject BuildSpdx(
        GeneratorOptions options,
        ProjectMetadata project,
        IReadOnlyList<Dependency> dependencies,
        string rootRef,
        string rootHash)
    {
        string rootId = $"SPDXRef-Package-{Sanitize(project.Id)}";
        JsonArray packages = new(
            new JsonObject
            {
                ["name"] = project.Id,
                ["SPDXID"] = rootId,
                ["versionInfo"] = project.Version,
                ["downloadLocation"] = "NOASSERTION",
                ["filesAnalyzed"] = false,
                ["licenseConcluded"] = "NOASSERTION",
                ["licenseDeclared"] = project.License,
                ["externalRefs"] = new JsonArray(
                    new JsonObject
                    {
                        ["referenceCategory"] = "PACKAGE-MANAGER",
                        ["referenceType"] = "purl",
                        ["referenceLocator"] = rootRef
                    }),
                ["checksums"] = new JsonArray(
                    new JsonObject
                    {
                        ["algorithm"] = "SHA256",
                        ["checksumValue"] = rootHash
                    })
            });
        JsonArray relationships = new(
            new JsonObject
            {
                ["spdxElementId"] = "SPDXRef-DOCUMENT",
                ["relationshipType"] = "DESCRIBES",
                ["relatedSpdxElement"] = rootId
            });

        foreach (Dependency dependency in dependencies)
        {
            string id =
                $"SPDXRef-Package-{Sanitize(dependency.Id)}-{Sanitize(dependency.Version)}";
            packages.Add(new JsonObject
            {
                ["name"] = dependency.Id,
                ["SPDXID"] = id,
                ["versionInfo"] = dependency.Version,
                ["downloadLocation"] = "NOASSERTION",
                ["filesAnalyzed"] = false,
                ["licenseConcluded"] = "NOASSERTION",
                ["licenseDeclared"] = "NOASSERTION",
                ["externalRefs"] = new JsonArray(
                    new JsonObject
                    {
                        ["referenceCategory"] = "PACKAGE-MANAGER",
                        ["referenceType"] = "purl",
                        ["referenceLocator"] =
                            $"pkg:nuget/{dependency.Id}@{dependency.Version}"
                    })
            });
            relationships.Add(new JsonObject
            {
                ["spdxElementId"] = rootId,
                ["relationshipType"] = "DEPENDS_ON",
                ["relatedSpdxElement"] = id
            });
        }

        return new JsonObject
        {
            ["spdxVersion"] = "SPDX-2.3",
            ["dataLicense"] = "CC0-1.0",
            ["SPDXID"] = "SPDXRef-DOCUMENT",
            ["name"] = $"{project.Id}-{project.Version}",
            ["documentNamespace"] =
                $"{options.RepositoryUrl.AbsoluteUri.TrimEnd('/')}/sbom/{options.Revision}/{project.Id}",
            ["creationInfo"] = new JsonObject
            {
                ["created"] = options.CreatedUtc.ToString(
                    "O",
                    CultureInfo.InvariantCulture),
                ["creators"] = new JsonArray("Tool: SharpAccess.Sbom")
            },
            ["packages"] = packages,
            ["relationships"] = relationships
        };
    }

    private static void ValidateCycloneDx(
        JsonObject document,
        string packageId,
        string packageVersion)
    {
        if (document["bomFormat"]?.GetValue<string>() != "CycloneDX"
            || document["specVersion"]?.GetValue<string>() != "1.6"
            || document["metadata"]?["component"]?["name"]?.GetValue<string>()
                != packageId
            || document["metadata"]?["component"]?["version"]?.GetValue<string>()
                != packageVersion)
        {
            throw new InvalidOperationException(
                $"CycloneDX validation failed for {packageId}.");
        }
    }

    private static void ValidateSpdx(
        JsonObject document,
        string packageId,
        string packageVersion)
    {
        if (document["spdxVersion"]?.GetValue<string>() != "SPDX-2.3"
            || document["packages"] is not JsonArray packages
            || !packages.OfType<JsonObject>().Any(package =>
                package["name"]?.GetValue<string>() == packageId
                && package["versionInfo"]?.GetValue<string>() == packageVersion))
        {
            throw new InvalidOperationException(
                $"SPDX validation failed for {packageId}.");
        }
    }

    private static JsonObject EvidenceFile(
        string name,
        string path,
        string format,
        string specificationVersion) => new()
    {
        ["file"] = name,
        ["format"] = format,
        ["specificationVersion"] = specificationVersion,
        ["sha256"] = HashFile(path)
    };

    private static Dictionary<string, string> ReadProviderStatuses(
        string repositoryRoot)
    {
        XDocument document = XDocument.Load(
            Path.Combine(repositoryRoot, "eng", "ProviderStatus.props"));
        Dictionary<string, string> statuses = new(StringComparer.Ordinal);
        foreach ((string packageId, string propertyName) in ProviderStatusProperties)
        {
            string value = document.Descendants(propertyName)
                .SingleOrDefault()?
                .Value
                .Trim()
                ?? throw new InvalidOperationException(
                    $"Provider status property {propertyName} is missing.");
            statuses.Add(packageId, value);
        }
        return statuses;
    }

    private static (Uri RepositoryUrl, string Identity)
        ParseRepositoryIdentity(string value)
    {
        string normalized = value.Trim().TrimEnd('/');
        if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri? repositoryUrl)
            || repositoryUrl.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "--repository-url must be an absolute HTTPS URL.");
        }

        normalized = repositoryUrl.AbsoluteUri.TrimEnd('/');
        if (string.Equals(
            normalized,
            DevelopmentRepositoryUrl,
            StringComparison.OrdinalIgnoreCase))
        {
            return (new Uri(DevelopmentRepositoryUrl), "development-candidate");
        }
        if (string.Equals(
            normalized,
            CanonicalRepositoryUrl,
            StringComparison.OrdinalIgnoreCase))
        {
            return (new Uri(CanonicalRepositoryUrl), "canonical-public");
        }
        throw new ArgumentException(
            $"--repository-url must be {DevelopmentRepositoryUrl} or {CanonicalRepositoryUrl}.");
    }

    private static string RequireValue(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        if (index < 0
            || index + 1 >= args.Length
            || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            throw new ArgumentException($"{name} is required.");
        }
        return args[index + 1];
    }

    private static string? OptionalValue(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        if (index < 0)
        {
            return null;
        }
        if (index + 1 >= args.Length
            || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            throw new ArgumentException($"{name} requires a value.");
        }
        return args[index + 1];
    }

    private static List<string> Values(string[] args, string name)
    {
        List<string> values = [];
        for (int index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], name, StringComparison.Ordinal)
                && index + 1 < args.Length)
            {
                values.Add(args[++index]);
            }
        }
        return values;
    }

    private static string RunGit(string root, params string[] arguments)
    {
        ProcessStartInfo start = new("git")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(root);
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("Unable to start Git.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Git failed: {error.Trim()}");
        }
        return output;
    }

    private static string HashFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static Guid DeterministicGuid(string value)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value))[..16];
        return new Guid(bytes);
    }

    private static string Sanitize(string value) =>
        Regex.Replace(value, "[^A-Za-z0-9.-]", "-");

    private static void WriteJson(string path, JsonObject document)
    {
        string json = document
            .ToJsonString(JsonOptions)
            .Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex RevisionPattern();

    [GeneratedRegex(
        "^\\d+\\.\\d+\\.\\d+(?:-[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersionPattern();
}
