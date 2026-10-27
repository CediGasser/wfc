using System;

namespace Wfc
{
    public enum DiagnosticSeverity
    {
        Info,
        Warning,
        Error,
    }

    /// <summary>What a diagnostic is about. Values are stored in baked rulesets, so they're fixed.</summary>
    public enum DiagnosticCode
    {
        /// <summary>Two tiles share an id; the later one is ignored.</summary>
        DuplicateTile = 0,
        /// <summary>A tile's allowed orientations aren't a group; the tile is ignored.</summary>
        AllowedSetNotAGroup = 1,
        /// <summary>A cell or setting refers to a tile slot that is empty or out of range.</summary>
        MissingTile = 2,
        /// <summary>A tile is placed in an orientation its allowed orientations don't include.</summary>
        DisallowedOrientation = 3,
        /// <summary>A tile's sockets tell apart orientations that look identical, so it gets more variants.</summary>
        SocketsBreakSymmetry = 4,
        /// <summary>Nothing may be next to a tile, so it can't be generated.</summary>
        TileNeverConnected = 5,
        /// <summary>Nothing may be next to some face of a tile in some orientation.</summary>
        DeadEnd = 6,
        /// <summary>An invisible tile such as air may never be next to itself.</summary>
        MissingSelfConnection = 7,
        /// <summary>A weight override matches no connection.</summary>
        OrphanedOverride = 8,
        /// <summary>A socket label can't be read, e.g. it has an unsupported number of parts.</summary>
        InvalidSocket = 9,
    }

    /// <summary>
    /// A problem or hint found while deriving rules. <see cref="cells"/> point at the sample cells involved (flat
    /// indices into the source's sample chunk), so editor tooling can select them.
    /// </summary>
    [Serializable]
    public sealed class RuleDiagnostic
    {
        public DiagnosticSeverity severity;
        public DiagnosticCode code;
        public string message;
        /// <summary>Tile index the diagnostic is about, or -1.</summary>
        public int tile = -1;
        public int[] cells = Array.Empty<int>();
        /// <summary>Bit per local face (<see cref="Direction"/>) the diagnostic is about, e.g. dead ends; 0 if none.</summary>
        public int faces;

        public RuleDiagnostic()
        {
        }

        public RuleDiagnostic(DiagnosticSeverity severity, DiagnosticCode code, string message, int tile = -1, params int[] cells)
        {
            this.severity = severity;
            this.code = code;
            this.message = message;
            this.tile = tile;
            this.cells = cells ?? Array.Empty<int>();
        }

        public override string ToString() => $"[{severity}] {code}: {message}";
    }
}
