using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeMetrics;
using Microsoft.CodeAnalysis.MSBuild;

namespace SharpAccess.QualityReport;

internal static partial class Program
{
    private static readonly StringComparer Ordinal = StringComparer.Ordinal;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static SortedDictionary<string, string> GetToolVersions(Arguments options)
    {
        return new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["dotnetRuntime"] = Environment.Version.ToString(),
            ["microsoftBuildLocator"] = typeof(MSBuildLocator).Assembly.GetName().Version?.ToString() ?? "unknown",
            ["microsoftCodeAnalysis"] = typeof(Compilation).Assembly.GetName().Version?.ToString() ?? "unknown",
            ["microsoftCodeAnalysisAnalyzerUtilities"] = typeof(CodeAnalysisMetricData).Assembly.GetName().Version?.ToString() ?? "unknown",
            ["reportGenerator"] = options.ReportGeneratorVersion,
            ["sharpAccessQualityReport"] = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown"
        };
    }
    private static void RequireFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Required quality-report input is missing: {path}", path);
        }
    }
    private static string RunGit(string root, params string[] arguments)
    {
        ProcessStartInfo startInfo = new("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start Git.");
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Git failed: {standardError.Trim()}");
        }

        return standardOutput.Trim();
    }
    private static string NormalizeRelative(string root, string path)
    {
        string fullPath = Path.GetFullPath(path);
        string relative = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        if (relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            throw new InvalidOperationException($"Path is outside the repository: {path}");
        }

        return relative;
    }
    private static void WriteUtf8(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));
    }
    private static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
