#if UNITY_EDITOR
using System.Collections.Generic;
using LegendsOfFurry.Content.Contracts;
using NUnit.Framework;
using UnityEngine;

public sealed class WorldChunkStreamerTests
{
    private readonly List<Object> owned = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        for (int i = owned.Count - 1; i >= 0; i--)
        {
            if (owned[i] != null)
                Object.DestroyImmediate(owned[i]);
        }

        owned.Clear();
    }

    [Test]
    public void QueriesWorkWithoutInstantiatingFarChunks()
    {
        BoardHarness harness = CreateInfiniteBoard(prefetch: 0, maxResident: 1, hysteresis: 0);
        WorldChunkStreamer streamer = harness.Streamer;
        BoardGenerator board = harness.Board;
        streamer.SetFocusChunk(new ChunkPosition(0, 0));
        streamer.Flush();

        Assert.That(streamer.ResidentCount, Is.EqualTo(1));
        Assert.That(board.TryGetCell(0, 0, out _), Is.True);
        Assert.That(streamer.IsResident(new ChunkPosition(20, 0)), Is.False);
        Assert.That(board.TryGetCell(40, 0, out _), Is.False);
        Assert.That(board.HasTile(40, 0), Is.True);
        Assert.That(board.GetHeight(40, 0), Is.GreaterThanOrEqualTo(WorldTerrain.MinHeight));
        Assert.That(board.GetTerrain(40, 0), Is.Not.EqualTo(WorldTerrainCatalog.Void));
    }

    [Test]
    public void OffscreenChunksLeaveNoRenderers()
    {
        BoardHarness harness = CreateInfiniteBoard(prefetch: 0, maxResident: 1, hysteresis: 0);
        WorldChunkStreamer streamer = harness.Streamer;
        streamer.SetFocusChunk(new ChunkPosition(0, 0));
        streamer.Flush();
        Assert.That(streamer.TryGetView(new ChunkPosition(0, 0), out WorldChunkView origin));
        int originRenderers = origin.GetComponentsInChildren<MeshRenderer>(true).Length;
        Assert.That(originRenderers, Is.GreaterThan(0));

        streamer.SetFocusChunk(new ChunkPosition(8, 0));
        streamer.Flush();
        Assert.That(streamer.IsResident(new ChunkPosition(0, 0)), Is.False);
        Assert.That(origin == null);

        WorldTerrainInstanceRenderer instancer = harness.Board.GetComponent<WorldTerrainInstanceRenderer>();
        Camera camera = Track(new GameObject("OffscreenCamera")).AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 4f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 80f;
        camera.transform.SetPositionAndRotation(new Vector3(1000f, 20f, 1000f), Quaternion.Euler(90f, 0f, 0f));
        instancer.RefreshFrame(camera, false);
        Assert.That(instancer.VisibleInstanceCount, Is.EqualTo(0));
        Assert.That(instancer.SubmittedBatchCount, Is.EqualTo(0));
    }

    [Test]
    [Timeout(180000)]
    public void MovingOneThousandChunksKeepsResidentCountBounded()
    {
        BoardHarness harness = CreateInfiniteBoard(prefetch: 1, maxResident: 9, hysteresis: 0);
        WorldChunkStreamer streamer = harness.Streamer;
        int peakViews = 0;
        int peakCells = 0;
        for (int x = 0; x <= 1000; x++)
        {
            streamer.SetFocusChunk(new ChunkPosition(x, 0));
            streamer.Flush();
            peakViews = Mathf.Max(peakViews, streamer.ResidentCount);
            peakCells = Mathf.Max(peakCells, harness.Board.GetComponentsInChildren<BoardCell>(true).Length);
        }

        Assert.That(peakViews, Is.LessThanOrEqualTo(9));
        Assert.That(streamer.ResidentCount, Is.LessThanOrEqualTo(9));
        Assert.That(peakCells, Is.LessThanOrEqualTo(9 * 4));
        Assert.That(streamer.IsResident(new ChunkPosition(0, 0)), Is.False);
        Assert.That(streamer.IsResident(new ChunkPosition(1000, 0)), Is.True);
        Assert.That(harness.Board.GetComponentsInChildren<WorldChunkView>(true).Length,
            Is.EqualTo(streamer.ResidentCount));
    }

    [Test]
    public void FloatingOriginShiftsRootWithoutChangingLogicalCoords()
    {
        BoardHarness harness = CreateInfiniteBoard(prefetch: 0, maxResident: 1, hysteresis: 0);
        WorldChunkStreamer streamer = harness.Streamer;
        BoardGenerator board = harness.Board;
        Vector3 origin = board.GridRoot.position;
        int heightBefore = board.GetHeight(200, 0);
        streamer.SetFocusWorldTile(200, 0);
        streamer.Flush();
        WorldFloatingOrigin floating = board.GetComponent<WorldFloatingOrigin>();
        Assert.That(floating, Is.Not.Null);
        Assert.That(board.GridRoot.position, Is.Not.EqualTo(origin));
        Assert.That(board.GetHeight(200, 0), Is.EqualTo(heightBefore));
        Assert.That(board.TryGetCell(200, 0, out BoardCell cell), Is.True);
        Assert.That(cell.Coordinate, Is.EqualTo(new Vector2Int(200, 0)));
        Assert.That(cell.transform.position.magnitude, Is.LessThan(20f));
    }

    [Test]
    public void FoundationWallsRebuildPerResidentChunkAtNegativeCoordinates()
    {
        BoardHarness harness = CreateInfiniteBoard(prefetch: 1, maxResident: 9, hysteresis: 0);
        harness.Streamer.SetFocusChunk(new ChunkPosition(-4, -3));
        harness.Streamer.Flush();

        Assert.That(harness.Streamer.IsResident(new ChunkPosition(-4, -3)), Is.True);
        Assert.DoesNotThrow(harness.Board.RebuildFoundationWalls);
        Assert.That(harness.Board.GridRoot.Find(WorldFoundationWalls.ChildName), Is.Null,
            "流式棋盘不应再创建覆盖所有区块的全局墙网格。");

        WorldChunkView[] views = harness.Board.GridRoot.GetComponentsInChildren<WorldChunkView>(true);
        Assert.That(views.Length, Is.EqualTo(harness.Streamer.ResidentCount));

        Assert.That(harness.Streamer.TryGetView(new ChunkPosition(-4, -3), out WorldChunkView focusView), Is.True);
        BoardCell[] focusCells = focusView.GetComponentsInChildren<BoardCell>(true);
        for (int i = 0; i < focusCells.Length; i++)
            focusCells[i].SetTerrainVisible(false);
        focusView.RebuildWalls(harness.Board);
        Assert.That(focusView.transform.Find(WorldFoundationWalls.ChildName), Is.Null,
            "隐藏区块的地形后不应残留地基墙。");
    }

    [Test]
    public void EditorOverlaysBuildVisibleUpFacingGeometryAndCanToggleOff()
    {
        BoardHarness harness = CreateInfiniteBoard(prefetch: 0, maxResident: 1, hysteresis: 0);
        harness.Streamer.SetFocusChunk(new ChunkPosition(0, 0));
        harness.Streamer.Flush();
        Assert.That(harness.Streamer.TryGetView(new ChunkPosition(0, 0), out WorldChunkView view), Is.True);

        view.SetEditorOverlays(harness.Board, showContours: true, showBounds: true);
        Transform bounds = view.transform.Find("EditorChunkBounds");
        Transform contours = view.transform.Find("EditorContours");
        Assert.That(bounds, Is.Not.Null);
        Assert.That(contours, Is.Not.Null);
        Mesh mesh = bounds.GetComponent<MeshFilter>().sharedMesh;
        Assert.That(mesh.vertexCount, Is.GreaterThan(0));
        Assert.That(mesh.triangles.Length, Is.GreaterThan(0));
        int[] triangles = mesh.triangles;
        Vector3[] vertices = mesh.vertices;
        Vector3 normal = Vector3.Cross(vertices[triangles[1]] - vertices[triangles[0]],
            vertices[triangles[2]] - vertices[triangles[0]]);
        Assert.That(normal.y, Is.GreaterThan(0f));
        view.SetEditorOverlays(harness.Board, showContours: false, showBounds: false);
        Assert.That(bounds.gameObject.activeSelf, Is.False);
        Assert.That(contours.gameObject.activeSelf, Is.False);
    }

    [Test]
    public void FiniteDemoStillInstantiatesEveryTile()
    {
        GameObject prefab = CreateCellPrefab();
        GameObject root = Track(new GameObject("FiniteBoard"));
        root.SetActive(false);
        Transform grid = new GameObject("Grid").transform;
        grid.SetParent(root.transform, false);
        BoardGenerator board = root.AddComponent<BoardGenerator>();
        board.BindTerrainVisuals(prefab, new[]
        {
            new BoardGenerator.TerrainPrefabBinding { terrainId = WorldTerrainCatalog.Grass, prefab = prefab }
        }, grid);
        WorldDefinition world = WorldMapIO.CreateBlank("finite-stream", "有限", 2, 2, 2);
        board.BuildWorldBoard(world);
        root.SetActive(true);
        Assert.That(board.GetComponentsInChildren<BoardCell>(true).Length, Is.EqualTo(16));
        Assert.That(board.GetComponent<WorldChunkStreamer>(), Is.Null);
        Assert.That(board.HasTile(0, 0));
        Assert.That(board.TryGetCell(0, 0, out _), Is.True);
        Assert.That(board.CanStep(new Vector2Int(0, 0), new Vector2Int(1, 0), null), Is.True);
    }

    private BoardHarness CreateInfiniteBoard(int prefetch, int maxResident, int hysteresis)
    {
        GameObject prefab = CreateCellPrefab();
        GameObject root = Track(new GameObject("StreamBoard"));
        root.SetActive(false);
        Transform grid = new GameObject("Grid").transform;
        grid.SetParent(root.transform, false);
        BoardGenerator board = root.AddComponent<BoardGenerator>();
        board.BindTerrainVisuals(prefab, new[]
        {
            new BoardGenerator.TerrainPrefabBinding { terrainId = WorldTerrainCatalog.Grass, prefab = prefab },
            new BoardGenerator.TerrainPrefabBinding { terrainId = WorldTerrainCatalog.Dirt, prefab = prefab },
            new BoardGenerator.TerrainPrefabBinding { terrainId = WorldTerrainCatalog.Sand, prefab = prefab },
            new BoardGenerator.TerrainPrefabBinding { terrainId = WorldTerrainCatalog.Stone, prefab = prefab },
            new BoardGenerator.TerrainPrefabBinding { terrainId = WorldTerrainCatalog.Water, prefab = prefab },
            new BoardGenerator.TerrainPrefabBinding { terrainId = WorldTerrainCatalog.Forest, prefab = prefab },
            new BoardGenerator.TerrainPrefabBinding { terrainId = WorldTerrainCatalog.Road, prefab = prefab }
        }, grid);
        WorldChunkStreamer streamer = root.AddComponent<WorldChunkStreamer>();
        streamer.AutoCollectFromCamera = false;
        streamer.PrefetchRing = prefetch;
        streamer.MaxResident = maxResident;
        streamer.UnloadHysteresis = hysteresis;
        WorldDefinition world = WorldMapIO.CreateInfinite("stream-test", "流式", 4242, 2);
        board.BuildWorldBoard(world);
        root.SetActive(true);
        return new BoardHarness { Board = board, Streamer = streamer };
    }

    private GameObject CreateCellPrefab()
    {
        GameObject prefab = Track(new GameObject("CellPrefab"));
        prefab.AddComponent<BoardCell>();
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Cube";
        cube.transform.SetParent(prefab.transform, false);
        cube.AddComponent<TerrainBatchSource>();
        prefab.SetActive(true);
        return prefab;
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
    }
}
#endif
