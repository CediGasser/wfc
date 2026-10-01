using System;

namespace Wfc
{
    public enum DiagnosticSeverity
    {
        Info,
        Warning,
        Error,
    }

    public enum DiagnosticCode
    {
        InvalidTile,
        AllowedSetNotAGroup,
        OrientationMapped,
        InvalidOrientation,
        OverlappingInstances,
        UnsnappedInstance,
        MissingDefinition,
        TileNeverConnected,
        DeadEnd,
        MissingSelfConnection,
        OrphanedOverride,
    }

    /// <summary>
    /// A problem or hint found while deriving rules. <see cref="sourceIds"/> point back to the sample
    /// instances involved (indices into the derivation input), so editor tooling can select them.
    /// </summary>
    [Serializable]
    public sealed class RuleDiagnostic
    {
        public DiagnosticSeverity severity;
        public DiagnosticCode code;
        public string message;
        /// <summary>Tile index the diagnostic is about, or -1.</summary>
        public int tile = -1;
        public int[] sourceIds = Array.Empty<int>();

        public RuleDiagnostic()
        {
        }

        public RuleDiagnostic(DiagnosticSeverity severity, DiagnosticCode code, string message, int tile = -1, params int[] sourceIds)
        {
            this.severity = severity;
            this.code = code;
            this.message = message;
            this.tile = tile;
            this.sourceIds = sourceIds ?? Array.Empty<int>();
        }

        public override string ToString() => $"[{severity}] {code}: {message}";
    }
}
