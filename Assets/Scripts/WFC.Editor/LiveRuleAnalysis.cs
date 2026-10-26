using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>
    /// Keeps an up-to-date analysis of every rule source in the open scenes, so gizmos, inspectors and tools can show
    /// the derived rules while samples or sockets are being edited.
    /// </summary>
    [InitializeOnLoad]
    public static class LiveRuleAnalysis
    {
        private const double MinRefreshInterval = 0.1;

        private static readonly Dictionary<RuleSource, RuleSourceAnalysis> Cache = new Dictionary<RuleSource, RuleSourceAnalysis>();
        private static bool dirty = true;
        private static double lastRefresh;

        static LiveRuleAnalysis()
        {
            // Only authored data counts: generated chunk layers and the hidden tile visuals never change rules.
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            Chunk.AnyChanged += _ => MarkDirty();
            RuleSource.Changed += _ => MarkDirty();
            TileSet.Changed += _ => MarkDirty();
            TileDefinition.Changed += _ => MarkDirty();
            Undo.undoRedoPerformed += MarkDirty;
            EditorSceneManager.sceneOpened += (_, _) => MarkDirty();
            EditorApplication.update += Update;
        }

        /// <summary>Raised after the analyses were recomputed.</summary>
        public static event Action Updated;

        public static void MarkDirty() => dirty = true;

        /// <summary>The current analysis of a source, computed on demand.</summary>
        public static RuleSourceAnalysis Get(RuleSource source)
        {
            if (source == null)
            {
                return null;
            }
            if (!Cache.TryGetValue(source, out RuleSourceAnalysis analysis))
            {
                analysis = RulesetBaker.Analyze(source);
                Cache[source] = analysis;
            }
            return analysis;
        }

        private static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            for (int i = 0; i < stream.length; i++)
            {
                // Property changes arrive through the chunk and source events; structural changes (sources added,
                // removed, duplicated) need a refresh here.
                if (stream.GetEventType(i) != ObjectChangeKind.ChangeGameObjectOrComponentProperties)
                {
                    MarkDirty();
                    return;
                }
            }
        }

        private static void Update()
        {
            if (!dirty || EditorApplication.timeSinceStartup - lastRefresh < MinRefreshInterval)
            {
                return;
            }
            dirty = false;
            lastRefresh = EditorApplication.timeSinceStartup;
            Cache.Clear();
            foreach (RuleSource source in UnityEngine.Object.FindObjectsByType<RuleSource>(FindObjectsInactive.Exclude))
            {
                Cache[source] = RulesetBaker.Analyze(source);
            }
            Updated?.Invoke();
            SceneView.RepaintAll();
        }
    }
}
