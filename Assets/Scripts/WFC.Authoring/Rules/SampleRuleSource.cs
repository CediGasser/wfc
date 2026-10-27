using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Wfc.Authoring
{
    /// <summary>
    /// Rules learned from the sample in this GameObject's <see cref="Authoring.Chunk"/>: every pair of tiles touching face
    /// to face is a connection, expanded by every orientation both tiles allow. Empty cells count as
    /// <see cref="EmptyCellTile"/>, usually air; without one they carry no information, so separate samples can share
    /// a chunk with a gap between them. Cells outside the chunk never carry information.
    /// </summary>
    [RequireComponent(typeof(Chunk))]
    [AddComponentMenu("WFC/Sample Rule Source")]
    public sealed class SampleRuleSource : RuleSource
    {
        [Tooltip("Presence: every connection that appears has weight 1. Frequency: weight is how often it appears. Overrides replace either; 0 forbids a connection.")]
        [SerializeField, FormerlySerializedAs("weightSource")] private SampleWeighting weighting = SampleWeighting.Frequency;

        [Tooltip("The tile empty cells count as, usually air. None: empty cells carry no information, e.g. gaps between separate samples.")]
        [SerializeField] private TileDefinition emptyCellTile;

        private Chunk chunk;

        public Chunk Chunk => chunk != null ? chunk : chunk = GetComponent<Chunk>();

        public override TileSet TileSet => Chunk != null ? Chunk.TileSet : null;

        public SampleWeighting Weighting
        {
            get => weighting;
            set
            {
                weighting = value;
                NotifyChanged();
            }
        }

        public TileDefinition EmptyCellTile
        {
            get => emptyCellTile;
            set
            {
                emptyCellTile = value;
                NotifyChanged();
            }
        }

        public override bool HasObservations => true;

        public override IRuleSource CreateRules() => CreateRules(Overrides);

        /// <summary>The rules with other overrides, e.g. for weight calibration.</summary>
        public SampleRules CreateRules(IEnumerable<WeightOverride> withOverrides)
        {
            TileSet set = TileSet;
            // A tile outside the set is reported by the derivation; Empty would silently mean "no information".
            int empty = emptyCellTile == null ? ChunkData.Empty : set != null && set.Contains(emptyCellTile) ? set.IndexOf(emptyCellTile) : set != null ? set.Count : 0;
            var rules = new SampleRules
            {
                sample = Chunk != null ? Chunk.Cells : new ChunkData(),
                emptyCellTile = empty,
                weighting = weighting,
            };
            rules.overrides.AddRange(withOverrides);
            return rules;
        }
    }
}
