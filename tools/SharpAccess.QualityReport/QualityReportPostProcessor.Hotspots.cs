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
    private static List<HotspotCandidate> BuildHotspots(JsonArray members, QualityThresholds thresholds)
    {
        List<HotspotCandidate> hotspots = [];
        foreach (JsonNode? node in members)
        {
            if (node is not JsonObject member)
            {
                continue;
            }

            double? crapScore = GetNullableDouble(member, "crapScore");
            int complexity = GetInt(member, "cyclomaticComplexity");
            int maintainability = GetInt(member, "maintainabilityIndex");
            int coupling = GetInt(member, "classCoupling");
            JsonObject coverage = member["coverage"] as JsonObject ?? new JsonObject();
            int uncoveredBranches = GetInt(coverage, "uncoveredBranches");
            double? lineCoverage = GetNullableDouble(coverage, "lineCoverage");

            List<string> reasons = [];
            if (crapScore >= thresholds.Crap.Warning)
            {
                reasons.Add("HighCrapScore");
            }
            if (complexity >= thresholds.Complexity.Warning)
            {
                reasons.Add("ExcessiveComplexity");
            }
            if (maintainability < thresholds.Maintainability.Warning)
            {
                reasons.Add("LowMaintainability");
            }
            if (coupling >= thresholds.Coupling.Warning)
            {
                reasons.Add("HighCoupling");
            }
            if (uncoveredBranches > 0)
            {
                reasons.Add("BranchCoverageGap");
            }
            if (lineCoverage == 0 && complexity >= 5)
            {
                reasons.Add("UncoveredComplexMethod");
            }

            string type = GetString(member, "type");
            string memberName = GetString(member, "member");
            if (lineCoverage == 0 && IsSecuritySensitive(type, memberName))
            {
                reasons.Add("SecuritySensitiveUncovered");
            }

            if (reasons.Count == 0)
            {
                continue;
            }

            string classification = IsCritical(
                crapScore,
                complexity,
                maintainability,
                coupling,
                thresholds)
                ? "Critical"
                : "Warning";
            hotspots.Add(new HotspotCandidate(
                GetString(member, "project"),
                type,
                memberName,
                GetString(member, "file"),
                GetInt(member, "startLine"),
                crapScore,
                uncoveredBranches,
                complexity,
                maintainability,
                coupling,
                classification,
                reasons.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()));
        }

        return hotspots
            .OrderByDescending(static row => row.CrapScore ?? double.MinValue)
            .ThenByDescending(static row => row.UncoveredBranches)
            .ThenByDescending(static row => row.CyclomaticComplexity)
            .ThenBy(static row => row.MaintainabilityIndex)
            .ThenByDescending(static row => row.ClassCoupling)
            .ThenBy(static row => row.Project, StringComparer.Ordinal)
            .ThenBy(static row => row.Type, StringComparer.Ordinal)
            .ThenBy(static row => row.Member, StringComparer.Ordinal)
            .ToList();
    }
    private static bool IsCritical(
        double? crapScore,
        int complexity,
        int maintainability,
        int coupling,
        QualityThresholds thresholds)
        => crapScore >= thresholds.Crap.Critical
           || complexity >= thresholds.Complexity.Critical
           || maintainability < thresholds.Maintainability.Critical
           || coupling >= thresholds.Coupling.Critical;
    private static bool IsSecuritySensitive(string type, string member)
    {
        string value = $"{type}.{member}";
        return SecuritySensitiveTerms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
    private static JsonObject CountByClassification(IEnumerable<HotspotCandidate> hotspots)
    {
        JsonObject result = new()
        {
            ["critical"] = 0,
            ["warning"] = 0,
            ["information"] = 0
        };
        foreach (HotspotCandidate hotspot in hotspots)
        {
            string key = hotspot.Classification.ToLowerInvariant();
            result[key] = GetInt(result, key) + 1;
        }
        return result;
    }
    private sealed record HotspotCandidate(
        string Project,
        string Type,
        string Member,
        string File,
        int StartLine,
        double? CrapScore,
        int UncoveredBranches,
        int CyclomaticComplexity,
        int MaintainabilityIndex,
        int ClassCoupling,
        string Classification,
        IReadOnlyList<string> Reasons)
    {
        public JsonObject ToJson()
        {
            JsonArray reasons = [];
            foreach (string reason in Reasons)
            {
                reasons.Add(reason);
            }
            JsonObject result = new()
            {
                ["project"] = Project,
                ["type"] = Type,
                ["member"] = Member,
                ["file"] = File,
                ["startLine"] = StartLine,
                ["uncoveredBranches"] = UncoveredBranches,
                ["cyclomaticComplexity"] = CyclomaticComplexity,
                ["maintainabilityIndex"] = MaintainabilityIndex,
                ["classCoupling"] = ClassCoupling,
                ["classification"] = Classification,
                ["reasons"] = reasons
            };
            result["crapScore"] = CrapScore.HasValue ? JsonValue.Create(CrapScore.Value) : null;
            return result;
        }
    }
}
