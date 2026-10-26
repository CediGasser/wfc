using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>The rules of a <see cref="RuleSource"/> as derived right now, with what the editor needs to show them.</summary>
    public sealed class RuleSourceAnalysis
    {
        public RuleSource source;
        public TileSet tileSet;
        /// <summary>One spec per tile set slot, in slot order.</summary>
        public List<TileSpec> specs;
        public DerivedRules result;
        /// <summary>Size of the sample the cell indices in <see cref="result"/> refer to; zero for sources without a sample.</summary>
        public int3 sampleSize;

        public RulesetData Data => result.data;

        public string InputHash => result.inputHash;

        public bool IsBakedAssetUpToDate => source.Ruleset != null && source.Ruleset.SourceHash == InputHash;

        /// <summary>The sample chunk, for sources that learn from one.</summary>
        public Chunk Chunk => source is SampleRuleSource sample ? sample.Chunk : null;

        /// <summary>The definition of a tile index, or null.</summary>
        public TileDefinition GetDefinition(int tile) => tileSet != null ? tileSet[tile] : null;

        /// <summary>The sample cells whose pairs produced a connection.</summary>
        public IEnumerable<int> GetConnectionCells(int connection)
        {
            return result.pairs.Where(p => p.connection == connection).SelectMany(p => new[] { p.cellA, p.cellB });
        }

        /// <summary>True if the cell indices of the analysis still match the chunk, i.e. its size hasn't changed since.</summary>
        public bool MatchesChunk(Chunk chunk) => chunk != null && chunk.Size.Equals(sampleSize);
    }
}
