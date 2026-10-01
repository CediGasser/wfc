using System.Collections.Generic;
using UnityEngine;

namespace Wfc.Authoring
{
    /// <summary>
    /// Root of a set of ruleset samples. Every <see cref="TileInstance"/> below it is part of the samples, and
    /// every pair of tiles touching face to face becomes a connection. Its local space is the grid: cell (x, y, z)
    /// is centred at local position (x, y, z). Empty cells carry no information, so keep separate samples at
    /// least one empty cell apart.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("WFC/Ruleset Authoring")]
    public sealed class RulesetAuthoring : MonoBehaviour
    {
        [Tooltip("The baked ruleset asset. Created next to the scene on the first bake if empty.")]
        [SerializeField] private Ruleset target;

        [Tooltip("Presence: every connection that appears has weight 1. Frequency: weight is how often it appears.")]
        [SerializeField] private WeightSource weightSource = WeightSource.Presence;

        [Tooltip("Tile used by 'Fill empty cells', usually air.")]
        [SerializeField] private TileDefinition fillTile;

        [SerializeField] private List<WeightOverride> overrides = new List<WeightOverride>();

        [SerializeField] private bool showCells = true;
        [SerializeField] private bool showConnections = true;

        public Ruleset Target
        {
            get => target;
            set => target = value;
        }

        public WeightSource WeightSource => weightSource;

        public TileDefinition FillTile => fillTile;

        public IReadOnlyList<WeightOverride> Overrides => overrides;

        public bool ShowCells => showCells;

        public bool ShowConnections => showConnections;

        public bool TryGetOverride(OrbitKey key, out float weight)
        {
            OrbitKey undirected = key.Undirected;
            foreach (WeightOverride weightOverride in overrides)
            {
                if (weightOverride.key.Undirected.Equals(undirected))
                {
                    weight = weightOverride.weight;
                    return true;
                }
            }
            weight = 0f;
            return false;
        }

        public void SetOverride(OrbitKey key, float weight)
        {
            RemoveOverride(key);
            overrides.Add(new WeightOverride(key.Undirected, Mathf.Max(0f, weight)));
        }

        public void RemoveOverride(OrbitKey key)
        {
            OrbitKey undirected = key.Undirected;
            overrides.RemoveAll(o => o.key.Undirected.Equals(undirected));
        }
    }
}
