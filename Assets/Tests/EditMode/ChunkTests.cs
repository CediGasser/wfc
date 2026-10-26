using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Tests
{
    /// <summary>Creates tile definitions, tile sets and chunks for tests and destroys them afterwards.</summary>
    internal sealed class TestObjects
    {
        private readonly List<Object> created = new List<Object>();

        public T Track<T>(T obj) where T : Object
        {
            created.Add(obj);
            return obj;
        }

        /// <summary>A tile definition, with a cube as model unless <paramref name="withModel"/> is false.</summary>
        public TileDefinition Definition(string name, OrientationPreset allowed = OrientationPreset.All, bool withModel = true)
        {
            var definition = Track(ScriptableObject.CreateInstance<TileDefinition>());
            definition.name = name;
            definition.SetAllowedOrientations(allowed);
            if (withModel)
            {
                GameObject model = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
                model.name = name;
                definition.SetModel(model);
            }
            return definition;
        }

        public TileSet TileSet(params TileDefinition[] definitions)
        {
            var set = Track(ScriptableObject.CreateInstance<TileSet>());
            foreach (TileDefinition definition in definitions)
            {
                set.Append(definition);
            }
            return set;
        }

        public Chunk Chunk(TileSet set, int3 size, bool withView = false)
        {
            var gameObject = Track(new GameObject("chunk"));
            var chunk = gameObject.AddComponent<Chunk>();
            chunk.TileSet = set;
            chunk.Resize(size, int3.zero);
            if (withView)
            {
                gameObject.AddComponent<ChunkView>();
            }
            return chunk;
        }

        public void DestroyAll()
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
    }

    public class ChunkTests
    {
        private readonly TestObjects objects = new TestObjects();

        [TearDown]
        public void TearDown() => objects.DestroyAll();

        [Test]
        public void ChangesAreReported()
        {
            Chunk chunk = objects.Chunk(objects.TileSet(objects.Definition("a")), new int3(2, 2, 2));
            var changed = new List<int>();
            var any = new List<Chunk>();
            chunk.Changed += changed.Add;
            void OnAny(Chunk c) => any.Add(c);
            Chunk.AnyChanged += OnAny;
            try
            {
                chunk.SetCell(new int3(1, 0, 1), 0, Orientation.RotationY90);
                chunk.ClearCells();
            }
            finally
            {
                Chunk.AnyChanged -= OnAny;
            }

            Assert.That(changed, Is.EqualTo(new[] { chunk.Cells.ToIndex(new int3(1, 0, 1)), Chunk.AllCells }));
            Assert.That(any, Is.EqualTo(new[] { chunk, chunk }));
        }

        [Test]
        public void ResizeKeepsCellsAndDropsTheGeneratedLayer()
        {
            Chunk chunk = objects.Chunk(objects.TileSet(objects.Definition("a")), new int3(3, 1, 1));
            chunk.SetCell(new int3(2, 0, 0), 0, Orientation.Identity);
            chunk.GetOrCreateGenerated().Set(0, 0, Orientation.Identity);

            chunk.Resize(new int3(4, 2, 1), new int3(1, 1, 0));

            Assert.That(chunk.Size, Is.EqualTo(new int3(4, 2, 1)));
            Assert.That(chunk.Cells.GetTile(chunk.Cells.ToIndex(new int3(3, 1, 0))), Is.EqualTo(0));
            Assert.That(chunk.Generated, Is.Null);
        }

        [Test]
        public void ShownTilePrefersAuthoredCells()
        {
            Chunk chunk = objects.Chunk(objects.TileSet(objects.Definition("a"), objects.Definition("b")), new int3(2, 1, 1));
            chunk.SetCell(0, 0, Orientation.Identity);
            ChunkData generated = chunk.GetOrCreateGenerated();
            generated.Set(0, 1, Orientation.Identity);
            generated.Set(1, 1, Orientation.MirrorX);

            Assert.That(chunk.GetShownTile(0, true, out _, out bool firstGenerated), Is.EqualTo(0));
            Assert.That(firstGenerated, Is.False);
            Assert.That(chunk.GetShownTile(1, true, out Orientation orientation, out bool secondGenerated), Is.EqualTo(1));
            Assert.That((orientation, secondGenerated), Is.EqualTo((Orientation.MirrorX, true)));
            Assert.That(chunk.GetShownTile(1, false, out _, out _), Is.EqualTo(ChunkData.Empty));
        }

        [Test]
        public void KeepGeneratedOnlyFillsEmptyCells()
        {
            Chunk chunk = objects.Chunk(objects.TileSet(objects.Definition("a"), objects.Definition("b")), new int3(3, 1, 1));
            chunk.SetCell(0, 0, Orientation.Identity);
            ChunkData generated = chunk.GetOrCreateGenerated();
            for (int cell = 0; cell < 2; cell++)
            {
                generated.Set(cell, 1, Orientation.Identity);
            }

            Assert.That(chunk.KeepGenerated(), Is.EqualTo(1));
            Assert.That(chunk.Cells.tiles, Is.EqualTo(new[] { 0, 1, ChunkData.Empty }));
            Assert.That(chunk.Generated, Is.Null);
        }

        [Test]
        public void GeneratedLayerIsNotSaved()
        {
            Chunk chunk = objects.Chunk(objects.TileSet(objects.Definition("a")), new int3(2, 1, 1));
            chunk.SetCell(0, 0, Orientation.Identity);
            chunk.GetOrCreateGenerated().Set(1, 0, Orientation.Identity);

            // Instantiate copies exactly what would be saved.
            Chunk copy = objects.Track(Object.Instantiate(chunk.gameObject)).GetComponent<Chunk>();

            Assert.That(copy.Cells.ContentEquals(chunk.Cells), Is.True);
            Assert.That(copy.Generated, Is.Null);
        }

        [Test]
        public void AssetRoundTrip()
        {
            TileSet set = objects.TileSet(objects.Definition("a"), objects.Definition("b"));
            Chunk chunk = objects.Chunk(set, new int3(2, 2, 1));
            chunk.SetCell(new int3(1, 1, 0), 1, Orientation.RotationY90);
            var asset = objects.Track(ScriptableObject.CreateInstance<ChunkAsset>());
            chunk.SaveTo(asset);
            chunk.ClearCells();
            Assert.That(asset.Cells.CountNonEmpty(), Is.EqualTo(1), "the asset holds a copy");

            Chunk other = objects.Chunk(null, new int3(5, 5, 5));
            Assert.That(other.LoadFrom(asset), Is.True);
            Assert.That(other.TileSet, Is.SameAs(set), "a chunk without a tile set takes the asset's");
            Assert.That(other.Cells.ContentEquals(asset.Cells), Is.True);
        }

        [Test]
        public void LoadingMatchesTilesByDefinition()
        {
            TileDefinition a = objects.Definition("a"), b = objects.Definition("b"), c = objects.Definition("c");
            Chunk source = objects.Chunk(objects.TileSet(a, b), new int3(2, 1, 1));
            source.SetCell(0, 1, Orientation.Identity);
            var asset = objects.Track(ScriptableObject.CreateInstance<ChunkAsset>());
            source.SaveTo(asset);

            Chunk reordered = objects.Chunk(objects.TileSet(c, b), new int3(1, 1, 1));
            Assert.That(reordered.LoadFrom(asset), Is.True);
            Assert.That(reordered.Cells.GetTile(0), Is.EqualTo(1), "b is in slot 1 of both sets");

            Chunk lacking = objects.Chunk(objects.TileSet(a, c), new int3(1, 1, 1));
            var missing = new List<TileDefinition>();
            Assert.That(lacking.LoadFrom(asset, missing), Is.False);
            Assert.That(missing, Is.EqualTo(new[] { b }));
            Assert.That(lacking.Size, Is.EqualTo(new int3(1, 1, 1)), "nothing changes when tiles are missing");
        }

        [Test]
        public void UndoRestoresCells()
        {
            Chunk chunk = objects.Chunk(objects.TileSet(objects.Definition("a")), new int3(2, 1, 1));
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(chunk, "Place Tile");
            chunk.SetCell(1, 0, Orientation.Identity);
            Undo.IncrementCurrentGroup();

            Undo.PerformUndo();

            Assert.That(chunk.Cells.IsEmpty(1), Is.True);
        }

        [Test]
        public void TileSetSlotsAreStable()
        {
            TileDefinition a = objects.Definition("a"), b = objects.Definition("b"), c = objects.Definition("c");
            TileSet set = objects.TileSet(a, b);
            set.RemoveAt(0);
            Assert.That(set[0], Is.Null);
            Assert.That(set.IndexOf(b), Is.EqualTo(1));
            Assert.That(set.Append(c), Is.EqualTo(2), "removed slots aren't reused");
            Assert.That(set.Append(b), Is.EqualTo(1), "a tile that's already in the set keeps its slot");
            Assert.That(set[7], Is.Null);
        }
    }

    public class ChunkViewTests
    {
        private readonly TestObjects objects = new TestObjects();

        [TearDown]
        public void TearDown() => objects.DestroyAll();

        private static Transform[] Shown(ChunkView view)
        {
            return view.Container == null ? new Transform[0] : view.Container.Cast<Transform>().ToArray();
        }

        [Test]
        public void ShowsTheModelsOfFilledCells()
        {
            TileSet set = objects.TileSet(objects.Definition("block"), objects.Definition("air", withModel: false));
            Chunk chunk = objects.Chunk(set, new int3(3, 1, 1), withView: true);
            chunk.SetCell(0, 0, Orientation.Identity);
            chunk.SetCell(1, 1, Orientation.Identity);
            var view = chunk.GetComponent<ChunkView>();

            Transform[] shown = Shown(view);
            Assert.That(shown.Length, Is.EqualTo(1), "air has no model, and the third cell is empty");
            Assert.That(shown[0].localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(view.Container.gameObject.hideFlags, Is.EqualTo(HideFlags.HideAndDontSave));
            Assert.That(shown[0].GetComponentsInChildren<Transform>(true).All(t => t.gameObject.hideFlags == HideFlags.HideAndDontSave), Is.True);
        }

        [Test]
        public void FollowsCellChanges()
        {
            Chunk chunk = objects.Chunk(objects.TileSet(objects.Definition("block")), new int3(3, 1, 1), withView: true);
            var view = chunk.GetComponent<ChunkView>();

            chunk.SetCell(2, 0, Orientation.RotationY90);
            Assert.That(Shown(view).Single().localRotation.eulerAngles.y, Is.EqualTo(90f).Within(1e-3f));
            chunk.SetCell(2, 0, Orientation.Identity);
            Assert.That(Shown(view).Single().localRotation, Is.EqualTo(Quaternion.identity));
            chunk.ClearCell(2);
            Assert.That(Shown(view), Is.Empty);
        }

        [Test]
        public void ShowsGeneratedCellsUnderAuthoredOnes()
        {
            Chunk chunk = objects.Chunk(objects.TileSet(objects.Definition("a"), objects.Definition("b")), new int3(2, 1, 1), withView: true);
            var view = chunk.GetComponent<ChunkView>();
            chunk.SetCell(0, 0, Orientation.Identity);
            ChunkData generated = chunk.GetOrCreateGenerated();
            generated.Set(0, 1, Orientation.Identity);
            generated.Set(1, 1, Orientation.Identity);
            chunk.NotifyGenerated(Chunk.AllCells);

            Assert.That(Shown(view).Select(t => t.name), Is.EquivalentTo(new[] { "a int3(0, 0, 0)", "b int3(1, 0, 0)" }));
            view.ShowGenerated = false;
            Assert.That(Shown(view).Length, Is.EqualTo(1));
            view.ShowGenerated = true;
            chunk.ClearGenerated();
            Assert.That(Shown(view).Length, Is.EqualTo(1));
        }

        [Test]
        public void DuplicatesDontKeepCopiedVisuals()
        {
            Chunk chunk = objects.Chunk(objects.TileSet(objects.Definition("block")), new int3(2, 1, 1), withView: true);
            chunk.SetCell(0, 0, Orientation.Identity);

            GameObject copy = objects.Track(Object.Instantiate(chunk.gameObject));

            Assert.That(copy.transform.childCount, Is.EqualTo(1), "one container, not the copied one as well");
            Assert.That(Shown(copy.GetComponent<ChunkView>()).Length, Is.EqualTo(1));
        }

        [Test]
        public void DisablingRemovesTheVisuals()
        {
            Chunk chunk = objects.Chunk(objects.TileSet(objects.Definition("block")), new int3(1, 1, 1), withView: true);
            chunk.SetCell(0, 0, Orientation.Identity);
            var view = chunk.GetComponent<ChunkView>();

            view.enabled = false;
            Assert.That(chunk.transform.childCount, Is.Zero);
            view.enabled = true;
            Assert.That(Shown(view).Length, Is.EqualTo(1));
        }
    }

    public class ChunkRaycastTests
    {
        [Test]
        public void HitsTheFirstSolidCell()
        {
            Assert.That(ChunkRaycast.Raycast(new int3(5, 1, 1), new Vector3(-3f, 0f, 0f), Vector3.right, cell => cell.x >= 3, out ChunkRaycast.Hit hit), Is.True);
            Assert.That(hit.solid, Is.True);
            Assert.That(hit.cell, Is.EqualTo(new int3(3, 0, 0)));
            Assert.That(hit.face, Is.EqualTo(Direction.NegX));
        }

        [Test]
        public void FirstCellIsEnteredThroughTheBoxFace()
        {
            ChunkRaycast.Raycast(new int3(3, 3, 3), new Vector3(1f, 10f, 1f), Vector3.down, _ => true, out ChunkRaycast.Hit hit);
            Assert.That(hit.cell, Is.EqualTo(new int3(1, 2, 1)));
            Assert.That(hit.face, Is.EqualTo(Direction.PosY));
            Assert.That(hit.distance, Is.EqualTo(7.5f).Within(1e-4f));
        }

        [Test]
        public void WithoutSolidCellsTheFarWallIsReturned()
        {
            Assert.That(ChunkRaycast.Raycast(new int3(5, 3, 1), new Vector3(2f, 6f, 0f), Vector3.down, _ => false, out ChunkRaycast.Hit hit), Is.True);
            Assert.That(hit.solid, Is.False);
            Assert.That(hit.cell, Is.EqualTo(new int3(2, 0, 0)));
            Assert.That(hit.face, Is.EqualTo(Direction.PosY));
        }

        [Test]
        public void RaysPastTheBoxMiss()
        {
            Assert.That(ChunkRaycast.Raycast(new int3(2, 2, 2), new Vector3(5f, 5f, 5f), Vector3.up, _ => true, out _), Is.False);
            Assert.That(ChunkRaycast.Raycast(new int3(2, 2, 2), new Vector3(-3f, 0f, 0f), Vector3.left, _ => true, out _), Is.False);
        }

        [Test]
        public void DiagonalRaysVisitNeighbouringCellsOnce()
        {
            var visited = new List<int3>();
            ChunkRaycast.Raycast(new int3(6, 6, 6), new Vector3(-1f, -0.7f, -0.2f), new Vector3(1f, 0.8f, 0.6f), cell =>
            {
                visited.Add(cell);
                return false;
            }, out _);

            Assert.That(visited.Count, Is.GreaterThan(6));
            Assert.That(visited.Distinct().Count(), Is.EqualTo(visited.Count));
            for (int i = 1; i < visited.Count; i++)
            {
                int3 step = math.abs(visited[i] - visited[i - 1]);
                Assert.That(math.csum(step), Is.EqualTo(1), $"{visited[i - 1]} → {visited[i]}");
            }
        }

        [Test]
        public void RaysStartingInsideWork()
        {
            Assert.That(ChunkRaycast.Raycast(new int3(4, 1, 1), new Vector3(0.2f, 0f, 0f), Vector3.right, cell => cell.x == 2, out ChunkRaycast.Hit hit), Is.True);
            Assert.That(hit.cell, Is.EqualTo(new int3(2, 0, 0)));
            Assert.That(hit.face, Is.EqualTo(Direction.NegX));
        }
    }
}
