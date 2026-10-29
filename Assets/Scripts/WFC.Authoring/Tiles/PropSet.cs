using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Wfc.Authoring
{
    /// <summary>
    /// Decoration placed on a tile's sockets: transforms in its model whose name starts with <see cref="SocketPrefix"/>.
    /// Each socket independently gets a prop with probability <see cref="FillChance"/>, picked by weight. Props are
    /// visual only; they don't take part in the rules.
    /// </summary>
    [CreateAssetMenu(menuName = "WFC/Prop Set", fileName = "NewPropSet", order = 2)]
    public sealed class PropSet : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("Prop model. Its pivot is the base and it's scaled like a tile (1 unit = one cell).")]
            public GameObject prefab;

            [Min(0f)] public float weight;
        }

        [Tooltip("Transforms in the tile model whose name starts with this are sockets.")]
        [SerializeField] private string socketPrefix = "PropSocket";

        [SerializeField] private List<Entry> props = new List<Entry>();

        [Tooltip("Probability that a socket gets a prop.")]
        [SerializeField, Range(0f, 1f)] private float fillChance = 1f;

        [Tooltip("Uniform scale range applied to each prop.")]
        [SerializeField] private Vector2 scaleRange = new Vector2(1f, 1f);

        [Tooltip("Rotate each prop randomly around its up axis.")]
        [SerializeField] private bool randomYaw = true;

        /// <summary>Raised whenever a prop set is edited, so chunks can refresh their visuals.</summary>
        public static event Action<PropSet> Changed;

        public string SocketPrefix => socketPrefix;

        public bool IsSocket(Transform candidate) =>
            !string.IsNullOrEmpty(socketPrefix) && candidate.name.StartsWith(socketPrefix, StringComparison.Ordinal);

        /// <summary>
        /// Decides what a socket gets. The same seed always gives the same result. Returns false if the socket stays
        /// empty.
        /// </summary>
        public bool Pick(uint seed, out GameObject prefab, out float yaw, out float scale)
        {
            var random = new Unity.Mathematics.Random(seed == 0 ? 1u : seed);
            prefab = null;
            yaw = 0f;
            scale = 1f;
            if (random.NextFloat() >= fillChance)
            {
                return false;
            }

            float total = 0f;
            foreach (Entry entry in props)
            {
                if (entry.prefab != null)
                {
                    total += entry.weight;
                }
            }
            float pick = random.NextFloat() * total;
            foreach (Entry entry in props)
            {
                if (entry.prefab == null || entry.weight <= 0f)
                {
                    continue;
                }
                prefab = entry.prefab;
                pick -= entry.weight;
                if (pick < 0f)
                {
                    break;
                }
            }
            yaw = randomYaw ? random.NextFloat(0f, 360f) : 0f;
            scale = random.NextFloat(math.min(scaleRange.x, scaleRange.y), math.max(scaleRange.x, scaleRange.y));
            return prefab != null;
        }

        public void NotifyChanged() => Changed?.Invoke(this);

        private void OnValidate() => NotifyChanged();
    }
}
