using System.Collections.Generic;
using System.Linq;

namespace Wfc
{
    /// <summary>
    /// Checks finished rules for problems that make tiles hard or impossible to generate. Works only on the derived
    /// data, so it doesn't influence the rules themselves, and it works the same for every rule source.
    /// </summary>
    public static class RuleAnalysis
    {
        /// <param name="cellVariant">
        /// Variant per sample cell (-1 for empty or ignored cells), used to point diagnostics at the cells of a tile.
        /// If given, only tiles that appear in it are checked; for sources without samples (null), every tile with
        /// variants is.
        /// </param>
        public static List<RuleDiagnostic> Diagnose(RulesetData data, IReadOnlyList<TileSpec> tiles, int[] cellVariant)
        {
            var diagnostics = new List<RuleDiagnostic>();
            bool hasCells = cellVariant != null;
            cellVariant ??= System.Array.Empty<int>();
            for (int t = 0; t < data.TileCount; t++)
            {
                int[] cells = Enumerable.Range(0, cellVariant.Length)
                    .Where(i => cellVariant[i] >= 0 && data.variantTile[cellVariant[i]] == t)
                    .ToArray();
                if (hasCells ? cells.Length == 0 : !data.GetVariants(t).Any())
                {
                    continue;
                }

                bool connected = false;
                bool selfConnected = false;
                int deadFaces = 0;
                foreach (int v in data.GetVariants(t))
                {
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
                        $"Nothing may be placed next to {name}, so it can't be generated.", t, cells));
                    continue;
                }
                if (tiles[t].isInvisible && !selfConnected)
                {
                    diagnostics.Add(new RuleDiagnostic(DiagnosticSeverity.Warning, DiagnosticCode.MissingSelfConnection,
                        $"{name} may never be next to another {name}, so two {name} cells can't be neighbours.", t, cells));
                }
                if (deadFaces != 0)
                {
                    string faces = string.Join(", ", Enumerable.Range(0, Directions.Count)
                        .Where(d => (deadFaces & (1 << d)) != 0)
                        .Select(d => ((Direction)d).ToShortString()));
                    diagnostics.Add(new RuleDiagnostic(DiagnosticSeverity.Warning, DiagnosticCode.DeadEnd,
                        $"{name}: nothing may be placed next to local face(s) {faces} in at least one orientation, so those variants only fit at the edge of the world.", t, cells)
                    {
                        faces = deadFaces,
                    });
                }
            }
            return diagnostics;
        }
    }
}
