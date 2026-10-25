using System.Collections.Generic;
using System.Linq;

namespace Wfc
{
    /// <summary>
    /// Checks finished rules for problems that make tiles hard or impossible to generate. Works only on the
    /// derivation output, so it doesn't influence the rules themselves.
    /// </summary>
    public static class RuleAnalysis
    {
        public static List<RuleDiagnostic> Diagnose(DerivationInput input, DerivationResult result)
        {
            RulesetData data = result.data;
            var diagnostics = new List<RuleDiagnostic>();
            for (int t = 0; t < data.TileCount; t++)
            {
                // Instances the derivation skipped have no variant and don't count as samples.
                int[] sources = Enumerable.Range(0, result.instanceVariant.Length)
                    .Where(i => result.instanceVariant[i] >= 0 && data.variantTile[result.instanceVariant[i]] == t)
                    .ToArray();
                if (sources.Length == 0)
                {
                    continue;
                }

                bool connected = false;
                bool selfConnected = false;
                int deadFaces = 0;
                for (int v = 0; v < data.VariantCount; v++)
                {
                    if (data.variantTile[v] != t)
                    {
                        continue;
                    }
                    Orientation inverse = data.GetVariantOrientation(v).Inverse;
                    for (int d = 0; d < Directions.Count; d++)
                    {
                        var (start, end) = data.GetNeighbourRange(v, (Direction)d);
                        if (start == end)
                        {
                            deadFaces |= 1 << (int)inverse.Apply((Direction)d);
                        }
                        for (int i = start; i < end; i++)
                        {
                            connected = true;
                            selfConnected |= data.variantTile[data.adjacencyVariant[i]] == t;
                        }
                    }
                }

                string name = data.tileNames[t];
                if (!connected)
                {
                    diagnostics.Add(new RuleDiagnostic(DiagnosticSeverity.Warning, DiagnosticCode.TileNeverConnected,
                        $"{name} never touches another tile in the samples, so it can't be generated.", t, sources));
                    continue;
                }
                if (input.tiles[t].isEmpty && !selfConnected)
                {
                    diagnostics.Add(new RuleDiagnostic(DiagnosticSeverity.Warning, DiagnosticCode.MissingSelfConnection,
                        $"{name} never touches another {name} in the samples, so two {name} cells can't be neighbours.", t, sources));
                }
                if (deadFaces != 0)
                {
                    string faces = string.Join(", ", Enumerable.Range(0, Directions.Count)
                        .Where(d => (deadFaces & (1 << d)) != 0)
                        .Select(d => ((Direction)d).ToShortString()));
                    diagnostics.Add(new RuleDiagnostic(DiagnosticSeverity.Warning, DiagnosticCode.DeadEnd,
                        $"{name}: nothing was learned next to local face(s) {faces} in at least one orientation, so those variants only fit at the edge of the world.", t, sources));
                }
            }
            return diagnostics;
        }
    }
}
