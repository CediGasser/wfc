using System.Collections.Generic;
using Unity.Mathematics;

namespace Wfc
{
    /// <summary>
    /// The variants of a list of tiles. A tile may appear in every orientation of its allowed set, and orientations
    /// that look identical form one variant: variants are the cosets g·S of the effective symmetry S (geometric
    /// symmetry ∩ allowed orientations), each represented by its smallest orientation index. Tiles that don't take
    /// part, have no id, share their id with an earlier tile or whose allowed set isn't a group get no variants.
    /// </summary>
    public sealed class VariantTable
    {
        public const int None = -1;

        private readonly bool[] valid;
        private readonly OrientationSet[] allowed;
        private readonly OrientationSet[] effective;
        // Per tile * Orientation.Count + orientation index: the coset's representative orientation and its variant,
        // or None if the orientation isn't allowed.
        private readonly int[] representative;
        private readonly int[] variantOf;
        private readonly List<int> variantTile = new List<int>();
        private readonly List<byte> variantOrientation = new List<byte>();

        /// <param name="tiles">The tiles; their index is the tile index everywhere.</param>
        /// <param name="takesPart">Which tiles get variants; null for every tile.</param>
        /// <param name="extraSymmetry">Per tile, a group that further restricts the effective symmetry; null for none.</param>
        /// <param name="diagnostics">Receives problems with the tiles, if given.</param>
        public VariantTable(IReadOnlyList<TileSpec> tiles, IReadOnlyList<bool> takesPart = null,
            IReadOnlyList<OrientationSet> extraSymmetry = null, List<RuleDiagnostic> diagnostics = null)
        {
            TileCount = tiles.Count;
            valid = new bool[TileCount];
            allowed = new OrientationSet[TileCount];
            effective = new OrientationSet[TileCount];
            representative = new int[TileCount * Orientation.Count];
            variantOf = new int[TileCount * Orientation.Count];
            System.Array.Fill(representative, None);
            System.Array.Fill(variantOf, None);

            var ids = new Dictionary<string, int>();
            for (int t = 0; t < TileCount; t++)
            {
                TileSpec spec = tiles[t];
                if (!spec.IsDefined || (takesPart != null && !takesPart[t]))
                {
                    continue;
                }
                if (!ids.TryAdd(spec.id, t))
                {
                    diagnostics?.Add(new RuleDiagnostic(DiagnosticSeverity.Error, DiagnosticCode.DuplicateTile,
                        $"{spec.name} has the same id as {tiles[ids[spec.id]].name} ({spec.id}). It is ignored.", t));
                    continue;
                }
                if (!spec.allowed.IsGroup)
                {
                    diagnostics?.Add(new RuleDiagnostic(DiagnosticSeverity.Error, DiagnosticCode.AllowedSetNotAGroup,
                        $"{spec.name}: the allowed orientations must contain the identity and every combination of its members. The tile is ignored.", t));
                    continue;
                }

                valid[t] = true;
                allowed[t] = spec.allowed;
                OrientationSet symmetry = spec.symmetry.With(Orientation.Identity).Closure().Intersect(spec.allowed);
                if (extraSymmetry != null)
                {
                    symmetry = symmetry.Intersect(extraSymmetry[t].With(Orientation.Identity).Closure());
                }
                effective[t] = symmetry;
                AddVariants(t);
            }
        }

        public int TileCount { get; }

        public int VariantCount => variantTile.Count;

        /// <summary>Tile index per variant.</summary>
        public IReadOnlyList<int> VariantTile => variantTile;

        /// <summary>Representative orientation index per variant.</summary>
        public IReadOnlyList<byte> VariantOrientation => variantOrientation;

        /// <summary>True if the tile has variants.</summary>
        public bool IsValid(int tile) => valid[tile];

        public OrientationSet Allowed(int tile) => allowed[tile];

        /// <summary>The symmetry whose cosets are the tile's variants.</summary>
        public OrientationSet Effective(int tile) => effective[tile];

        /// <summary>The representative of the coset <paramref name="orientation"/> belongs to, or None if it isn't allowed.</summary>
        public int Representative(int tile, Orientation orientation) => representative[tile * Orientation.Count + orientation.Index];

        /// <summary>The variant that shows the tile in <paramref name="orientation"/> (any orientation of its coset), or None.</summary>
        public int VariantOf(int tile, Orientation orientation) => variantOf[tile * Orientation.Count + orientation.Index];

        /// <summary>
        /// The base weight of every tile spread across its variants, so rotatable tiles aren't more common just because
        /// they rotate.
        /// </summary>
        public float[] ComputeVariantWeights(IReadOnlyList<TileSpec> tiles)
        {
            var counts = new int[TileCount];
            foreach (int tile in variantTile)
            {
                counts[tile]++;
            }
            var weights = new float[VariantCount];
            for (int v = 0; v < weights.Length; v++)
            {
                weights[v] = tiles[variantTile[v]].baseWeight / counts[variantTile[v]];
            }
            return weights;
        }

        /// <summary>The variant per tile and orientation (<see cref="RulesetData.orientationVariant"/>).</summary>
        public int[] BuildOrientationVariant() => (int[])variantOf.Clone();

        /// <summary>The representative orientation of every variant of a tile, in ascending order.</summary>
        public static List<Orientation> Representatives(OrientationSet allowed, OrientationSet effective)
        {
            var representatives = new List<Orientation>();
            foreach (Orientation g in allowed)
            {
                if (RepresentativeOf(g, effective) == g.Index)
                {
                    representatives.Add(g);
                }
            }
            return representatives;
        }

        private static int RepresentativeOf(Orientation g, OrientationSet symmetry)
        {
            int rep = g.Index;
            foreach (Orientation s in symmetry)
            {
                rep = math.min(rep, (g * s).Index);
            }
            return rep;
        }

        private void AddVariants(int tile)
        {
            // Orientations are visited in ascending order, so a coset's representative (its smallest member) always
            // comes before the other members.
            foreach (Orientation g in allowed[tile])
            {
                int slot = tile * Orientation.Count + g.Index;
                int rep = RepresentativeOf(g, effective[tile]);
                representative[slot] = rep;
                if (rep == g.Index)
                {
                    variantOf[slot] = variantTile.Count;
                    variantTile.Add(tile);
                    variantOrientation.Add(g.Index);
                }
                else
                {
                    variantOf[slot] = variantOf[tile * Orientation.Count + rep];
                }
            }
        }
    }
}
