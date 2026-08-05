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
    private sealed record MetricNode(
        string Project,
        string Assembly,
        string Namespace,
        string Type,
        string Member,
        string Kind,
        string File,
        int StartLine,
        long SourceLines,
        long ExecutableLines,
        int CyclomaticComplexity,
        int MaintainabilityIndex,
        int ClassCoupling,
        IReadOnlyList<string> InternalCoupledTypes,
        IReadOnlyList<string> ExternalCoupledTypes)
    {
        public string Key => $"{Assembly}|{Type}|{Member}|{StartLine.ToString(CultureInfo.InvariantCulture)}";
    }
    private sealed record ProjectAnalysis(
        ProjectPolicy Policy,
        IReadOnlyList<MetricNode> Nodes)
    {
        public static ProjectAnalysis Create(
            string root,
            ProjectPolicy policy,
            CodeAnalysisMetricData metricData,
            IReadOnlySet<string> internalAssemblies)
        {
            List<MetricNode> nodes = [];
            Traverse(metricData, policy, root, internalAssemblies, nodes);
            return new ProjectAnalysis(
                policy,
                nodes.OrderBy(node => node.Kind, Ordinal)
                    .ThenBy(node => node.Namespace, Ordinal)
                    .ThenBy(node => node.Type, Ordinal)
                    .ThenBy(node => node.Member, Ordinal)
                    .ThenBy(node => node.StartLine)
                    .ToArray());
        }

        private static void Traverse(
            CodeAnalysisMetricData data,
            ProjectPolicy policy,
            string root,
            IReadOnlySet<string> internalAssemblies,
            ICollection<MetricNode> nodes)
        {
            ISymbol symbol = data.Symbol;
            if (!symbol.IsImplicitlyDeclared)
            {
                Location? location = symbol.Locations.FirstOrDefault(candidate => candidate.IsInSource);
                string file = string.Empty;
                int line = 0;
                if (location is not null)
                {
                    FileLinePositionSpan span = location.GetLineSpan();
                    if (!string.IsNullOrWhiteSpace(span.Path))
                    {
                        file = NormalizeSourcePath(root, span.Path);
                        line = span.StartLinePosition.Line + 1;
                    }
                }

                if (!IsGenerated(file))
                {
                    string namespaceName = symbol.ContainingNamespace is { IsGlobalNamespace: false }
                        ? symbol.ContainingNamespace.ToDisplayString()
                        : string.Empty;
                    string typeName = symbol switch
                    {
                        INamedTypeSymbol namedType => namedType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                        _ => symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? string.Empty
                    };
                    string memberName = symbol.Kind is SymbolKind.Assembly or SymbolKind.Namespace or SymbolKind.NamedType
                        ? string.Empty
                        : symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

                    string[] internalTypes = data.CoupledNamedTypes
                        .Where(type => internalAssemblies.Contains(type.ContainingAssembly.Name))
                        .Select(type => $"{type.ContainingAssembly.Name}:{type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}")
                        .Distinct(Ordinal)
                        .Order(Ordinal)
                        .ToArray();
                    string[] externalTypes = data.CoupledNamedTypes
                        .Where(type => !internalAssemblies.Contains(type.ContainingAssembly.Name))
                        .Select(type => $"{type.ContainingAssembly.Name}:{type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}")
                        .Distinct(Ordinal)
                        .Order(Ordinal)
                        .ToArray();

                    nodes.Add(new MetricNode(
                        policy.Path,
                        policy.Assembly,
                        namespaceName,
                        typeName,
                        memberName,
                        symbol.Kind.ToString(),
                        file,
                        line,
                        data.SourceLines,
                        data.ExecutableLines,
                        data.CyclomaticComplexity,
                        data.MaintainabilityIndex,
                        data.CoupledNamedTypes.Count,
                        internalTypes,
                        externalTypes));
                }
            }

            foreach (CodeAnalysisMetricData child in data.Children)
            {
                Traverse(child, policy, root, internalAssemblies, nodes);
            }
        }

        private static string NormalizeSourcePath(string root, string path)
        {
            string fullPath = Path.GetFullPath(path);
            string relative = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
            return relative.StartsWith("../", StringComparison.Ordinal) ? string.Empty : relative;
        }

        private static bool IsGenerated(string file)
            => string.IsNullOrEmpty(file)
                ? false
                : file.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                  || file.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                  || file.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
                  || file.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)
                  || file.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase);
    }
}
