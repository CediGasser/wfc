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
        public void CellToWorldMatchesTheShownTiles()
        {
            // Previews are drawn with CellToWorld, tiles are shown with ToRotationScale; both must agree.
            var chunkObject = new GameObject("chunk");
            var tile = new GameObject("tile");
            try
            {
                chunkObject.transform.SetPositionAndRotation(new Vector3(10, 0, 3), Quaternion.Euler(0, 30, 0));
                var chunk = chunkObject.AddComponent<Chunk>();
                tile.transform.SetParent(chunkObject.transform, false);

                for (int i = 0; i < Orientation.Count; i++)
                {
                    var cell = new int3(i % 5, i % 3, i % 4);
                    Orientation orientation = Orientation.FromIndex(i);
                    orientation.ToRotationScale(out Quaternion rotation, out Vector3 scale);
                    tile.transform.localPosition = cell.ToVector3();
                    tile.transform.localRotation = rotation;
                    tile.transform.localScale = scale;

                    Matrix4x4 expected = chunk.CellToWorld(cell, orientation);
                    Matrix4x4 actual = tile.transform.localToWorldMatrix;
                    for (int entry = 0; entry < 16; entry++)
                    {
                        Assert.That(actual[entry], Is.EqualTo(expected[entry]).Within(1e-4f), $"{orientation}, entry {entry}");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(chunkObject);
            }
        }
    }
}
