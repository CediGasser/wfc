using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>
    /// Keeps an up-to-date rule analysis for every ruleset root in the open scenes, so gizmos, inspectors and tools
    /// can show learned connections while samples are being edited.
    /// </summary>
    [InitializeOnLoad]
    public static class RulesetLiveAnalysis
    {
        private const double MinRefreshInterval = 0.1;

        private static readonly Dictionary<RulesetAuthoring, RulesetBaker.Analysis> Cache = new Dictionary<RulesetAuthoring, RulesetBaker.Analysis>();
        private static bool dirty = true;
        private static double lastRefresh;

        static RulesetLiveAnalysis()
        {
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            EditorApplication.hierarchyChanged += MarkDirty;
            Undo.undoRedoPerformed += MarkDirty;
            TileDefinition.Changed += _ => MarkDirty();
            EditorSceneManager.sceneOpened += (_, _) => MarkDirty();
            EditorApplication.update += Update;
        }

        /// <summary>Raised after the analyses were recomputed.</summary>
        public static event Action Updated;

        public static void MarkDirty() => dirty = true;

        /// <summary>The current analysis of a root, computed on demand.</summary>
        public static RulesetBaker.Analysis Get(RulesetAuthoring root)
        {
            if (root == null)
            {
                return null;
            }
            if (!Cache.TryGetValue(root, out RulesetBaker.Analysis analysis))
            {
                analysis = RulesetBaker.Analyze(root);
                Cache[root] = analysis;
            }
            return analysis;
        }

        private static void OnChangesPublished(ref ObjectChangeEventStream stream) => MarkDirty();

        private static void Update()
        {
            if (!dirty || EditorApplication.timeSinceStartup - lastRefresh < MinRefreshInterval)
            {
                return;
            }
            dirty = false;
            lastRefresh = EditorApplication.timeSinceStartup;
            Cache.Clear();
            foreach (RulesetAuthoring root in UnityEngine.Object.FindObjectsByType<RulesetAuthoring>(FindObjectsInactive.Exclude))
            {
                Cache[root] = RulesetBaker.Analyze(root);
            }
            Updated?.Invoke();
            SceneView.RepaintAll();
        }
    }
}
