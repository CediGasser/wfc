using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Wfc
{
    public enum OrientationPreset
    {
        /// <summary>Only the authored orientation.</summary>
        Fixed,
        /// <summary>The four 90° rotations around the vertical axis.</summary>
        Yaw,
        /// <summary>Yaw rotations plus their mirrored versions (8).</summary>
        YawMirror,
        /// <summary>All 24 rotations, including upside down and sideways.</summary>
        AllRotations,
        /// <summary>All 48 orientations, rotations and mirrored rotations.</summary>
        All,
        Custom,
    }

    /// <summary>
    /// A set of orientations stored as a 48-bit mask. Allowed orientations and symmetries of a tile are sets
    /// that should form a group (contain the identity and be closed under composition).
    /// </summary>
    public readonly struct OrientationSet : IEquatable<OrientationSet>
    {
        private const ulong FullMask = (1UL << Orientation.Count) - 1;

        public static readonly OrientationSet Empty = new OrientationSet(0);
        public static readonly OrientationSet IdentityOnly = new OrientationSet(1);
        public static readonly OrientationSet All = new OrientationSet(FullMask);
        public static readonly OrientationSet Yaw = Generate(Orientation.RotationY90);
        public static readonly OrientationSet YawMirror = Generate(Orientation.RotationY90, Orientation.MirrorX);
        public static readonly OrientationSet AllRotations = Generate(Orientation.RotationX90, Orientation.RotationY90);

        public OrientationSet(ulong mask)
        {
            Mask = mask & FullMask;
        }

        public ulong Mask { get; }

        public int Count => math.countbits(Mask);

        public bool IsEmpty => Mask == 0;

        public static OrientationSet FromPreset(OrientationPreset preset, ulong customMask = 1)
        {
            return preset switch
            {
                OrientationPreset.Fixed => IdentityOnly,
                OrientationPreset.Yaw => Yaw,
                OrientationPreset.YawMirror => YawMirror,
                OrientationPreset.AllRotations => AllRotations,
                OrientationPreset.All => All,
                _ => new OrientationSet(customMask),
            };
        }

        /// <summary>The smallest group containing the identity and all generators.</summary>
        public static OrientationSet Generate(params Orientation[] generators)
        {
            var set = IdentityOnly;
            foreach (Orientation generator in generators)
            {
                set = set.With(generator);
            }
            return set.Closure();
        }

        public bool Contains(Orientation orientation) => (Mask & (1UL << orientation.Index)) != 0;

        public OrientationSet With(Orientation orientation) => new OrientationSet(Mask | (1UL << orientation.Index));

        public OrientationSet Intersect(OrientationSet other) => new OrientationSet(Mask & other.Mask);

        public OrientationSet Union(OrientationSet other) => new OrientationSet(Mask | other.Mask);

        /// <summary>True if the set contains the identity and is closed under composition.</summary>
        public bool IsGroup
        {
            get
            {
                if (!Contains(Orientation.Identity))
                {
                    return false;
                }
                foreach (Orientation a in this)
                {
                    foreach (Orientation b in this)
                    {
                        if (!Contains(a * b))
                        {
                            return false;
                        }
                    }
                }
                return true;
            }
        }

        /// <summary>Adds the identity and every product of members until the set is closed, turning it into a group.</summary>
        public OrientationSet Closure()
        {
            var set = With(Orientation.Identity);
            while (true)
            {
                var grown = set;
                foreach (Orientation a in set)
                {
                    foreach (Orientation b in set)
                    {
                        grown = grown.With(a * b);
                    }
                }
                if (grown.Mask == set.Mask)
                {
                    return set;
                }
                set = grown;
            }
        }

        public Enumerator GetEnumerator() => new Enumerator(Mask);

        public List<Orientation> ToList()
        {
            var list = new List<Orientation>(Count);
            foreach (Orientation orientation in this)
            {
                list.Add(orientation);
            }
            return list;
        }

        public bool Equals(OrientationSet other) => Mask == other.Mask;

        public override bool Equals(object obj) => obj is OrientationSet other && Equals(other);

        public override int GetHashCode() => Mask.GetHashCode();

        public override string ToString() => $"OrientationSet({Count})";

        /// <summary>Allocation-free enumeration in ascending index order.</summary>
        public struct Enumerator
        {
            private ulong remaining;

            public Enumerator(ulong mask)
            {
                remaining = mask;
                Current = Orientation.Identity;
            }

            public Orientation Current { get; private set; }

            public bool MoveNext()
            {
                if (remaining == 0)
                {
                    return false;
                }
                int index = math.tzcnt(remaining);
                remaining &= remaining - 1;
                Current = Orientation.FromIndex(index);
                return true;
            }
        }
    }
}
