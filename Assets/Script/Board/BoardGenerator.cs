using System;
using System.Collections.Generic;
using LegendsOfFurry.Content.Contracts;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 负责生成棋盘、记录所有真实存在的格子与占格，并提供安全的查询接口。
/// boardMap 中数值为 0 的位置代表空洞，不会生成格子。
/// 每种地形使用独立 Prefab，不再靠染色区分。
/// </summary>
[DefaultExecutionOrder(-100)]
public class BoardGenerator : MonoBehaviour
{
    [SerializeField] private float RandomMapProbability = 0.9f;

    [Header("棋盘设置")]
    [FormerlySerializedAs("cellPrefab")]
    [SerializeField] private GameObject fallbackCellPrefab;
    [SerializeField] private TerrainPrefabBinding[] terrainPrefabs;

    [SerializeField] private int width = 10;
    [SerializeField] private int height = 8;

    [Header("格子设置")]
    [SerializeField] private float cellSize = 1f;
    [SerializeField] private float gap = 0f;

    [Header("节点")]
    [SerializeField] private Transform gridRoot;

    private static readonly Vector2Int[] FourDirections =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left
    };

    private int originWorldX;
    private int originWorldZ;
    private WorldDefinition worldSource;
    private Func<int, int, bool> movementFilter;
    private readonly Dictionary<long, TileRecord> tiles = new Dictionary<long, TileRecord>();
    private readonly Dictionary<long, BoardCell> cellLookup = new Dictionary<long, BoardCell>();
    private readonly Dictionary<Vector2Int, Unit> occupants =
        new Dictionary<Vector2Int, Unit>();
    private readonly List<GameObject> decorations = new List<GameObject>();
    private Dictionary<string, GameObject> terrainPrefabLookup;
    private bool foundationWallsDirty;
    private bool usesChunkStreaming;
    private IChunkProvider chunkProvider;

    public int Width => width;
    public int Height => height;
    public float Spacing => cellSize + gap;
    public float HeightStep => WorldTerrain.StepY;
    public int OriginWorldX => originWorldX;
    public int OriginWorldZ => originWorldZ;

    /// <summary>世界格子中心的世界坐标。棋盘外的格子按同样间距外推，方便云海铺边。</summary>
    public Vector3 EvaluateTilePosition(int worldX, int worldZ)
    {
        if (TryGetCell(worldX, worldZ, out BoardCell cell) && cell != null)
            return cell.transform.position;
        float spacing = Spacing;
        int mapWidth = Width;
        int mapHeight = Height;
        float posX = (worldX - (originWorldX + (mapWidth - 1) / 2f)) * spacing;
        float posZ = (worldZ - (originWorldZ + (mapHeight - 1) / 2f)) * spacing;
        Transform root = gridRoot != null ? gridRoot : transform;
        return root.TransformPoint(new Vector3(posX, 0f, posZ));
    }

    public Transform GridRoot => gridRoot != null ? gridRoot : transform;
    public WorldDefinition WorldSource => worldSource;
    public IChunkProvider ChunkProvider => chunkProvider;
    public bool IsWorldBoard { get; private set; }

    /// <summary>测试和编辑器工具绑定地形 Prefab。root 为空时仍用现有 gridRoot。</summary>
    public void BindTerrainVisuals(GameObject fallback, TerrainPrefabBinding[] bindings, Transform root = null)
    {
        fallbackCellPrefab = fallback;
        terrainPrefabs = bindings;
        if (root != null) gridRoot = root;
        RebuildTerrainPrefabLookup();
    }

    private void GenerateRandomBoard()
    {
        tiles.Clear();
        usesChunkStreaming = false;
        originWorldX = 0;
        originWorldZ = 0;
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                if (UnityEngine.Random.value >= RandomMapProbability) continue;
                tiles[TileKey(x, z)] = new TileRecord
                {
                    Exists = true,
                    Height = 0,
                    TerrainId = WorldTerrainCatalog.Grass,
                    StageId = string.Empty
                };
            }
        }

        IsWorldBoard = false;
        GenerateBoard();
    }

    private void Awake()
    {
        RebuildTerrainPrefabLookup();
        if (cellLookup.Count > 0) return;
        if (TryGenerateRunWorld()) return;
        GenerateRandomBoard();
    }

    private void OnValidate()
    {
        RebuildTerrainPrefabLookup();
    }

    private void RebuildTerrainPrefabLookup()
    {
        terrainPrefabLookup = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        if (terrainPrefabs == null) return;
        for (int i = 0; i < terrainPrefabs.Length; i++)
        {
            TerrainPrefabBinding binding = terrainPrefabs[i];
            if (binding == null || string.IsNullOrWhiteSpace(binding.terrainId) || binding.prefab == null)
                continue;
            terrainPrefabLookup[binding.terrainId] = binding.prefab;
        }
    }

    private GameObject ResolveCellPrefab(string terrainId)
    {
        if (string.IsNullOrWhiteSpace(terrainId))
            terrainId = WorldTerrainCatalog.Grass;
        if (terrainPrefabLookup != null &&
            terrainPrefabLookup.TryGetValue(terrainId, out GameObject prefab) &&
            prefab != null)
            return prefab;
        return fallbackCellPrefab;
    }

    /// <summary>冒险进行中时把整张大地图拼进同一块棋盘，不再随机挖洞。</summary>
    private bool TryGenerateRunWorld()
    {
        if (!RunSession.HasActive) return false;
        if (!WorldCatalog.TryGet(RunSession.Current.worldId, out WorldDefinition world))
            world = WorldCatalog.Default;
        if (world == null) return false;
        chunkProvider = WorldMapIO.CreateProvider(world);
        BuildWorldBoard(world);
        return true;
    }

    /// <summary>按关卡格子生成连续地形；空洞只出现在没有关卡定义或地形为空的格子。</summary>
    public void BindChunkProvider(IChunkProvider provider) => chunkProvider = provider;

    public void BuildWorldBoard(WorldDefinition world, bool useStreamer = false)
    {
        ClearWorldVisuals();
        worldSource = world;
        usesChunkStreaming = world.IsInfinite || useStreamer;
        chunkProvider ??= world != null ? WorldMapIO.CreateProvider(world) : null;
        tiles.Clear();
        int tw = Mathf.Max(1, world.TerrainWidth);
        int th = Mathf.Max(1, world.TerrainHeight);
        originWorldX = world.IsInfinite ? 0 : world.OriginWorldX;
        originWorldZ = world.IsInfinite ? 0 : world.OriginWorldY;
        width = world.IsInfinite ? tw : Mathf.Max(1, world.WorldTerrainWidth);
        height = world.IsInfinite ? th : Mathf.Max(1, world.WorldTerrainHeight);
        if (world.Stages != null)
        {
            for (int i = 0; i < world.Stages.Count; i++)
                AbsorbStage(world.Stages[i], tw, th);
        }

        IsWorldBoard = true;
        WorldChunkStreamer streamer = GetComponent<WorldChunkStreamer>();
        if (usesChunkStreaming)
        {
            if (streamer == null) streamer = gameObject.AddComponent<WorldChunkStreamer>();
            streamer.Bind(this, chunkProvider);
            streamer.SetFocusChunk(new ChunkPosition(world.StartChunkX, world.StartChunkY));
            streamer.Flush();
            RebuildTerrainInstanceRenderer();
            return;
        }

        GenerateBoard();
        SpawnDecorations();
    }

    /// <summary>切换编辑世界前释放旧的有限棋盘与流式区块，避免残留格子和坐标偏移。</summary>
    public void ClearWorldVisuals()
    {
        WorldChunkStreamer streamer = GetComponent<WorldChunkStreamer>();
        streamer?.ResetStream();
        ClearGeneratedCells();
        tiles.Clear();
    }

    public void AbsorbStage(StageDefinition stage, int tw, int th)
    {
        if (stage == null || !stage.Enabled) return;
        tw = Mathf.Max(1, tw);
        th = Mathf.Max(1, th);
        stage.EnsureGrids(tw, th);
        for (int localY = 0; localY < th; localY++)
        {
            for (int localX = 0; localX < tw; localX++)
            {
                int worldX = stage.GridX * tw + localX;
                int worldZ = stage.GridY * th + localY;
                string terrainId = stage.TerrainAt(localX, localY, tw, th);
                tiles[TileKey(worldX, worldZ)] = new TileRecord
                {
                    Exists = terrainId != WorldTerrainCatalog.Void,
                    Height = stage.HeightAt(localX, localY, tw, th),
                    TerrainId = terrainId,
                    StageId = stage.StageId
                };
            }
        }
    }

    public bool EnsureChunkData(ChunkPosition position, out StageDefinition stage)
    {
        stage = null;
        if (chunkProvider == null || !chunkProvider.TryGetChunk(position, out stage) || stage == null)
            return false;
        int tw = worldSource != null ? Mathf.Max(1, worldSource.TerrainWidth) : 10;
        int th = worldSource != null ? Mathf.Max(1, worldSource.TerrainHeight) : 10;
        AbsorbStage(stage, tw, th);
        return true;
    }

    public void SetMovementFilter(Func<int, int, bool> filter)
    {
        movementFilter = filter;
    }

    public int GetHeight(int x, int z)
    {
        if (!TryGetTile(x, z, out TileRecord tile))
            return 0;
        return tile.Height;
    }

    public string GetTerrain(int x, int z)
    {
        if (!TryGetTile(x, z, out TileRecord tile) || string.IsNullOrEmpty(tile.TerrainId))
            return WorldTerrainCatalog.Grass;
        return tile.TerrainId;
    }

    public bool HasTile(int x, int z) => TryGetTile(x, z, out TileRecord tile) && tile.Exists;

    /// <summary>四方向走一步：存在格子、可走地形、高度差不超过 1、未被占用，并满足当前探索/战斗范围。</summary>
    public bool CanStep(Vector2Int from, Vector2Int to, Unit mover, Vector2Int? goal = null)
    {
        if (movementFilter != null && !movementFilter(to.x, to.y)) return false;
        if (!HasTile(to.x, to.y) && !TryGetCell(to.x, to.y, out _)) return false;
        if (!WorldTerrainCatalog.CanEnter(GetTerrain(to.x, to.y), mover)) return false;
        if (Mathf.Abs(GetHeight(from.x, from.y) - GetHeight(to.x, to.y)) > 1) return false;
        if (goal.HasValue && to == goal.Value) return true;
        return !IsOccupied(to.x, to.y, mover);
    }

    /// <summary>根据稀疏瓦片实例化当前已有数据的棋盘格。</summary>
    public void GenerateBoard()
    {
        ClearGeneratedCells();
        RebuildTerrainPrefabLookup();
        if (fallbackCellPrefab == null && (terrainPrefabs == null || terrainPrefabs.Length == 0))
        {
            Debug.LogError("棋盘地形预制件为空，无法生成棋盘。", this);
            return;
        }

        Transform parent = gridRoot != null ? gridRoot : transform;
        foreach (KeyValuePair<long, TileRecord> pair in tiles)
        {
            if (!pair.Value.Exists) continue;
            DecodeTileKey(pair.Key, out int worldX, out int worldZ);
            SpawnCell(worldX, worldZ, pair.Value, parent);
        }

        RebuildFoundationWalls();
        RebuildTerrainInstanceRenderer();
    }

    public Vector3 CellLocalPosition(int worldX, int worldZ, int tileHeight)
    {
        float spacing = Spacing;
        float posX = (worldX - (originWorldX + (width - 1) / 2f)) * spacing;
        float posZ = (worldZ - (originWorldZ + (height - 1) / 2f)) * spacing;
        return new Vector3(posX, tileHeight * HeightStep, posZ);
    }

    public BoardCell SpawnCell(int worldX, int worldZ, TileRecord tile, Transform parent)
    {
        GameObject prefab = ResolveCellPrefab(tile.TerrainId);
        if (prefab == null)
        {
            Debug.LogError($"地形 {tile.TerrainId} 没有 Prefab，且 fallback 也为空。", this);
            return null;
        }

        GameObject cellObject = Instantiate(prefab, parent);
        cellObject.transform.localPosition = CellLocalPosition(worldX, worldZ, tile.Height);
        cellObject.transform.localRotation = Quaternion.identity;
        BoardCell cell = cellObject.GetComponent<BoardCell>();
        if (cell == null) cell = cellObject.GetComponentInChildren<BoardCell>();
        if (cell == null) cell = cellObject.AddComponent<BoardCell>();
        cell.Initialize(worldX, worldZ, tile.StageId, tile.Height, tile.TerrainId);
        cellLookup[TileKey(worldX, worldZ)] = cell;
        return cell;
    }

    public void UnregisterCellsInChunk(ChunkPosition chunk, int tw, int th)
    {
        for (int y = 0; y < th; y++)
        {
            for (int x = 0; x < tw; x++)
            {
                int worldX = chunk.X * tw + x;
                int worldZ = chunk.Y * th + y;
                long key = TileKey(worldX, worldZ);
                cellLookup.Remove(key);
                tiles.Remove(key);
            }
        }
    }

    public bool TryPeekTile(int x, int z, out TileRecord tile) => tiles.TryGetValue(TileKey(x, z), out tile);

    public int TileCount => tiles.Count;

    private void ClearGeneratedCells()
    {
        occupants.Clear();
        Transform parent = gridRoot != null ? gridRoot : transform;
        BoardCell[] existing = parent.GetComponentsInChildren<BoardCell>(true);
        for (int i = 0; i < existing.Length; i++)
        {
            if (existing[i] == null) continue;
            DestroyImmediate(existing[i].gameObject);
        }

        cellLookup.Clear();
        for (int i = 0; i < decorations.Count; i++)
        {
            if (decorations[i] == null) continue;
            DestroyImmediate(decorations[i]);
        }

        decorations.Clear();
        WorldFoundationWalls.Clear(parent);
        WorldChunkView[] views = parent.GetComponentsInChildren<WorldChunkView>(true);
        for (int i = 0; i < views.Length; i++)
        {
            if (views[i] == null) continue;
            DestroyImmediate(views[i].gameObject);
        }
    }

    private void RebuildTerrainInstanceRenderer()
    {
        WorldTerrainInstanceRenderer instancer = GetComponent<WorldTerrainInstanceRenderer>();
        if (instancer == null)
            instancer = gameObject.AddComponent<WorldTerrainInstanceRenderer>();
        instancer.Rebuild();
    }

    private void LateUpdate()
    {
        if (!foundationWallsDirty) return;
        RebuildFoundationWalls();
    }

    /// <summary>地块显隐、激活或生成变化后标记地基墙，本帧结束按当前可见格子重算一次。</summary>
    public void NotifyTilesChanged()
    {
        if (!isActiveAndEnabled) return;
        foundationWallsDirty = true;
    }

    /// <summary>按当前仍显示的格子重算地基墙。隐藏或关掉的地块不画墙。</summary>
    public void RebuildFoundationWalls()
    {
        foundationWallsDirty = false;
        if (tiles.Count == 0) return;
        Transform root = gridRoot != null ? gridRoot : transform;
        if (usesChunkStreaming)
        {
            // 流式世界的逻辑坐标可远离原点，不能塞进 width x height 的有限数组。
            // 每个驻留区块拥有自己的局部墙网格，重建它们也能避免与全局墙重复绘制。
            WorldFoundationWalls.Clear(root);
            WorldChunkView[] views = root.GetComponentsInChildren<WorldChunkView>(true);
            for (int i = 0; i < views.Length; i++)
            {
                if (views[i] != null && views[i].gameObject.activeInHierarchy)
                    views[i].RebuildWalls(this);
            }

            return;
        }

        int mapHeight = Mathf.Max(1, height);
        int mapWidth = Mathf.Max(1, width);
        int[,] map = new int[mapHeight, mapWidth];
        int[,] heights = new int[mapWidth, mapHeight];
        foreach (KeyValuePair<long, BoardCell> pair in cellLookup)
        {
            BoardCell cell = pair.Value;
            if (cell == null || !cell.gameObject.activeInHierarchy || !cell.TerrainVisible) continue;
            if (!TryMapWorld(cell.Coordinate.x, cell.Coordinate.y, out int ix, out int iz)) continue;
            map[iz, ix] = 1;
            heights[ix, iz] = cell.Height;
        }

        WorldFoundationWalls.Rebuild(root, map, heights, Spacing, HeightStep);
    }

    public BoardCell GetCell(int x, int z)
    {
        if (cellLookup.Count == 0)
        {
            Debug.LogError("棋盘还没有初始化！");
            return null;
        }

        if (!TryGetCell(x, z, out BoardCell cell))
            Debug.LogWarning($"位置 ({x}, {z}) 不存在格子");
        return cell;
    }

    /// <summary>安静地尝试获取格子。范围计算和寻路应优先使用此接口。</summary>
    public bool TryGetCell(int x, int z, out BoardCell cell)
    {
        return cellLookup.TryGetValue(TileKey(x, z), out cell) && cell != null;
    }

    public bool TryMapWorld(int worldX, int worldZ, out int indexX, out int indexZ)
    {
        indexX = worldX - originWorldX;
        indexZ = worldZ - originWorldZ;
        if (worldSource != null && worldSource.IsInfinite)
            return HasTile(worldX, worldZ);
        return indexX >= 0 && indexZ >= 0 && indexX < width && indexZ < height;
    }

    public bool IsOccupied(int x, int z, Unit ignore = null)
    {
        if (!occupants.TryGetValue(new Vector2Int(x, z), out Unit occupant) ||
            occupant == null)
        {
            return false;
        }

        return occupant != ignore;
    }

    public bool TryGetOccupant(int x, int z, out Unit occupant)
    {
        return occupants.TryGetValue(new Vector2Int(x, z), out occupant) &&
            occupant != null;
    }

    public void SetOccupant(Unit unit, Vector2Int position, Vector2Int? previous)
    {
        if (unit == null)
        {
            return;
        }

        if (previous.HasValue)
        {
            Vector2Int oldPosition = previous.Value;
            if (occupants.TryGetValue(oldPosition, out Unit current) && current == unit)
            {
                occupants.Remove(oldPosition);
            }
        }
        else
        {
            RemoveOccupant(unit);
        }

        occupants[position] = unit;
    }

    public void RemoveOccupant(Unit unit)
    {
        if (unit == null)
        {
            return;
        }

        List<Vector2Int> keysToRemove = null;
        foreach (KeyValuePair<Vector2Int, Unit> pair in occupants)
        {
            if (pair.Value == unit)
            {
                keysToRemove ??= new List<Vector2Int>();
                keysToRemove.Add(pair.Key);
            }
        }

        if (keysToRemove == null)
        {
            return;
        }

        for (int i = 0; i < keysToRemove.Count; i++)
        {
            occupants.Remove(keysToRemove[i]);
        }
    }

    /// <summary>从偏好坐标开始，找最近的空格子。随机地图导致出生点是空洞时使用。</summary>
    public bool TryFindNearestFreeCell(
        Vector2Int preferred,
        Unit ignore,
        out Vector2Int result)
    {
        result = preferred;

        if (cellLookup.Count == 0)
        {
            return false;
        }

        if (IsFreeCell(preferred.x, preferred.y, ignore))
        {
            return true;
        }

        Queue<Vector2Int> frontier = new Queue<Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        frontier.Enqueue(preferred);
        visited.Add(preferred);

        while (frontier.Count > 0)
        {
            Vector2Int current = frontier.Dequeue();
            foreach (Vector2Int direction in FourDirections)
            {
                Vector2Int next = current + direction;
                if (!TryMapWorld(next.x, next.y, out _, out _) || !visited.Add(next))
                    continue;

                if (IsFreeCell(next.x, next.y, ignore))
                {
                    result = next;
                    return true;
                }

                frontier.Enqueue(next);
            }
        }

        return false;
    }

    /// <summary>四方向最短路径。目标格可被指定单位占用（用于走向敌人但不踩上去）。</summary>
    public bool TryFindPath(
        Vector2Int start,
        Vector2Int goal,
        Unit mover,
        List<Vector2Int> path)
    {
        path.Clear();

        if (start == goal)
        {
            path.Add(start);
            return true;
        }

        Queue<Vector2Int> frontier = new Queue<Vector2Int>();
        Dictionary<Vector2Int, Vector2Int> cameFrom =
            new Dictionary<Vector2Int, Vector2Int>();

        frontier.Enqueue(start);
        cameFrom[start] = start;

        while (frontier.Count > 0)
        {
            Vector2Int current = frontier.Dequeue();
            if (current == goal)
            {
                ReconstructPath(cameFrom, start, goal, path);
                return true;
            }

            foreach (Vector2Int direction in FourDirections)
            {
                Vector2Int next = current + direction;
                if (cameFrom.ContainsKey(next) || !CanStep(current, next, mover, goal))
                {
                    continue;
                }

                cameFrom[next] = current;
                frontier.Enqueue(next);
            }
        }

        return false;
    }

    private bool IsFreeCell(int x, int z, Unit ignore)
    {
        return HasTile(x, z) && !IsOccupied(x, z, ignore);
    }

    private static void ReconstructPath(
        Dictionary<Vector2Int, Vector2Int> cameFrom,
        Vector2Int start,
        Vector2Int goal,
        List<Vector2Int> path)
    {
        Vector2Int current = goal;
        path.Add(current);

        while (current != start)
        {
            current = cameFrom[current];
            path.Add(current);
        }

        path.Reverse();
    }

    private void SpawnDecorations()
    {
        for (int i = 0; i < decorations.Count; i++)
            if (decorations[i] != null) Destroy(decorations[i]);
        decorations.Clear();
        if (worldSource == null) return;
        for (int s = 0; s < worldSource.Stages.Count; s++)
        {
            StageDefinition stage = worldSource.Stages[s];
            if (stage == null || !stage.Enabled) continue;
            if (stage.Decorations != null)
            {
                for (int d = 0; d < stage.Decorations.Count; d++)
                {
                    WorldDecorationDefinition deco = stage.Decorations[d];
                    if (deco == null || string.IsNullOrWhiteSpace(deco.Definition)) continue;
                    Vector2Int world = WorldLayout.ToBoard(stage, deco.LocalX, deco.LocalY, worldSource);
                    if (!TryGetCell(world.x, world.y, out BoardCell cell)) continue;
                    decorations.Add(WorldDecorationCatalog.Spawn(deco, cell.transform));
                }
            }

            if (stage.UnitPlacements == null) continue;
            for (int u = 0; u < stage.UnitPlacements.Count; u++)
            {
                GameObject preview = WorldUnitPreview.SpawnOnBoard(this, stage, stage.UnitPlacements[u]);
                if (preview != null) decorations.Add(preview);
            }
        }
    }

    [Serializable]
    public sealed class TerrainPrefabBinding
    {
        public string terrainId;
        public GameObject prefab;
    }

    public struct TileRecord
    {
        public bool Exists;
        public int Height;
        public string TerrainId;
        public string StageId;
    }

    public static long TileKey(int x, int z) => ((long)x << 32) | (uint)z;

    public static void DecodeTileKey(long key, out int x, out int z)
    {
        x = (int)(key >> 32);
        z = (int)key;
    }

    private bool TryGetTile(int x, int z, out TileRecord tile)
    {
        if (tiles.TryGetValue(TileKey(x, z), out tile))
            return true;
        if (chunkProvider == null || worldSource == null) return false;
        int tw = Mathf.Max(1, worldSource.TerrainWidth);
        int th = Mathf.Max(1, worldSource.TerrainHeight);
        WorldCoords.ToChunk(x, z, tw, th, out ChunkPosition chunk, out _, out _);
        if (!EnsureChunkData(chunk, out _)) return false;
        return tiles.TryGetValue(TileKey(x, z), out tile);
    }
}
