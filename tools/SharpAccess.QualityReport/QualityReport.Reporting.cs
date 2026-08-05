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
    private sealed record StatisticalSummary(
        double? Median,
        double? Percentile95,
        double? Maximum,
        double? Minimum);
    private sealed record ProjectRow(
        string Project,
        string Assembly,
        CoverageSummary Coverage,
        int CyclomaticComplexity,
        int MaintainabilityIndex,
        int ClassCoupling,
        int AfferentCoupling,
        int EfferentCoupling,
        double? Instability,
        int ExternalDependencies);
    private sealed record NamespaceRow(
        string Project,
        string Namespace,
        CoverageSummary Coverage,
        int CyclomaticComplexity,
        int MaintainabilityIndex,
        int ClassCoupling,
        int AfferentCoupling,
        int EfferentCoupling,
        double? Instability,
        int ExternalDependencies);
    private sealed record TypeRow(
        string Project,
        string Namespace,
        string Type,
        string File,
        int StartLine,
        CoverageSummary Coverage,
        int CyclomaticComplexity,
        int MaintainabilityIndex,
        int ClassCoupling);
    private sealed record MemberRow(
        string Project,
        string Namespace,
        string Type,
        string Member,
        string Kind,
        string File,
        int StartLine,
        CoverageSummary Coverage,
        int CyclomaticComplexity,
        int MaintainabilityIndex,
        int ClassCoupling,
        double? CrapScore,
        string MatchStatus);
    private sealed record DependencyRow(
        string Scope,
        string Unit,
        int AfferentCoupling,
        int EfferentCoupling,
        double? Instability,
        IReadOnlyList<string> InternalDependencies,
        IReadOnlyList<string> ExternalDependencies);
    private sealed record HotspotRow(
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
        string Classification);
    private sealed record RepositorySummary(
        string Revision,
        CoverageSummary Coverage,
        StatisticalSummary CrapScore,
        StatisticalSummary CyclomaticComplexity,
        StatisticalSummary MaintainabilityIndex,
        StatisticalSummary ClassCoupling,
        int HotspotCount);
    private sealed record ReportDataset(
        int SchemaVersion,
        string Repository,
        string Revision,
        string Enforcement,
        RepositorySummary Summary,
        IReadOnlyDictionary<string, string> ToolVersions,
        IReadOnlyList<ProjectRow> Projects,
        IReadOnlyList<NamespaceRow> Namespaces,
        IReadOnlyList<TypeRow> Types,
        IReadOnlyList<MemberRow> Members,
        IReadOnlyList<DependencyRow> Dependencies,
        IReadOnlyList<HotspotRow> Hotspots,
        IReadOnlyList<string> IncludedProjects,
        IReadOnlyList<string> Exclusions,
        IReadOnlyDictionary<string, string> Definitions)
    {
        public static ReportDataset Create(
            Arguments options,
            QualityPolicy policy,
            IReadOnlyList<ProjectAnalysis> analyses,
            CoverageDataset coverage,
            ComplexityDataset complexity,
            IReadOnlyDictionary<string, string> toolVersions)
        {
            MetricNode[] nodes = analyses.SelectMany(analysis => analysis.Nodes).ToArray();
            MetricNode[] memberNodes = nodes
                .Where(node => node.Kind is "Method" or "Property" or "Field" or "Event")
                .ToArray();

            List<MemberRow> members = [];
            foreach (MetricNode node in memberNodes)
            {
                ComplexityMetric? complexityMetric = FindComplexity(node, complexity.Methods);
                CoverageMetric? coverageMetric = FindCoverage(node, coverage.Methods, complexityMetric);
                CoverageSummary coverageSummary = coverageMetric is null
                    ? EmptyCoverage()
                    : CreateCoverage(
                        coverageMetric.CoveredLines,
                        coverageMetric.TotalLines,
                        coverageMetric.CoveredBranches,
                        coverageMetric.TotalBranches);

                members.Add(new MemberRow(
                    node.Project,
                    node.Namespace,
                    node.Type,
                    node.Member,
                    node.Kind,
                    node.File,
                    node.StartLine,
                    coverageSummary,
                    complexityMetric?.CyclomaticComplexity ?? node.CyclomaticComplexity,
                    node.MaintainabilityIndex,
                    node.ClassCoupling,
                    complexityMetric?.CrapScore,
                    complexityMetric is null ? "RoslynOnly" : coverageMetric is null ? "ComplexityMatched" : "Matched"));
            }

            TypeRow[] types = nodes.Where(node => node.Kind == "NamedType")
                .Select(node =>
                {
                    CoverageMetric[] typeCoverage = coverage.Methods
                        .Where(method => string.Equals(method.Assembly, node.Assembly, StringComparison.Ordinal)
                                         && TypeEquivalent(method.Type, node.Type))
                        .ToArray();
                    return new TypeRow(
                        node.Project,
                        node.Namespace,
                        node.Type,
                        node.File,
                        node.StartLine,
                        AggregateCoverage(typeCoverage),
                        node.CyclomaticComplexity,
                        node.MaintainabilityIndex,
                        node.ClassCoupling);
                })
                .OrderBy(row => row.Project, Ordinal)
                .ThenBy(row => row.Namespace, Ordinal)
                .ThenBy(row => row.Type, Ordinal)
                .ToArray();

            NamespaceRow[] namespaces = analyses
                .SelectMany(analysis => analysis.Nodes
                    .Where(node => node.Kind == "Namespace" && !string.IsNullOrWhiteSpace(node.Namespace))
                    .Select(node => CreateNamespaceRow(node, analyses, coverage)))
                .GroupBy(row => $"{row.Project}|{row.Namespace}", StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(row => row.Project, Ordinal)
                .ThenBy(row => row.Namespace, Ordinal)
                .ToArray();

            ProjectRow[] projects = analyses
                .Select(analysis => CreateProjectRow(analysis, analyses, coverage))
                .OrderBy(row => row.Assembly, Ordinal)
                .ToArray();

            DependencyRow[] dependencies = BuildDependencies(analyses);
            Dictionary<string, DependencyRow> projectDependencies = dependencies
                .Where(row => row.Scope == "Project")
                .ToDictionary(row => row.Unit, StringComparer.Ordinal);
            Dictionary<string, DependencyRow> namespaceDependencies = dependencies
                .Where(row => row.Scope == "Namespace")
                .ToDictionary(row => row.Unit, StringComparer.Ordinal);

            projects = projects.Select(row =>
            {
                DependencyRow? dependency = projectDependencies.GetValueOrDefault(row.Assembly);
                return row with
                {
                    AfferentCoupling = dependency?.AfferentCoupling ?? 0,
                    EfferentCoupling = dependency?.EfferentCoupling ?? 0,
                    Instability = dependency?.Instability,
                    ExternalDependencies = dependency?.ExternalDependencies.Count ?? 0
                };
            }).ToArray();

            namespaces = namespaces.Select(row =>
            {
                string key = $"{AssemblyForProject(policy, row.Project)}:{row.Namespace}";
                DependencyRow? dependency = namespaceDependencies.GetValueOrDefault(key);
                return row with
                {
                    AfferentCoupling = dependency?.AfferentCoupling ?? 0,
                    EfferentCoupling = dependency?.EfferentCoupling ?? 0,
                    Instability = dependency?.Instability,
                    ExternalDependencies = dependency?.ExternalDependencies.Count ?? 0
                };
            }).ToArray();

            HotspotRow[] hotspots = members
                .Where(member => member.CrapScore.HasValue
                                 || member.CyclomaticComplexity >= 15
                                 || member.MaintainabilityIndex < 20
                                 || member.ClassCoupling >= 40
                                 || member.Coverage.UncoveredBranches > 0)
                .Select(member => new HotspotRow(
                    member.Project,
                    member.Type,
                    member.Member,
                    member.File,
                    member.StartLine,
                    member.CrapScore,
                    member.Coverage.UncoveredBranches,
                    member.CyclomaticComplexity,
                    member.MaintainabilityIndex,
                    member.ClassCoupling,
                    Classify(member)))
                .OrderByDescending(row => row.CrapScore ?? double.MinValue)
                .ThenByDescending(row => row.UncoveredBranches)
                .ThenByDescending(row => row.CyclomaticComplexity)
                .ThenBy(row => row.MaintainabilityIndex)
                .ThenByDescending(row => row.ClassCoupling)
                .ThenBy(row => row.Project, Ordinal)
                .ThenBy(row => row.Type, Ordinal)
                .ThenBy(row => row.Member, Ordinal)
                .Take(policy.TopHotspots)
                .ToArray();

            double[] crapValues = members.Where(member => member.CrapScore.HasValue).Select(member => member.CrapScore!.Value).ToArray();
            double[] complexityValues = members.Select(member => (double)member.CyclomaticComplexity).ToArray();
            double[] maintainabilityValues = members.Select(member => (double)member.MaintainabilityIndex).ToArray();
            double[] couplingValues = members.Select(member => (double)member.ClassCoupling).ToArray();

            RepositorySummary summary = new(
                options.Revision,
                CreateCoverage(coverage.CoveredLines, coverage.TotalLines, coverage.CoveredBranches, coverage.TotalBranches),
                Stats(crapValues),
                Stats(complexityValues),
                Stats(maintainabilityValues),
                Stats(couplingValues),
                hotspots.Length);

            SortedDictionary<string, string> definitions = new(StringComparer.Ordinal)
            {
                ["afferentCoupling"] = "Ca: distinct in-repository units that depend on the measured unit.",
                ["branchCoverage"] = "Covered branches divided by total branches from the normalized Cobertura report.",
                ["classCoupling"] = "Distinct referenced named types reported by Microsoft.CodeAnalysis.CodeMetrics.",
                ["crapScore"] = "complexity^2 * (1 - lineCoverage)^3 + complexity; methods without executable coverage lines are excluded.",
                ["cyclomaticComplexity"] = "Source-based Microsoft.CodeAnalysis.CodeMetrics cyclomatic complexity.",
                ["efferentCoupling"] = "Ce: distinct in-repository units on which the measured unit depends.",
                ["instability"] = "Ce / (Ca + Ce) when Ca + Ce is greater than zero.",
                ["lineCoverage"] = "Covered executable lines divided by total executable lines from normalized Cobertura data.",
                ["maintainabilityIndex"] = "Source-based Microsoft.CodeAnalysis.CodeMetrics maintainability index from 0 through 100.",
                ["symbolMatching"] = "Assembly, normalized containing type, method name, and exact/nearest source start line bind Roslyn symbols to existing coverage/CRAP evidence."
            };

            return new ReportDataset(
                policy.SchemaVersion,
                options.RepositoryUrl,
                options.Revision,
                policy.Enforcement,
                summary,
                toolVersions,
                projects,
                namespaces,
                types,
                members.OrderBy(row => row.Project, Ordinal)
                    .ThenBy(row => row.Namespace, Ordinal)
                    .ThenBy(row => row.Type, Ordinal)
                    .ThenBy(row => row.Member, Ordinal)
                    .ThenBy(row => row.StartLine)
                    .ToArray(),
                dependencies,
                hotspots,
                policy.Projects.Select(project => project.Path).Order(Ordinal).ToArray(),
                [
                    "Generated and compiler-generated symbols.",
                    "bin, obj, and artifacts directories.",
                    "Tests, samples, benchmarks, and engineering tools from the primary production scorecard.",
                    "Methods without executable coverage lines from CRAP calculation."
                ],
                definitions);
        }

        private static string AssemblyForProject(QualityPolicy policy, string project)
            => policy.Projects.Single(item => item.Path == project).Assembly;

        private static ProjectRow CreateProjectRow(
            ProjectAnalysis analysis,
            IReadOnlyList<ProjectAnalysis> analyses,
            CoverageDataset coverage)
        {
            MetricNode root = analysis.Nodes.First(node => node.Kind == "Assembly");
            CoverageMetric[] projectCoverage = coverage.Methods
                .Where(method => string.Equals(method.Assembly, analysis.Policy.Assembly, StringComparison.Ordinal))
                .ToArray();
            return new ProjectRow(
                analysis.Policy.Path,
                analysis.Policy.Assembly,
                AggregateCoverage(projectCoverage),
                root.CyclomaticComplexity,
                root.MaintainabilityIndex,
                root.ClassCoupling,
                0,
                0,
                null,
                0);
        }

        private static NamespaceRow CreateNamespaceRow(
            MetricNode node,
            IReadOnlyList<ProjectAnalysis> analyses,
            CoverageDataset coverage)
        {
            CoverageMetric[] namespaceCoverage = coverage.Methods
                .Where(method => string.Equals(method.Assembly, node.Assembly, StringComparison.Ordinal)
                                 && string.Equals(method.Namespace, node.Namespace, StringComparison.Ordinal))
                .ToArray();
            return new NamespaceRow(
                node.Project,
                node.Namespace,
                AggregateCoverage(namespaceCoverage),
                node.CyclomaticComplexity,
                node.MaintainabilityIndex,
                node.ClassCoupling,
                0,
                0,
                null,
                0);
        }

        private static CoverageSummary AggregateCoverage(IEnumerable<CoverageMetric> metrics)
        {
            CoverageMetric[] values = metrics.ToArray();
            return CreateCoverage(
                values.Sum(metric => metric.CoveredLines),
                values.Sum(metric => metric.TotalLines),
                values.Sum(metric => metric.CoveredBranches),
                values.Sum(metric => metric.TotalBranches));
        }

        private static CoverageSummary CreateCoverage(
            int coveredLines,
            int totalLines,
            int coveredBranches,
            int totalBranches)
        {
            return new CoverageSummary(
                coveredLines,
                Math.Max(0, totalLines - coveredLines),
                totalLines,
                totalLines == 0 ? null : Math.Round(100.0 * coveredLines / totalLines, 2),
                coveredBranches,
                Math.Max(0, totalBranches - coveredBranches),
                totalBranches,
                totalBranches == 0 ? null : Math.Round(100.0 * coveredBranches / totalBranches, 2));
        }

        private static CoverageSummary EmptyCoverage() => CreateCoverage(0, 0, 0, 0);

        private static ComplexityMetric? FindComplexity(
            MetricNode node,
            IReadOnlyList<ComplexityMetric> methods)
        {
            string methodName = SimpleMemberName(node.Member);
            ComplexityMetric[] candidates = methods
                .Where(method => string.Equals(method.Assembly, node.Assembly, StringComparison.Ordinal)
                                 && TypeEquivalent(method.Type, node.Type)
                                 && string.Equals(NormalizeMethod(method.Method), NormalizeMethod(methodName), StringComparison.Ordinal))
                .ToArray();
            return candidates
                .OrderBy(method => node.StartLine == 0 ? method.StartLine : Math.Abs(method.StartLine - node.StartLine))
                .ThenBy(method => method.Signature, Ordinal)
                .FirstOrDefault();
        }

        private static CoverageMetric? FindCoverage(
            MetricNode node,
            IReadOnlyList<CoverageMetric> methods,
            ComplexityMetric? complexity)
        {
            string methodName = complexity?.Method ?? SimpleMemberName(node.Member);
            CoverageMetric[] candidates = methods
                .Where(method => string.Equals(method.Assembly, node.Assembly, StringComparison.Ordinal)
                                 && TypeEquivalent(method.Type, node.Type)
                                 && string.Equals(NormalizeMethod(method.Method), NormalizeMethod(methodName), StringComparison.Ordinal))
                .ToArray();
            int referenceLine = complexity?.StartLine ?? node.StartLine;
            return candidates
                .OrderBy(method => referenceLine == 0 ? method.StartLine : Math.Abs(method.StartLine - referenceLine))
                .ThenBy(method => method.Signature, Ordinal)
                .FirstOrDefault();
        }

        private static string SimpleMemberName(string member)
        {
            int parenthesis = member.IndexOf('(');
            string value = parenthesis >= 0 ? member[..parenthesis] : member;
            int dot = value.LastIndexOf('.');
            return dot >= 0 ? value[(dot + 1)..] : value;
        }

        private static string NormalizeMethod(string value)
            => value.Replace("get_", string.Empty, StringComparison.Ordinal)
                .Replace("set_", string.Empty, StringComparison.Ordinal)
                .Trim();

        private static bool TypeEquivalent(string left, string right)
            => string.Equals(NormalizeType(left), NormalizeType(right), StringComparison.Ordinal);

        private static string NormalizeType(string value)
        {
            StringBuilder builder = new();
            int genericDepth = 0;
            foreach (char character in value.Replace('+', '.'))
            {
                if (character == '<') { genericDepth++; continue; }
                if (character == '>') { genericDepth = Math.Max(0, genericDepth - 1); continue; }
                if (genericDepth == 0 && character != '`') { builder.Append(character); }
            }
            return builder.ToString();
        }

        private static StatisticalSummary Stats(IEnumerable<double> source)
        {
            double[] values = source.Order().ToArray();
            if (values.Length == 0) { return new StatisticalSummary(null, null, null, null); }
            return new StatisticalSummary(
                Percentile(values, 0.50),
                Percentile(values, 0.95),
                values[^1],
                values[0]);
        }

        private static double Percentile(double[] sorted, double percentile)
        {
            if (sorted.Length == 1) { return Math.Round(sorted[0], 2); }
            double position = (sorted.Length - 1) * percentile;
            int lower = (int)Math.Floor(position);
            int upper = (int)Math.Ceiling(position);
            double value = lower == upper
                ? sorted[lower]
                : sorted[lower] + ((sorted[upper] - sorted[lower]) * (position - lower));
            return Math.Round(value, 2);
        }

        private static string Classify(MemberRow member)
        {
            if ((member.CrapScore ?? 0) >= 30
                || member.CyclomaticComplexity >= 25
                || member.MaintainabilityIndex < 10
                || member.ClassCoupling >= 95)
            {
                return "Critical";
            }
            if ((member.CrapScore ?? 0) >= 15
                || member.CyclomaticComplexity >= 15
                || member.MaintainabilityIndex < 20
                || member.ClassCoupling >= 40
                || member.Coverage.UncoveredBranches > 0)
            {
                return "Warning";
            }
            return "Information";
        }

        private static DependencyRow[] BuildDependencies(IReadOnlyList<ProjectAnalysis> analyses)
        {
            Dictionary<string, HashSet<string>> projectInternal = new(StringComparer.Ordinal);
            Dictionary<string, HashSet<string>> projectExternal = new(StringComparer.Ordinal);
            Dictionary<string, HashSet<string>> namespaceInternal = new(StringComparer.Ordinal);
            Dictionary<string, HashSet<string>> namespaceExternal = new(StringComparer.Ordinal);

            foreach (ProjectAnalysis analysis in analyses)
            {
                projectInternal[analysis.Policy.Assembly] = new HashSet<string>(StringComparer.Ordinal);
                projectExternal[analysis.Policy.Assembly] = new HashSet<string>(StringComparer.Ordinal);
                foreach (MetricNode type in analysis.Nodes.Where(node => node.Kind == "NamedType"))
                {
                    string namespaceUnit = $"{analysis.Policy.Assembly}:{type.Namespace}";
                    namespaceInternal.TryAdd(namespaceUnit, new HashSet<string>(StringComparer.Ordinal));
                    namespaceExternal.TryAdd(namespaceUnit, new HashSet<string>(StringComparer.Ordinal));

                    foreach (string dependency in type.InternalCoupledTypes)
                    {
                        int separator = dependency.IndexOf(':');
                        string targetAssembly = separator >= 0 ? dependency[..separator] : dependency;
                        if (!string.Equals(targetAssembly, analysis.Policy.Assembly, StringComparison.Ordinal))
                        {
                            projectInternal[analysis.Policy.Assembly].Add(targetAssembly);
                        }

                        string targetType = separator >= 0 ? dependency[(separator + 1)..] : string.Empty;
                        int finalDot = targetType.LastIndexOf('.');
                        string targetNamespace = finalDot >= 0 ? targetType[..finalDot] : string.Empty;
                        string targetUnit = $"{targetAssembly}:{targetNamespace}";
                        if (!string.Equals(targetUnit, namespaceUnit, StringComparison.Ordinal))
                        {
                            namespaceInternal[namespaceUnit].Add(targetUnit);
                        }
                    }

                    foreach (string dependency in type.ExternalCoupledTypes)
                    {
                        projectExternal[analysis.Policy.Assembly].Add(dependency.Split(':', 2)[0]);
                        namespaceExternal[namespaceUnit].Add(dependency.Split(':', 2)[0]);
                    }
                }
            }

            List<DependencyRow> rows = [];
            foreach (string unit in projectInternal.Keys.Order(Ordinal))
            {
                string[] internalDependencies = projectInternal[unit].Order(Ordinal).ToArray();
                int ca = projectInternal.Count(pair => pair.Value.Contains(unit));
                int ce = internalDependencies.Length;
                rows.Add(new DependencyRow(
                    "Project",
                    unit,
                    ca,
                    ce,
                    ca + ce == 0 ? null : Math.Round((double)ce / (ca + ce), 4),
                    internalDependencies,
                    projectExternal[unit].Order(Ordinal).ToArray()));
            }

            foreach (string unit in namespaceInternal.Keys.Order(Ordinal))
            {
                string[] internalDependencies = namespaceInternal[unit].Order(Ordinal).ToArray();
                int ca = namespaceInternal.Count(pair => pair.Value.Contains(unit));
                int ce = internalDependencies.Length;
                rows.Add(new DependencyRow(
                    "Namespace",
                    unit,
                    ca,
                    ce,
                    ca + ce == 0 ? null : Math.Round((double)ce / (ca + ce), 4),
                    internalDependencies,
                    namespaceExternal[unit].Order(Ordinal).ToArray()));
            }

            return rows.OrderBy(row => row.Scope, Ordinal).ThenBy(row => row.Unit, Ordinal).ToArray();
        }
    }
}
