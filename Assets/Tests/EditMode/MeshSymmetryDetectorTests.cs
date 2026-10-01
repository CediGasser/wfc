using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Wfc.Editor;

namespace Wfc.Tests
{
    public class MeshSymmetryDetectorTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in created)
            {
                Object.DestroyImmediate(obj);
            }
            created.Clear();
        }

        private GameObject Model(params (Vector3 position, Vector3 size)[] boxes)
        {
            var model = new GameObject("model");
            created.Add(model);
            foreach (var (position, size) in boxes)
            {
                GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(box.GetComponent<Collider>());
                box.transform.SetParent(model.transform, false);
                box.transform.localPosition = position;
                box.transform.localScale = size;
            }
            return model;
        }

        [Test]
        public void CubeIsFullySymmetric()
        {
            Assert.That(MeshSymmetryDetector.Detect(Model((Vector3.zero, Vector3.one))).Count, Is.EqualTo(48));
        }

        [Test]
        public void MissingModelIsFullySymmetric()
        {
            Assert.That(MeshSymmetryDetector.Detect(null), Is.EqualTo(OrientationSet.All));
        }

        [Test]
        public void StraightPipeKeepsItsAxis()
        {
            OrientationSet symmetry = MeshSymmetryDetector.Detect(Model((Vector3.zero, new Vector3(1f, 0.4f, 0.4f))));
            Assert.That(symmetry.Count, Is.EqualTo(16));
            foreach (Orientation o in symmetry)
            {
                Assert.That(o.Apply(Direction.PosX).Axis(), Is.EqualTo(0));
            }
        }

        [Test]
        public void LShapeHasFourSymmetries()
        {
            OrientationSet symmetry = MeshSymmetryDetector.Detect(Model(
                (new Vector3(0.25f, 0f, 0f), new Vector3(0.5f, 0.2f, 0.2f)),
                (new Vector3(0f, 0.25f, 0f), new Vector3(0.2f, 0.5f, 0.2f))));
            Assert.That(symmetry.Count, Is.EqualTo(4));
            Assert.That(symmetry.IsGroup, Is.True);
        }

        [Test]
        public void AsymmetricShapeOnlyHasIdentity()
        {
            OrientationSet symmetry = MeshSymmetryDetector.Detect(Model(
                (new Vector3(0.3f, 0f, 0f), Vector3.one * 0.05f),
                (new Vector3(0f, 0.2f, 0f), Vector3.one * 0.05f),
                (new Vector3(0f, 0f, 0.1f), Vector3.one * 0.05f)));
            Assert.That(symmetry, Is.EqualTo(OrientationSet.IdentityOnly));
        }

        [Test]
        public void MaterialsBreakSymmetry()
        {
            GameObject model = Model(
                (new Vector3(0.25f, 0f, 0f), Vector3.one * 0.2f),
                (new Vector3(-0.25f, 0f, 0f), Vector3.one * 0.2f));
            int withSameMaterial = MeshSymmetryDetector.Detect(model).Count;

            var red = new Material(Shader.Find("Hidden/Internal-Colored"));
            created.Add(red);
            model.transform.GetChild(0).GetComponent<MeshRenderer>().sharedMaterial = red;
            int withDifferentMaterial = MeshSymmetryDetector.Detect(model).Count;

            Assert.That(withSameMaterial, Is.EqualTo(16));
            Assert.That(withDifferentMaterial, Is.EqualTo(8), "the two ends can no longer be swapped");
        }
    }
}
