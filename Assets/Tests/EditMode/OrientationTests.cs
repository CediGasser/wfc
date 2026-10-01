using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace Wfc.Tests
{
    public class OrientationTests
    {
        private static IEnumerable<Orientation> All()
        {
            for (int i = 0; i < Orientation.Count; i++)
            {
                yield return Orientation.FromIndex(i);
            }
        }

        [Test]
        public void AllMatricesAreDistinctSignedPermutations()
        {
            var seen = new HashSet<int3x3>();
            foreach (Orientation o in All())
            {
                Assert.That(seen.Add(o.Matrix), Is.True, $"Duplicate matrix for {o}");
                Assert.That(Orientation.TryFromMatrix(o.Matrix, out Orientation back), Is.True);
                Assert.That(back, Is.EqualTo(o));
            }
            Assert.That(seen.Count, Is.EqualTo(48));
        }

        [Test]
        public void IdentityIsIndexZero()
        {
            Assert.That(Orientation.Identity.Index, Is.EqualTo(0));
            Assert.That(Orientation.Identity.Matrix, Is.EqualTo(int3x3.identity));
        }

        [Test]
        public void GroupAxiomsHold()
        {
            foreach (Orientation a in All())
            {
                Assert.That(Orientation.Identity * a, Is.EqualTo(a));
                Assert.That(a * Orientation.Identity, Is.EqualTo(a));
                Assert.That(a * a.Inverse, Is.EqualTo(Orientation.Identity));
                Assert.That(a.Inverse * a, Is.EqualTo(Orientation.Identity));
                foreach (Orientation b in All())
                {
                    Assert.That((a * b).Matrix, Is.EqualTo(math.mul(a.Matrix, b.Matrix)));
                    foreach (Orientation c in All())
                    {
                        if ((a * b) * c != a * (b * c))
                        {
                            Assert.Fail($"Not associative for {a}, {b}, {c}");
                        }
                    }
                }
            }
        }

        [Test]
        public void HalfAreMirrored()
        {
            int mirrored = 0;
            foreach (Orientation o in All())
            {
                Assert.That(o.Determinant, Is.EqualTo(1).Or.EqualTo(-1));
                Assert.That(o.IsMirrored, Is.EqualTo(o.Determinant < 0));
                mirrored += o.IsMirrored ? 1 : 0;
            }
            Assert.That(mirrored, Is.EqualTo(24));
        }

        [Test]
        public void DirectionMappingMatchesMatrix()
        {
            foreach (Orientation o in All())
            {
                for (int d = 0; d < Directions.Count; d++)
                {
                    var direction = (Direction)d;
                    Assert.That(o.Apply(direction).ToVector(), Is.EqualTo(math.mul(o.Matrix, direction.ToVector())));
                    Assert.That(o.Apply(direction.Opposite()), Is.EqualTo(o.Apply(direction).Opposite()));
                }
            }
        }

        [Test]
        public void TryFromMatrixRejectsNonPermutations()
        {
            Assert.That(Orientation.TryFromMatrix(int3x3.zero, out _), Is.False);
            Assert.That(Orientation.TryFromMatrix(new int3x3(1, 1, 0, 0, 1, 0, 0, 0, 1), out _), Is.False);
            Assert.That(Orientation.TryFromMatrix(new int3x3(2, 0, 0, 0, 1, 0, 0, 0, 1), out _), Is.False);
            Assert.That(Orientation.TryFromMatrix(new int3x3(1, 0, 0, 1, 0, 0, 0, 0, 1), out _), Is.False);
        }

        [Test]
        public void NamedRotationsFollowUnityConventions()
        {
            // Quaternion.Euler(0, 90, 0) turns forward into right; Euler(90, 0, 0) turns up into forward; Euler(0, 0, 90) turns right into up.
            Assert.That(Orientation.RotationY90.Apply(Direction.PosZ), Is.EqualTo(Direction.PosX));
            Assert.That(Orientation.RotationX90.Apply(Direction.PosY), Is.EqualTo(Direction.PosZ));
            Assert.That(Orientation.RotationZ90.Apply(Direction.PosX), Is.EqualTo(Direction.PosY));
            Assert.That(Orientation.MirrorX.Apply(Direction.PosX), Is.EqualTo(Direction.NegX));
            Assert.That(Orientation.MirrorX.IsMirrored, Is.True);

            Orientation y = Orientation.RotationY90;
            Assert.That(y * y * y * y, Is.EqualTo(Orientation.Identity));
            Assert.That(y * y, Is.Not.EqualTo(Orientation.Identity));
        }

        [Test]
        public void PresetsAreGroupsOfTheExpectedSize()
        {
            Assert.That(OrientationSet.FromPreset(OrientationPreset.Fixed).Count, Is.EqualTo(1));
            Assert.That(OrientationSet.Yaw.Count, Is.EqualTo(4));
            Assert.That(OrientationSet.YawMirror.Count, Is.EqualTo(8));
            Assert.That(OrientationSet.AllRotations.Count, Is.EqualTo(24));
            Assert.That(OrientationSet.All.Count, Is.EqualTo(48));
            foreach (OrientationPreset preset in new[] { OrientationPreset.Fixed, OrientationPreset.Yaw, OrientationPreset.YawMirror, OrientationPreset.AllRotations, OrientationPreset.All })
            {
                Assert.That(OrientationSet.FromPreset(preset).IsGroup, Is.True, preset.ToString());
            }

            foreach (Orientation o in OrientationSet.YawMirror)
            {
                Assert.That(o.Apply(Direction.PosY), Is.EqualTo(Direction.PosY), "Yaw and mirror must keep up pointing up");
            }
            foreach (Orientation o in OrientationSet.AllRotations)
            {
                Assert.That(o.IsMirrored, Is.False);
            }
        }

        [Test]
        public void ClosureAndGroupCheck()
        {
            Assert.That(OrientationSet.Generate(Orientation.RotationX90).Count, Is.EqualTo(4));
            Assert.That(new OrientationSet(1UL | (1UL << Orientation.RotationY90.Index)).IsGroup, Is.False, "{identity, Y90} lacks Y180 and Y270");
            Assert.That(OrientationSet.IdentityOnly.With(Orientation.MirrorX).IsGroup, Is.True, "a mirror is its own inverse");
            Assert.That(OrientationSet.Empty.IsGroup, Is.False);
            Assert.That(OrientationSet.IdentityOnly.IsGroup, Is.True);

            var closed = new OrientationSet(1UL << Orientation.RotationZ90.Index).Closure();
            Assert.That(closed.IsGroup, Is.True);
            Assert.That(closed.Count, Is.EqualTo(4));
        }

        [Test]
        public void EnumerationIsAscending()
        {
            var list = OrientationSet.All.ToList();
            Assert.That(list.Count, Is.EqualTo(48));
            for (int i = 0; i < list.Count; i++)
            {
                Assert.That(list[i].Index, Is.EqualTo(i));
            }
        }
    }
}
