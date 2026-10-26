using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Wfc.Authoring
{
    /// <summary>
    /// A component rules come from, such as samples (<see cref="SampleRuleSource"/>) or sockets
    /// (<see cref="SocketRuleSource"/>). The editor derives its rules for the tiles of <see cref="TileSet"/> and bakes
    /// them into <see cref="Ruleset"/>, which generators use. Weight overrides replace the derived weight of single
    /// connections (0 forbids one); they're keyed by <see cref="ConnectionKey"/>, so they survive rebakes.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class RuleSource : MonoBehaviour
    {
        [Tooltip("The baked ruleset asset. Created next to the scene on the first bake if empty.")]
        [SerializeField, FormerlySerializedAs("target")] private Ruleset ruleset;

        [SerializeField] private List<WeightOverride> overrides = new List<WeightOverride>();

        /// <summary>Raised when a source's settings are edited (also by Undo).</summary>
        public static event Action<RuleSource> Changed;

        public Ruleset Ruleset
        {
            get => ruleset;
            set => ruleset = value;
        }

        /// <summary>The tiles the rules are about; their slots are the tile indices of the derived rules.</summary>
        public abstract TileSet TileSet { get; }

        public IReadOnlyList<WeightOverride> Overrides => overrides;

        /// <summary>True if the derived connections count how often they were observed, which weight calibration compares against.</summary>
        public virtual bool HasObservations => false;

        /// <summary>The rules in their core form, including the overrides, ready to be derived for the tile set's tiles.</summary>
        public abstract IRuleSource CreateRules();

        public bool TryGetOverride(ConnectionKey key, out float weight)
        {
            ConnectionKey undirected = key.Undirected;
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

        public void SetOverride(ConnectionKey key, float weight)
        {
            RemoveOverride(key);
            overrides.Add(new WeightOverride(key.Undirected, Mathf.Max(0f, weight)));
            NotifyChanged();
        }

        public void RemoveOverride(ConnectionKey key)
        {
            ConnectionKey undirected = key.Undirected;
            if (overrides.RemoveAll(o => o.key.Undirected.Equals(undirected)) > 0)
            {
                NotifyChanged();
            }
        }

        protected void NotifyChanged() => Changed?.Invoke(this);

        protected virtual void OnValidate() => NotifyChanged();
    }
}
