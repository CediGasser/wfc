using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using static Wfc.Tests.SampleRulesTests;

namespace Wfc.Tests
{
    public class SocketRulesTests
    {
        /// <summary>Labels of the six local faces, in <see cref="Direction"/> order.</summary>
        private static string[] Faces(string posX = "", string negX = "", string posY = "", string negY = "", string posZ = "", string negZ = "")
        {
            return new[] { posX, negX, posY, negY, posZ, negZ };
        }

        private static string[] AllFaces(string label) => Enumerable.Repeat(label, Directions.Count).ToArray();

        /// <summary>The parts of a fingerprint that the solver sees: variants and adjacency with weights.</summary>
        private static string[] SolverView(RulesetData data)
        {
            return RulesetFingerprint.Describe(data).Split('\n').Where(line => line.StartsWith("variant ") || line.StartsWith("adjacent ")).ToArray();
        }

        [Test]
        public void EqualLabelsFit()
        {
            var tiles = new List<TileSpec> { Tile("a", OrientationSet.All, OrientationSet.All), Tile("b", OrientationSet.All, OrientationSet.All) };
            var sockets = new SocketRules { faceLabels = { AllFaces("x"), AllFaces("y") } };

            RulesetData data = sockets.Derive(tiles).data;

            Assert.That(data.connections.Select(c => (c.tileA, c.tileB)), Is.EquivalentTo(new[] { (0, 0), (1, 1) }), "x fits x and y fits y, but x doesn't fit y");
            Assert.That(data.connections.Select(c => c.weight), Is.All.EqualTo(1f));
            Assert.That(data.connections.Select(c => c.observations), Is.All.Zero);
        }

        [Test]
        public void CompatiblePairsFitInBothOrders()
        {
            var tiles = new List<TileSpec> { Tile("plug", OrientationSet.All, OrientationSet.All), Tile("socket", OrientationSet.All, OrientationSet.All) };
            var sockets = new SocketRules { faceLabels = { AllFaces("male"), AllFaces("female") }, compatible = { ("female", "male") } };

            RulesetData data = sockets.Derive(tiles).data;

            int plug = VariantsOf(data, 0).Single();
            int socket = VariantsOf(data, 1).Single();
            foreach (Direction direction in new[] { Direction.PosX, Direction.NegY, Direction.PosZ })
            {
                Assert.That(Neighbours(data, plug, direction), Does.Contain(socket));
                Assert.That(Neighbours(data, socket, direction), Does.Contain(plug));
            }
            Assert.That(data.connections.Length, Is.EqualTo(3), "plug-plug, socket-socket and plug-socket");
        }

        [Test]
        public void PipeSocketsGiveTheSameRulesAsAPipeSample()
        {
            var tiles = new List<TileSpec> { Tile("straight", OrientationSet.AllRotations, StraightPipeSymmetry) };
            var sockets = new SocketRules { faceLabels = { Faces(posX: "pipe", negX: "pipe") } };
            var sample = new SampleRules { sample = new SampleBuilder().Place(0, int3.zero).Place(0, new int3(1, 0, 0)).Build(), weighting = SampleWeighting.Presence };

            RulesetData fromSockets = sockets.Derive(tiles).data;
            RulesetData fromSample = sample.Derive(tiles).data;

            Assert.That(SolverView(fromSockets), Is.EqualTo(SolverView(fromSample)));
            Assert.That(fromSockets.connections.Select(c => c.key), Is.EqualTo(fromSample.connections.Select(c => c.key)));
        }

        [Test]
        public void SocketsReproduceLayers()
        {
            var tiles = new List<TileSpec>
            {
                Tile("bottom", OrientationSet.IdentityOnly, OrientationSet.IdentityOnly),
                Tile("top", OrientationSet.IdentityOnly, OrientationSet.IdentityOnly),
            };
            var sockets = new SocketRules
            {
                faceLabels =
                {
                    Faces(posX: "low", negX: "low", posY: "stack", posZ: "low", negZ: "low"),
                    Faces(posX: "high", negX: "high", negY: "stack", posZ: "high", negZ: "high"),
                },
            };
            var layers = new SampleBuilder()
                .Place(0, new int3(0, 0, 0)).Place(0, new int3(1, 0, 0)).Place(0, new int3(0, 0, 1))
                .Place(1, new int3(0, 1, 0)).Place(1, new int3(1, 1, 0)).Place(1, new int3(0, 1, 1));
            var sample = new SampleRules { sample = layers.Build(), weighting = SampleWeighting.Presence };

            RulesetData fromSockets = sockets.Derive(tiles).data;
            RulesetData fromSample = sample.Derive(tiles).data;

            Assert.That(SolverView(fromSockets), Is.EqualTo(SolverView(fromSample)));
            Assert.That(fromSockets.connections.Select(c => c.key), Is.EqualTo(fromSample.connections.Select(c => c.key)));
        }

        [Test]
        public void AdjacencyIsSymmetric()
        {
            var tiles = new List<TileSpec>
            {
                Tile("straight", OrientationSet.AllRotations, StraightPipeSymmetry),
                Tile("corner", OrientationSet.AllRotations, Stabilizer(Direction.PosX, Direction.PosY)),
                Tile("air", OrientationSet.All, OrientationSet.All, isInvisible: true),
            };
            var sockets = new SocketRules
            {
                faceLabels =
                {
                    Faces("pipe", "pipe", "open", "open", "open", "open"),
                    Faces("pipe", "open", "pipe", "open", "open", "open"),
                    AllFaces("open"),
                },
            };

            RulesetData data = sockets.Derive(tiles).data;

            Assert.That(data.connections.Length, Is.GreaterThan(3));
            AssertSymmetric(data);
        }

        [Test]
        public void LabelsSplitVariantsOfASymmetricModel()
        {
            // A model-less tile looks the same in every orientation, but its sockets don't.
            var tiles = new List<TileSpec> { Tile("marker", OrientationSet.Yaw, OrientationSet.All, isInvisible: true) };
            var sockets = new SocketRules { faceLabels = { Faces("arrow", "side", "side", "side", "side", "side") } };

            RulesetData data = sockets.Derive(tiles).data;

            Assert.That(VariantsOf(data, 0).Length, Is.EqualTo(4));
            Assert.That(data.diagnostics.Any(d => d.code == DiagnosticCode.SocketsBreakSymmetry), Is.True);
            foreach (int v in VariantsOf(data, 0))
            {
                // Whatever is next to the arrow face must point its own arrow face back.
                Direction arrow = data.GetVariantOrientation(v).Apply(Direction.PosX);
                foreach (int n in Neighbours(data, v, arrow))
                {
                    Assert.That(data.GetVariantOrientation(n).Apply(Direction.PosX), Is.EqualTo(arrow.Opposite()));
                }
            }
        }

        [Test]
        public void FacesNothingFitsAreDeadEnds()
        {
            var tiles = new List<TileSpec> { Tile("block", OrientationSet.IdentityOnly, OrientationSet.IdentityOnly) };
            var sockets = new SocketRules { faceLabels = { Faces("x", "x", "", "x", "x", "x") } };

            RulesetData data = sockets.Derive(tiles).data;

            RuleDiagnostic deadEnd = data.diagnostics.Single(d => d.code == DiagnosticCode.DeadEnd);
            Assert.That(deadEnd.faces, Is.EqualTo(1 << (int)Direction.PosY | 1 << (int)Direction.NegY), "+Y has no label, so -Y has no partner either");
        }

        [Test]
        public void TilesWithoutLabelsTakeNoPart()
        {
            var tiles = new List<TileSpec> { Tile("a", OrientationSet.All, OrientationSet.All), Tile("b", OrientationSet.All, OrientationSet.All) };
            var sockets = new SocketRules { faceLabels = { AllFaces("x") } };

            RulesetData data = sockets.Derive(tiles).data;

            Assert.That(VariantsOf(data, 1), Is.Empty);
            Assert.That(data.diagnostics.Where(d => d.tile == 1), Is.Empty);
        }

        [Test]
        public void OverridesApply()
        {
            var tiles = new List<TileSpec> { Tile("a", OrientationSet.All, OrientationSet.All) };
            var sockets = new SocketRules { faceLabels = { AllFaces("x") } };
            ConnectionKey key = sockets.Derive(tiles).data.connections.Single().key;

            sockets.overrides.Add(new WeightOverride(key.Reversed, 4f));
            Connection connection = sockets.Derive(tiles).data.connections.Single();
            Assert.That(connection.weight, Is.EqualTo(4f));
            Assert.That(connection.overridden, Is.True);

            sockets.overrides[0] = new WeightOverride(new ConnectionKey("nope", 0, Direction.PosX, "nope", 0), 2f);
            Assert.That(sockets.Derive(tiles).data.diagnostics.Any(d => d.code == DiagnosticCode.OrphanedOverride), Is.True);
        }

        [Test]
        public void HashChangesWithTheLabels()
        {
            var tiles = new List<TileSpec> { Tile("a", OrientationSet.All, OrientationSet.All) };
            var sockets = new SocketRules { faceLabels = { AllFaces("x") } };
            string before = sockets.ComputeHash(tiles);
            sockets.faceLabels[0][2] = "y";
            Assert.That(sockets.ComputeHash(tiles), Is.Not.EqualTo(before));
        }

        [Test]
        public void TwoPartEdgesOnlyMeetTheirReverse()
        {
            // Left and right are as seen from outside, so facing faces read each other backwards.
            var tiles = new List<TileSpec>
            {
                Tile("a", OrientationSet.IdentityOnly, OrientationSet.All),
                Tile("reverse", OrientationSet.IdentityOnly, OrientationSet.All),
                Tile("same", OrientationSet.IdentityOnly, OrientationSet.All),
            };
            var sockets = new SocketRules { faceLabels = { Faces(posX: "x|y"), Faces(negX: "y|x"), Faces(negX: "x|y") } };

            RulesetData data = sockets.Derive(tiles).data;

            Assert.That(Neighbours(data, VariantsOf(data, 0).Single(), Direction.PosX), Is.EqualTo(new[] { VariantsOf(data, 1).Single() }));
        }

        [Test]
        public void CornerLabelsJoinEdgesLikeMarchingSquares()
        {
            // Corners per face, left to right from outside: east SE|NE, north NE|NW, west NW|SW, south SW|SE.
            var tiles = new List<TileSpec>
            {
                Tile("grass", OrientationSet.Yaw, OrientationSet.All),
                Tile("water", OrientationSet.Yaw, OrientationSet.All),
                Tile("shore", OrientationSet.Yaw, OrientationSet.IdentityOnly),   // water on the north half
            };
            var sockets = new SocketRules
            {
                faceLabels =
                {
                    Faces("G|G", "G|G", "", "", "G|G", "G|G"),
                    Faces("W|W", "W|W", "", "", "W|W", "W|W"),
                    Faces(posX: "G|W", negX: "W|G", posZ: "W|W", negZ: "G|G"),
                },
            };

            RulesetData data = sockets.Derive(tiles).data;

            int shore = data.FindVariant(2, Orientation.Identity);
            int turned = data.FindVariant(2, Orientation.RotationY90 * Orientation.RotationY90);
            Assert.That(Neighbours(data, shore, Direction.PosX), Does.Contain(shore), "the shore continues along X");
            Assert.That(Neighbours(data, shore, Direction.PosX), Has.No.Member(turned), "water would meet grass");
            Assert.That(Neighbours(data, shore, Direction.PosZ), Does.Contain(VariantsOf(data, 1).Single()));
            Assert.That(Neighbours(data, shore, Direction.NegZ), Does.Contain(VariantsOf(data, 0).Single()));
            Assert.That(Neighbours(data, shore, Direction.PosZ), Has.No.Member(VariantsOf(data, 0).Single()));
            AssertSymmetric(data);
        }

        [Test]
        public void FourPartTopsOnlyStackInTheSameRotation()
        {
            var tiles = new List<TileSpec> { Tile("column", OrientationSet.Yaw, OrientationSet.All, isInvisible: true) };
            var sockets = new SocketRules { faceLabels = { Faces(posY: "a|b|c|d|core", negY: "a|b|c|d|core") } };

            RulesetData data = sockets.Derive(tiles).data;

            int[] variants = VariantsOf(data, 0);
            Assert.That(variants.Length, Is.EqualTo(4), "the pattern tells the four rotations apart");
            foreach (int v in variants)
            {
                Assert.That(Neighbours(data, v, Direction.PosY), Is.EqualTo(new[] { v }));
            }
        }

        [Test]
        public void MirroringReversesSideLabels()
        {
            var mirror = OrientationSet.Generate(Orientation.MirrorX);
            var tiles = new List<TileSpec>
            {
                Tile("fixed", OrientationSet.IdentityOnly, OrientationSet.All),
                Tile("flippable", mirror, OrientationSet.All),
            };
            // Mirrored along X, the flippable tile's east face "p|q" ends up on the west, still south p, north q.
            var sockets = new SocketRules { faceLabels = { Faces(posX: "p|q"), Faces(posX: "p|q", negX: "r|s") } };

            RulesetData data = sockets.Derive(tiles).data;

            Assert.That(Neighbours(data, VariantsOf(data, 0).Single(), Direction.PosX), Is.EqualTo(new[] { data.FindVariant(1, Orientation.MirrorX) }));
        }

        [Test]
        public void UnsupportedPartCountsAreReported()
        {
            var tiles = new List<TileSpec> { Tile("odd", OrientationSet.All, OrientationSet.All) };
            var sockets = new SocketRules { faceLabels = { Faces(posX: "a", posY: "a|b") } };

            RuleDiagnostic invalid = sockets.Derive(tiles).data.diagnostics.Single(d => d.code == DiagnosticCode.InvalidSocket);
            Assert.That(invalid.severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(invalid.message, Does.Contain("a|b"));
        }
    }
}
