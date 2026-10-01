using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using Wfc.Authoring;
using Wfc.Editor;

namespace Wfc.Tests
{
    public class RulesetBakerTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in created)
            {
                if (obj != null)
                {
                    Object.DestroyImmediate(obj);
                }
            }
            created.Clear();
        }

        private TileDefinition Definition(string name, OrientationPreset preset)
        {
            var definition = ScriptableObject.CreateInstance<TileDefinition>();
            definition.name = name;
            definition.SetAllowedOrientations(preset);
            created.Add(definition);
            return definition;
        }

        private RulesetAuthoring Root()
        {
            var root = new GameObject("samples").AddComponent<RulesetAuthoring>();
            created.Add(root.gameObject);
            return root;
        }

        private static TileInstance Place(RulesetAuthoring root, TileDefinition definition, Vector3 position, Transform parent = null)
        {
            var instance = new GameObject(definition.name).AddComponent<TileInstance>();
            instance.Definition = definition;
            instance.transform.SetParent(parent != null ? parent : root.transform, false);
            instance.transform.localPosition = position;
            return instance;
        }

        [Test]
        public void AnalyzeCollectsInstancesAndPairs()
        {
            RulesetAuthoring root = Root();
            TileDefinition air = Definition("air", OrientationPreset.All);
            TileDefinition block = Definition("block", OrientationPreset.YawMirror);
            var group = new GameObject("group").transform;
            group.SetParent(root.transform, false);
            group.localPosition = new Vector3(0, 5, 0);

            Place(root, block, Vector3.zero);
            Place(root, air, Vector3.up);
            Place(root, air, new Vector3(0, -4, 0), group);   // cell (0, 1, 0) through the group, same cell as the first air
            Place(root, air, new Vector3(1, 1, 0));

            RulesetBaker.Analysis analysis = RulesetBaker.Analyze(root, detectSymmetry: false);

            Assert.That(analysis.tiles, Is.EquivalentTo(new[] { air, block }));
            Assert.That(analysis.instances.Count, Is.EqualTo(4));
            Assert.That(analysis.input.instances[2].cell, Is.EqualTo(new int3(0, 1, 0)));
            Assert.That(analysis.Data.diagnostics.Any(d => d.code == DiagnosticCode.OverlappingInstances), Is.True);
            Assert.That(analysis.result.pairs.Count, Is.EqualTo(2), "block-air and air-air");
            Assert.That(analysis.IsBakedAssetUpToDate, Is.False);
        }

        [Test]
        public void UnsnappedAndUnassignedInstancesAreReported()
        {
            RulesetAuthoring root = Root();
            TileDefinition block = Definition("block", OrientationPreset.YawMirror);
            TileInstance crooked = Place(root, block, new Vector3(0.3f, 0, 0));
            crooked.transform.localRotation = Quaternion.Euler(0, 10, 0);
            var unassigned = new GameObject("unassigned").AddComponent<TileInstance>();
            unassigned.transform.SetParent(root.transform, false);

            RulesetBaker.Analysis analysis = RulesetBaker.Analyze(root, detectSymmetry: false);

            Assert.That(analysis.unassigned, Is.EqualTo(new[] { unassigned }));
            Assert.That(analysis.input.instances.Single().cell, Is.EqualTo(int3.zero));
            DiagnosticCode[] codes = analysis.Data.diagnostics.Select(d => d.code).ToArray();
            Assert.That(codes, Does.Contain(DiagnosticCode.UnsnappedInstance));
            Assert.That(codes, Does.Contain(DiagnosticCode.MissingDefinition));
        }

        [Test]
        public void OverridesOnTheRootAreApplied()
        {
            RulesetAuthoring root = Root();
            TileDefinition block = Definition("block", OrientationPreset.YawMirror);
            Place(root, block, Vector3.zero);
            Place(root, block, Vector3.right);

            OrbitKey key = RulesetBaker.Analyze(root, detectSymmetry: false).Data.orbits.Single().key;
            root.SetOverride(key, 3f);

            Orbit orbit = RulesetBaker.Analyze(root, detectSymmetry: false).Data.orbits.Single();
            Assert.That(orbit.weight, Is.EqualTo(3f));
            Assert.That(orbit.overridden, Is.True);
            Assert.That(root.TryGetOverride(key.Reversed, out float weight) && weight == 3f, Is.True);
        }
    }
}
