using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace SharpAccess.QualityReport;

internal static partial class QualityReportPostProcessor
{
    private static readonly string[] SecuritySensitiveTerms =
    [
        "AccessToken",
        "Auth",
        "Certificate",
        "EmailVerification",
        "Login",
        "OAuth",
        "Password",
        "Refresh",
        "Security",
        "Token"
    ];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("Arguments must use '--name value' pairs.");
            }
            values[args[index][2..]] = args[index + 1];
        }
        return values;
    }
    private static string GetRequired(IReadOnlyDictionary<string, string> arguments, string name)
        => arguments.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing required argument --{name}.");
    private static void RequireFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Required quality-report output is missing: {path}", path);
        }
    }
    private static JsonObject ParseObject(string path)
        => JsonNode.Parse(File.ReadAllText(path)) as JsonObject
           ?? throw new InvalidOperationException($"JSON root must be an object: {path}");
    private static void RefreshManifest(string repositoryRoot, string outputDirectory, string manifestPath)
    {
        JsonObject manifest = ParseObject(manifestPath);
        manifest["metricSchemaVersion"] = 2;
        JsonArray outputs = [];
        foreach (string path in Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories)
                     .Where(path => !string.Equals(path, manifestPath, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(path => NormalizeRelative(repositoryRoot, path), StringComparer.Ordinal))
        {
            outputs.Add(new JsonObject
            {
                ["path"] = NormalizeRelative(repositoryRoot, path),
                ["sha256"] = Sha256(path)
            });
        }
        manifest["outputs"] = outputs;
        WriteJson(manifestPath, manifest);
    }
    private static string NormalizeRelative(string root, string path)
    {
        string relative = Path.GetRelativePath(root, Path.GetFullPath(path)).Replace('\\', '/');
        if (relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            throw new InvalidOperationException($"Path is outside the repository: {path}");
        }
        return relative;
    }
    private static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
    private static void WriteJson(string path, JsonObject value)
        => WriteUtf8(path, value.ToJsonString(JsonOptions) + "\n");
    private static void WriteUtf8(string path, string content)
        => File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));
    private static int GetInt(JsonObject value, string property)
        => value[property] is JsonNode node ? node.GetValue<int>() : 0;
    private static bool GetBool(JsonObject value, string property)
        => value[property] is JsonNode node && node.GetValue<bool>();
    private static double? GetNullableDouble(JsonObject value, string property)
        => value[property] is JsonNode node ? node.GetValue<double>() : null;
    private static string GetString(JsonObject value, string property)
        => value[property]?.GetValue<string>() ?? string.Empty;
}
