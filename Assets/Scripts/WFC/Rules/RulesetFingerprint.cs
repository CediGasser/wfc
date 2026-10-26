using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Wfc
{
    /// <summary>
    /// Canonical text form of a ruleset: variants, connections and adjacency described by stable tile ids instead of
    /// indices, in sorted order. Two rulesets with the same fingerprint behave the same, even if their tiles or
    /// connections are numbered differently. Used to check that refactorings and data migrations don't change rules.
    /// </summary>
    public static class RulesetFingerprint
    {
        public static string Describe(RulesetData data, bool includeDiagnostics = false)
        {
            var lines = new List<string>();
            for (int v = 0; v < data.VariantCount; v++)
            {
                lines.Add($"variant {Variant(data, v)} weight {Format(data.variantWeight[v])}");
            }

            foreach (Connection connection in data.connections)
            {
                lines.Add($"connection {connection.key} seen {connection.observations} weight {Format(connection.weight)} " +
                    $"overridden {connection.overridden} members {connection.memberCount}");
            }

            for (int v = 0; v < data.VariantCount; v++)
            {
                for (int d = 0; d < Directions.Count; d++)
                {
                    var (start, end) = data.GetNeighbourRange(v, (Direction)d);
                    IEnumerable<string> neighbours = Enumerable.Range(start, end - start)
                        .Select(i => $"{Variant(data, data.adjacencyVariant[i])}:{Format(data.GetEntryWeight(i))}")
                        .OrderBy(s => s, System.StringComparer.Ordinal);
                    lines.Add($"adjacent {Variant(data, v)} {((Direction)d).ToShortString()} [{string.Join(" ", neighbours)}]");
                }
            }

            if (includeDiagnostics)
            {
                foreach (RuleDiagnostic diagnostic in data.diagnostics)
                {
                    string tile = diagnostic.tile >= 0 && diagnostic.tile < data.TileCount ? data.tileIds[diagnostic.tile] : "-";
                    lines.Add($"diagnostic {diagnostic.code} {diagnostic.severity} {tile}");
                }
            }

            lines.Sort(System.StringComparer.Ordinal);
            var builder = new StringBuilder();
            foreach (string line in lines)
            {
                builder.AppendLine(line);
            }
            return builder.ToString();
        }

        private static string Variant(RulesetData data, int variant)
        {
            return $"{data.tileIds[data.variantTile[variant]]}#{data.variantOrientation[variant]}";
        }

        private static string Format(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
