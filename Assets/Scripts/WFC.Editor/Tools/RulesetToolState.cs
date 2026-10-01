using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>State shared between the scene tool, its overlay and the tile palette.</summary>
    public static class RulesetToolState
    {
        public readonly struct FaceSelection
        {
            public FaceSelection(TileInstance instance, Direction direction)
            {
                Instance = instance;
                Direction = direction;
            }

            public TileInstance Instance { get; }

            /// <summary>Face direction in the root's grid space.</summary>
            public Direction Direction { get; }

            public bool IsValid => Instance != null && Instance.Root != null;
        }

        public readonly struct Candidate
        {
            public Candidate(int variant, TileDefinition definition, Orientation orientation, float weight)
            {
                Variant = variant;
                Definition = definition;
                Orientation = orientation;
                Weight = weight;
            }

            public int Variant { get; }
            public TileDefinition Definition { get; }
            public Orientation Orientation { get; }
            public float Weight { get; }
        }

        private static TileDefinition paletteTile;
        private static FaceSelection? selectedFace;
        private static int ghostIndex;

        public static event Action Changed;

        /// <summary>The tile placed by Shift+Click; chosen in the tile palette.</summary>
        public static TileDefinition PaletteTile
        {
            get => paletteTile;
            set
            {
                paletteTile = value;
                Changed?.Invoke();
            }
        }

        public static FaceSelection? SelectedFace
        {
            get => selectedFace is { IsValid: true } ? selectedFace : null;
            set
            {
                selectedFace = value;
                ghostIndex = 0;
                Changed?.Invoke();
            }
        }

        public static int GhostIndex
        {
            get => ghostIndex;
            set
            {
                ghostIndex = value;
                Changed?.Invoke();
            }
        }

        public static RulesetAuthoring SelectedRoot => SelectedFace?.Instance.Root;

        public static int3 GetCell(FaceSelection face)
        {
            return GridPlacement.Read(face.Instance.transform, face.Instance.Root.transform).Cell;
        }

        public static int3 GetNeighbourCell(FaceSelection face) => GetCell(face) + face.Direction.ToVector();

        /// <summary>Every variant the samples allow next to the selected face, in variant order.</summary>
        public static List<Candidate> GetCandidates(RulesetBaker.Analysis analysis, FaceSelection face)
        {
            var candidates = new List<Candidate>();
            int source = analysis.instances.IndexOf(face.Instance);
            int variant = source >= 0 ? analysis.result.instanceVariant[source] : -1;
            if (variant < 0)
            {
                return candidates;
            }
            RulesetData data = analysis.Data;
            var (start, end) = data.GetNeighbourRange(variant, face.Direction);
            for (int i = start; i < end; i++)
            {
                int neighbour = data.adjacencyVariant[i];
                candidates.Add(new Candidate(neighbour, analysis.tiles[data.variantTile[neighbour]], data.GetVariantOrientation(neighbour), data.adjacencyWeight[i]));
            }
            return candidates;
        }

        /// <summary>The candidate currently previewed, wrapping the ghost index into range.</summary>
        public static Candidate? GetCurrentCandidate(List<Candidate> candidates)
        {
            if (candidates.Count == 0)
            {
                return null;
            }
            int index = ((ghostIndex % candidates.Count) + candidates.Count) % candidates.Count;
            return candidates[index];
        }

        public static void NotifyChanged() => Changed?.Invoke();

        public static Color GetCandidateColor(Candidate candidate)
        {
            Color color = candidate.Definition != null ? candidate.Definition.GizmoColor : Color.white;
            color.a = 0.45f;
            return color;
        }
    }
}
