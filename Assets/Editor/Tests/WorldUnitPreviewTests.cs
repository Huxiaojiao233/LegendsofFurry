#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using LegendsOfFurry.Content.Contracts;
using NUnit.Framework;
using UnityEngine;

public sealed class WorldUnitPreviewTests
{
    private readonly List<Object> owned = new List<Object>();
    private readonly List<string> folders = new List<string>();

    [TearDown]
    public void TearDown()
    {
        for (int i = owned.Count - 1; i >= 0; i--)
        {
            if (owned[i] != null)
                Object.DestroyImmediate(owned[i]);
        }

        owned.Clear();
        for (int i = 0; i < folders.Count; i++)
        {
            if (Directory.Exists(folders[i]))
                Directory.Delete(folders[i], true);
        }

        folders.Clear();
    }

    [Test]
    public void PreviewUsesCombatAppearanceWithoutSpawningABattleUnit()
    {
        GameObject preview = Track(CombatantTokenFactory.SpawnPreview(new UnitDefinition
        {
            UnitId = "preview-slime",
            DisplayName = "史莱姆",
            TokenFrameColor = "#C15254"
        }, "slime-preview"));

        Assert.That(preview.GetComponent<Unit>(), Is.Null);
        Assert.That(preview.GetComponentInChildren<MeshRenderer>(true), Is.Not.Null);
        Assert.That(preview.GetComponentInChildren<MeshRenderer>(true).enabled, Is.True);
    }

    [Test]
    public void StreamedChunkBuildSpawnsUnitPreviewFromPlacements()
    {
        BoardHarness harness = CreateStreamedFiniteBoard(4);
        StageDefinition stage = harness.World.Stages[0];
        stage.UnitPlacements.Add(Placement("slime-1", "slime", 1, 1));
        harness.Streamer.Reload(new ChunkPosition(0, 0));
        harness.Streamer.Flush();

        Assert.That(harness.Streamer.TryGetView(new ChunkPosition(0, 0), out WorldChunkView view), Is.True);
        WorldUnitPreviewMarker[] markers = view.GetComponentsInChildren<WorldUnitPreviewMarker>(true);
        Assert.That(markers, Has.Length.EqualTo(1));
        Assert.That(markers[0].UnitId, Is.EqualTo("slime"));
        Assert.That(markers[0].InstanceId, Is.EqualTo("slime-1"));
        Assert.That(markers[0].GetComponent<Unit>(), Is.Null);
        Assert.That(markers[0].GetComponentInChildren<MeshRenderer>(true), Is.Not.Null);
        Assert.That(harness.Board.TryGetCell(1, 1, out BoardCell cell), Is.True);
        Assert.That(markers[0].transform.parent, Is.EqualTo(cell.transform));
    }

    [Test]
    public void DisabledPlacementDoesNotSpawnPreview()
    {
        BoardHarness harness = CreateStreamedFiniteBoard(4);
        harness.World.Stages[0].UnitPlacements.Add(new WorldUnitPlacementDefinition
        {
            InstanceId = "hidden",
            UnitId = "slime",
            LocalX = 0,
            LocalY = 0,
            Enabled = false
        });
        harness.Streamer.Reload(new ChunkPosition(0, 0));
        harness.Streamer.Flush();

        Assert.That(harness.Streamer.TryGetView(new ChunkPosition(0, 0), out WorldChunkView view), Is.True);
        Assert.That(view.GetComponentsInChildren<WorldUnitPreviewMarker>(true), Is.Empty);
    }

    [Test]
    public void ReloadAfterPlaceAndUndoKeepsPreviewsInSync()
    {
        string id = "unit-preview-" + Path.GetRandomFileName().Replace(".", "");
        WorldEditorSession session = WorldEditorSession.CreateFinite(id, "预览", 1, 1, 4);
        folders.Add(WorldEditorSession.WritableFolder(id));
        BoardHarness harness = CreateStreamedBoard(session.World, session.Provider);

        Assert.That(session.TryPlaceUnit(1, 0, Placement("unit-a", "slime", 1, 0), out string reason),
            Is.True, reason);
        harness.Streamer.Reload(new ChunkPosition(0, 0));
        harness.Streamer.Flush();
        Assert.That(CountPreviews(harness), Is.EqualTo(1));

        session.Commands.Undo();
        harness.Streamer.Reload(new ChunkPosition(0, 0));
        harness.Streamer.Flush();
        Assert.That(CountPreviews(harness), Is.EqualTo(0));

        session.Commands.Redo();
        harness.Streamer.Reload(new ChunkPosition(0, 0));
        harness.Streamer.Flush();
        Assert.That(CountPreviews(harness), Is.EqualTo(1));
    }

    [Test]
    public void FiniteBoardWithoutStreamerAlsoSpawnsUnitPreviews()
    {
        GameObject prefab = CreateCellPrefab();
        GameObject root = Track(new GameObject("FinitePreviewBoard"));
        root.SetActive(false);
        Transform grid = new GameObject("Grid").transform;
        grid.SetParent(root.transform, false);
        BoardGenerator board = root.AddComponent<BoardGenerator>();
        board.BindTerrainVisuals(prefab, new[]
        {
            new BoardGenerator.TerrainPrefabBinding { terrainId = WorldTerrainCatalog.Grass, prefab = prefab }
        }, grid);
        WorldDefinition world = WorldMapIO.CreateBlank("finite-unit-preview", "有限预览", 1, 1, 2);
        world.Stages[0].UnitPlacements.Add(Placement("boss-1", "taigao", 0, 0));
        board.BuildWorldBoard(world);
        root.SetActive(true);

        WorldUnitPreviewMarker[] markers = board.GetComponentsInChildren<WorldUnitPreviewMarker>(true);
        Assert.That(markers, Has.Length.EqualTo(1));
        Assert.That(markers[0].UnitId, Is.EqualTo("taigao"));
        Assert.That(markers[0].GetComponent<Unit>(), Is.Null);
        Assert.That(board.TryGetCell(0, 0, out BoardCell cell), Is.True);
        WorldUnitPreview.ClearOn(cell);
        Assert.That(board.GetComponentsInChildren<WorldUnitPreviewMarker>(true), Is.Empty);
    }

    private BoardHarness CreateStreamedFiniteBoard(int chunkSize)
    {
        WorldDefinition world = WorldMapIO.CreateBlank("stream-unit-preview", "流式预览", 1, 1, chunkSize);
        return CreateStreamedBoard(world, WorldMapIO.CreateProvider(world));
    }

    private BoardHarness CreateStreamedBoard(WorldDefinition world, IChunkProvider provider)
    {
        GameObject prefab = CreateCellPrefab();
        GameObject root = Track(new GameObject("StreamPreviewBoard"));
        root.SetActive(false);
        Transform grid = new GameObject("Grid").transform;
        grid.SetParent(root.transform, false);
        BoardGenerator board = root.AddComponent<BoardGenerator>();
        board.BindTerrainVisuals(prefab, new[]
        {
            new BoardGenerator.TerrainPrefabBinding { terrainId = WorldTerrainCatalog.Grass, prefab = prefab }
        }, grid);
        WorldChunkStreamer streamer = root.AddComponent<WorldChunkStreamer>();
        streamer.AutoCollectFromCamera = false;
        streamer.PrefetchRing = 0;
        streamer.MaxResident = 4;
        streamer.UnloadHysteresis = 0;
        board.BindChunkProvider(provider);
        board.BuildWorldBoard(world, useStreamer: true);
        root.SetActive(true);
        streamer.SetFocusChunk(new ChunkPosition(world.StartChunkX, world.StartChunkY));
        streamer.Flush();
        return new BoardHarness { Board = board, Streamer = streamer, World = world };
    }

    private GameObject CreateCellPrefab()
    {
        GameObject prefab = Track(new GameObject("CellPrefab"));
        prefab.AddComponent<BoardCell>();
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Cube";
        cube.transform.SetParent(prefab.transform, false);
        prefab.SetActive(true);
        return prefab;
    }

    private static WorldUnitPlacementDefinition Placement(string instanceId, string unitId, int x, int y)
    {
        return new WorldUnitPlacementDefinition
        {
            InstanceId = instanceId,
            UnitId = unitId,
            LocalX = x,
            LocalY = y,
            Enabled = true
        };
    }

    private static int CountPreviews(BoardHarness harness)
    {
        return harness.Board.GetComponentsInChildren<WorldUnitPreviewMarker>(true).Length;
    }

    private T Track<T>(T obj) where T : Object
    {
        if (obj != null) owned.Add(obj);
        return obj;
    }

    private struct BoardHarness
    {
        public BoardGenerator Board;
        public WorldChunkStreamer Streamer;
        public WorldDefinition World;
    }
}
#endif
