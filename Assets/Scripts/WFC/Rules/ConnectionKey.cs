using System;

namespace Wfc
{
    /// <summary>
    /// Stable identity of one learned <see cref="Connection"/>, used to store weight overrides across bakes. Tile B
    /// sits next to tile A in <see cref="direction"/>, both given by their stable tile id and the representative
    /// orientation of their variant. Keys are canonical: the same connection always produces the same key,
    /// regardless of direction, which tile came first or the order of the tiles.
    /// </summary>
    [Serializable]
    public struct ConnectionKey : IEquatable<ConnectionKey>, IComparable<ConnectionKey>
    {
        public string tileA;
        public int orientationA;
        public int direction;
        public string tileB;
        public int orientationB;

        public ConnectionKey(string tileA, int orientationA, Direction direction, string tileB, int orientationB)
        {
            this.tileA = tileA;
            this.orientationA = orientationA;
            this.direction = (int)direction;
            this.tileB = tileB;
            this.orientationB = orientationB;
        }

        public Direction Direction => (Direction)direction;

        /// <summary>The same connection seen from tile B.</summary>
        public ConnectionKey Reversed => new ConnectionKey(tileB, orientationB, Direction.Opposite(), tileA, orientationA);

        /// <summary>The smaller of this key and its reversed form.</summary>
        public ConnectionKey Undirected
        {
            get
            {
                ConnectionKey reversed = Reversed;
                return CompareTo(reversed) <= 0 ? this : reversed;
            }
        }

        public int CompareTo(ConnectionKey other)
        {
            int result = string.CompareOrdinal(tileA, other.tileA);
            if (result != 0) return result;
            result = orientationA.CompareTo(other.orientationA);
            if (result != 0) return result;
            result = direction.CompareTo(other.direction);
            if (result != 0) return result;
            result = string.CompareOrdinal(tileB, other.tileB);
            if (result != 0) return result;
            return orientationB.CompareTo(other.orientationB);
        }

        public bool Equals(ConnectionKey other)
        {
            return tileA == other.tileA && orientationA == other.orientationA && direction == other.direction
                && tileB == other.tileB && orientationB == other.orientationB;
        }

        public override bool Equals(object obj) => obj is ConnectionKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(tileA, orientationA, direction, tileB, orientationB);

        public override string ToString() => $"{tileA}#{orientationA} {Direction.ToShortString()} {tileB}#{orientationB}";
    }

    /// <summary>A user-defined weight for one learned connection. A weight of 0 removes the connection.</summary>
    [Serializable]
    public struct WeightOverride
    {
        public ConnectionKey key;
        public float weight;

        public WeightOverride(ConnectionKey key, float weight)
        {
            this.key = key;
            this.weight = weight;
        }
    }
}
