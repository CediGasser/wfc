using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Tests
{
    public class OrientationUnityTests
    {
        [Test]
        public void RotationScaleRoundTripsForAllOrientations()
        {
            for (int i = 0; i < Orientation.Count; i++)
            {
                Orientation orientation = Orientation.FromIndex(i);
                orientation.ToRotationScale(out Quaternion rotation, out Vector3 scale);
                Matrix4x4 trs = Matrix4x4.TRS(Vector3.zero, rotation, scale);

                Assert.That(OrientationUnity.Nearest(trs, out float error), Is.EqualTo(orientation), orientation.ToString());
                Assert.That(error, Is.LessThan(1e-5f));
                Assert.That(scale.x, Is.EqualTo(orientation.IsMirrored ? -1f : 1f));
                for (int d = 0; d < Directions.Count; d++)
                {
                    Vector3 expected = orientation.Apply((Direction)d).ToVector().ToVector3();
                    Vector3 actual = trs.MultiplyVector(((Direction)d).ToVector().ToVector3());
                    Assert.That(Vector3.Distance(expected, actual), Is.LessThan(1e-5f));
                }
            }
        }

        [Test]
        public void NamedRotationsMatchQuaternionEuler()
        {
            Assert.That(OrientationUnity.Nearest(Matrix4x4.Rotate(Quaternion.Euler(90, 0, 0)), out _), Is.EqualTo(Orientation.RotationX90));
            Assert.That(OrientationUnity.Nearest(Matrix4x4.Rotate(Quaternion.Euler(0, 90, 0)), out _), Is.EqualTo(Orientation.RotationY90));
            Assert.That(OrientationUnity.Nearest(Matrix4x4.Rotate(Quaternion.Euler(0, 0, 90)), out _), Is.EqualTo(Orientation.RotationZ90));
            Assert.That(OrientationUnity.Nearest(Matrix4x4.Scale(new Vector3(-1, 1, 1)), out _), Is.EqualTo(Orientation.MirrorX));
        }

        [Test]
        public void NearestReportsErrorForOffGridRotations()
        {
            Orientation nearest = OrientationUnity.Nearest(Matrix4x4.Rotate(Quaternion.Euler(0, 80, 0)), out float error);
            Assert.That(nearest, Is.EqualTo(Orientation.RotationY90));
            Assert.That(error, Is.GreaterThan(0.1f));
        }

        [Test]
        public void GridPlacementWritesExactCellsThroughRotatedParents()
        {
            var root = new GameObject("root");
            var group = new GameObject("group");
            var tile = new GameObject("tile");
            try
            {
                root.transform.SetPositionAndRotation(new Vector3(10, 0, 3), Quaternion.Euler(0, 30, 0));
                group.transform.SetParent(root.transform, false);
                group.transform.localPosition = new Vector3(2, 0, 0);
                group.transform.localRotation = Quaternion.Euler(0, 90, 0);
                tile.transform.SetParent(group.transform, false);

                for (int i = 0; i < Orientation.Count; i++)
                {
                    var cell = new int3(i % 5 - 2, i % 3, -i % 4);
                    Orientation orientation = Orientation.FromIndex(i);
                    GridPlacement.Write(tile.transform, root.transform, cell, orientation);
                    GridPlacement.Placement placement = GridPlacement.Read(tile.transform, root.transform);
                    Assert.That(placement.Cell, Is.EqualTo(cell));
                    Assert.That(placement.Orientation, Is.EqualTo(orientation));
                    Assert.That(placement.IsSnapped, Is.True);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
