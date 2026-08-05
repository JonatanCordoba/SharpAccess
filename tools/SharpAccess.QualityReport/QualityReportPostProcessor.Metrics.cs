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
    private static JsonObject CountByStringProperty(JsonArray values, string property)
    {
        SortedDictionary<string, int> counts = new(StringComparer.Ordinal);
        foreach (JsonNode? node in values)
        {
            if (node is not JsonObject item)
            {
                continue;
            }
            string value = GetString(item, property);
            if (string.IsNullOrWhiteSpace(value))
            {
                value = "Unspecified";
            }
            counts[value] = counts.GetValueOrDefault(value) + 1;
        }

        JsonObject result = new();
        foreach ((string name, int count) in counts)
        {
            result[name] = count;
        }
        return result;
    }
    private static void EnrichMaintainabilitySummary(
        JsonObject summary,
        JsonArray members,
        QualityThresholds thresholds)
    {
        double[] values = members
            .OfType<JsonObject>()
            .Select(member => (double)GetInt(member, "maintainabilityIndex"))
            .Order()
            .ToArray();
        if (values.Length == 0)
        {
            return;
        }

        JsonObject maintainability;
        if (summary["maintainabilityIndex"] is JsonObject existingMaintainability)
        {
            maintainability = existingMaintainability;
        }
        else
        {
            maintainability = new JsonObject();
            summary["maintainabilityIndex"] = maintainability;
        }
        maintainability["percentile05"] = Percentile(values, 0.05);
        maintainability["percentile10"] = Percentile(values, 0.10);
        maintainability["warningCount"] = values.Count(value => value < thresholds.Maintainability.Warning);
        maintainability["criticalCount"] = values.Count(value => value < thresholds.Maintainability.Critical);
    }
    private static double Percentile(double[] sorted, double percentile)
    {
        if (sorted.Length == 1)
        {
            return Math.Round(sorted[0], 2);
        }
        double position = (sorted.Length - 1) * percentile;
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        double value = lower == upper
            ? sorted[lower]
            : sorted[lower] + ((sorted[upper] - sorted[lower]) * (position - lower));
        return Math.Round(value, 2);
    }
    private static JsonObject BuildCoverageScope(JsonArray projects)
    {
        JsonArray included = [];
        JsonArray missing = [];
        foreach (JsonObject project in projects.OfType<JsonObject>().OrderBy(project => GetString(project, "assembly"), StringComparer.Ordinal))
        {
            string assembly = GetString(project, "assembly");
            JsonObject coverage = project["coverage"] as JsonObject ?? new JsonObject();
            if (GetInt(coverage, "totalLines") > 0)
            {
                included.Add(assembly);
            }
            else
            {
                missing.Add(assembly);
            }
        }
        return new JsonObject
        {
            ["complete"] = missing.Count == 0,
            ["includedAssemblies"] = included,
            ["missingAssemblies"] = missing
        };
    }
    private static JsonArray EnrichTypeSourceFiles(JsonArray types, JsonArray members)
    {
        Dictionary<string, SortedSet<string>> filesByType = new(StringComparer.Ordinal);
        foreach (JsonObject member in members.OfType<JsonObject>())
        {
            string project = GetString(member, "project");
            string type = GetString(member, "type");
            string file = GetString(member, "file");
            if (string.IsNullOrWhiteSpace(file))
            {
                continue;
            }
            string key = $"{project}|{type}";
            if (!filesByType.TryGetValue(key, out SortedSet<string>? files))
            {
                files = new SortedSet<string>(StringComparer.Ordinal);
                filesByType[key] = files;
            }
            files.Add(file);
        }

        JsonArray partialTypes = [];
        foreach (JsonObject type in types.OfType<JsonObject>())
        {
            string project = GetString(type, "project");
            string typeName = GetString(type, "type");
            string key = $"{project}|{typeName}";
            string[] files = filesByType.GetValueOrDefault(key)?.ToArray() ?? [];
            JsonArray sourceFiles = [];
            foreach (string file in files)
            {
                sourceFiles.Add(file);
            }
            type["sourceFiles"] = sourceFiles;
            if (files.Length > 1)
            {
                JsonArray partialSourceFiles = [];
                foreach (string file in files)
                {
                    partialSourceFiles.Add(file);
                }
                partialTypes.Add(new JsonObject
                {
                    ["project"] = project,
                    ["type"] = typeName,
                    ["sourceFiles"] = partialSourceFiles
                });
            }
        }
        return partialTypes;
    }
}
