using System;
using System.Collections.Generic;
using LegendsOfFurry.Content.Contracts;
using UnityEngine;

/// <summary>按摄像机可见集加载 ChunkView，预取一圈，LRU 回收离屏块。</summary>
[DefaultExecutionOrder(-50)]
public sealed class WorldChunkStreamer : MonoBehaviour
{
    [SerializeField] private int maxResident = 32;
    [SerializeField] private int prefetchRing = 1;
    [SerializeField] private int unloadHysteresis = 1;
    [SerializeField] private int instantiatesPerFrame = 4;
    [SerializeField] private int maxViewportChunkRadius = 4;
    [SerializeField] private bool autoCollectFromCamera = true;

    private readonly Dictionary<(int, int), WorldChunkView> resident =
        new Dictionary<(int, int), WorldChunkView>();
    private readonly LinkedList<ChunkPosition> lru = new LinkedList<ChunkPosition>();
    private readonly Dictionary<(int, int), LinkedListNode<ChunkPosition>> lruNodes =
        new Dictionary<(int, int), LinkedListNode<ChunkPosition>>();
    private readonly Queue<ChunkPosition> loadQueue = new Queue<ChunkPosition>();
    private readonly HashSet<(int, int)> queued = new HashSet<(int, int)>();
    private readonly HashSet<(int, int)> desired = new HashSet<(int, int)>();

    private BoardGenerator board;
    private IChunkProvider provider;
    private WorldFloatingOrigin floatingOrigin;
    private ChunkPosition focus;
    private int keepMinX;
    private int keepMaxX;
    private int keepMinY;
    private int keepMaxY;

    public int MaxResident { get => maxResident; set => maxResident = Mathf.Max(1, value); }
    public int PrefetchRing { get => prefetchRing; set => prefetchRing = Mathf.Max(0, value); }
    public int UnloadHysteresis { get => unloadHysteresis; set => unloadHysteresis = Mathf.Max(0, value); }
    public int InstantiatesPerFrame { get => instantiatesPerFrame; set => instantiatesPerFrame = Mathf.Max(1, value); }
    public bool AutoCollectFromCamera { get => autoCollectFromCamera; set => autoCollectFromCamera = value; }
    public int ResidentCount => resident.Count;
    public ChunkPosition Focus => focus;

    public event Action<ChunkPosition, WorldChunkView> ChunkLoaded;
    public event Action<ChunkPosition> ChunkUnloaded;

    /// <summary>切换世界时清空旧世界的队列、区块视图和 LRU 记录。</summary>
    public void ResetStream()
    {
        var loaded = new List<ChunkPosition>(resident.Count);
        foreach (KeyValuePair<(int, int), WorldChunkView> pair in resident)
            loaded.Add(new ChunkPosition(pair.Key.Item1, pair.Key.Item2));
        for (int i = 0; i < loaded.Count; i++) Unload(loaded[i]);
        resident.Clear();
        lru.Clear();
        lruNodes.Clear();
        loadQueue.Clear();
        queued.Clear();
        desired.Clear();
    }

    public void Bind(BoardGenerator boardGenerator, IChunkProvider chunkProvider)
    {
        if (board != boardGenerator || provider != chunkProvider)
            ResetStream();
        board = boardGenerator;
        provider = chunkProvider;
        if (board != null && floatingOrigin == null)
        {
            floatingOrigin = board.GetComponent<WorldFloatingOrigin>();
            if (floatingOrigin == null) floatingOrigin = board.gameObject.AddComponent<WorldFloatingOrigin>();
            floatingOrigin.Bind(board.GridRoot);
        }
    }

    public void SetFocusChunk(ChunkPosition chunk)
    {
        focus = chunk;
        int keep = prefetchRing + unloadHysteresis;
        SetKeepRect(focus.X - keep, focus.X + keep, focus.Y - keep, focus.Y + keep);
        RefreshDesired(prefetchRing);
        EnqueueMissing();
        EvictFarChunks();
    }

    public void SetFocusWorldTile(int worldX, int worldZ)
    {
        if (board?.WorldSource == null)
        {
            SetFocusChunk(new ChunkPosition(0, 0));
            return;
        }

        int tw = Mathf.Max(1, board.WorldSource.TerrainWidth);
        int th = Mathf.Max(1, board.WorldSource.TerrainHeight);
        WorldCoords.ToChunk(worldX, worldZ, tw, th, out ChunkPosition chunk, out _, out _);
        SetFocusChunk(chunk);
        floatingOrigin?.RecenterOn(board.EvaluateTilePosition(worldX, worldZ));
    }

    public void CollectVisibleFromCamera(Camera camera)
    {
        if (camera == null || board?.WorldSource == null)
        {
            SetFocusChunk(focus);
            return;
        }

        int tw = Mathf.Max(1, board.WorldSource.TerrainWidth);
        int th = Mathf.Max(1, board.WorldSource.TerrainHeight);
        desired.Clear();
        SampleViewportChunk(camera, 0.5f, 0.5f, tw, th, out ChunkPosition center);
        focus = center;
        SampleViewportChunk(camera, 0f, 0f, tw, th, out ChunkPosition a);
        SampleViewportChunk(camera, 1f, 0f, tw, th, out ChunkPosition b);
        SampleViewportChunk(camera, 0f, 1f, tw, th, out ChunkPosition c);
        SampleViewportChunk(camera, 1f, 1f, tw, th, out ChunkPosition d);
        int minX = Mathf.Min(a.X, Mathf.Min(b.X, Mathf.Min(c.X, d.X))) - prefetchRing;
        int maxX = Mathf.Max(a.X, Mathf.Max(b.X, Mathf.Max(c.X, d.X))) + prefetchRing;
        int minY = Mathf.Min(a.Y, Mathf.Min(b.Y, Mathf.Min(c.Y, d.Y))) - prefetchRing;
        int maxY = Mathf.Max(a.Y, Mathf.Max(b.Y, Mathf.Max(c.Y, d.Y))) + prefetchRing;
        // 透视相机接近地平线时，角点射线会落在极远处；不能据此创建成千上万块。
        int limit = Mathf.Max(1, maxViewportChunkRadius);
        minX = Mathf.Max(minX, center.X - limit);
        maxX = Mathf.Min(maxX, center.X + limit);
        minY = Mathf.Max(minY, center.Y - limit);
        maxY = Mathf.Min(maxY, center.Y + limit);
        if (board.WorldSource != null && !board.WorldSource.IsInfinite)
        {
            minX = Mathf.Max(minX, board.WorldSource.BoundsMinX);
            maxX = Mathf.Min(maxX, board.WorldSource.BoundsMaxX);
            minY = Mathf.Max(minY, board.WorldSource.BoundsMinY);
            maxY = Mathf.Min(maxY, board.WorldSource.BoundsMaxY);
        }
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
                desired.Add((x, y));
        SetKeepRect(minX - unloadHysteresis, maxX + unloadHysteresis,
            minY - unloadHysteresis, maxY + unloadHysteresis);
        EnqueueMissing();
        EvictFarChunks();
    }

    public void Tick()
    {
        int budget = instantiatesPerFrame;
        while (budget-- > 0 && loadQueue.Count > 0)
            TryLoad(loadQueue.Dequeue());
        EvictToCap();
    }

    public void Flush()
    {
        while (loadQueue.Count > 0)
            TryLoad(loadQueue.Dequeue());
        EvictToCap();
    }

    public void Reload(ChunkPosition position)
    {
        if (resident.ContainsKey((position.X, position.Y)))
            Unload(position);
        queued.Remove((position.X, position.Y));
        if (provider != null && !provider.CanProvide(position)) return;
        queued.Add((position.X, position.Y));
        loadQueue.Enqueue(position);
    }

    public bool IsResident(ChunkPosition position) => resident.ContainsKey((position.X, position.Y));

    public bool TryGetView(ChunkPosition position, out WorldChunkView view) =>
        resident.TryGetValue((position.X, position.Y), out view);

    private void LateUpdate()
    {
        if (board == null) return;
        if (autoCollectFromCamera)
        {
            Camera camera = Camera.main;
            if (camera != null) CollectVisibleFromCamera(camera);
        }

        Tick();
    }

    private void RefreshDesired(int ring)
    {
        desired.Clear();
        for (int y = focus.Y - ring; y <= focus.Y + ring; y++)
            for (int x = focus.X - ring; x <= focus.X + ring; x++)
                desired.Add((x, y));
    }

    private void EnqueueMissing()
    {
        foreach ((int x, int y) in desired)
        {
            if (resident.ContainsKey((x, y)) || queued.Contains((x, y))) continue;
            if (provider != null && !provider.CanProvide(new ChunkPosition(x, y))) continue;
            queued.Add((x, y));
            loadQueue.Enqueue(new ChunkPosition(x, y));
        }
    }

    private void TryLoad(ChunkPosition position)
    {
        queued.Remove((position.X, position.Y));
        if (resident.ContainsKey((position.X, position.Y)))
        {
            Touch(position);
            return;
        }

        if (board == null || !board.EnsureChunkData(position, out StageDefinition stage) || stage == null)
            return;

        Transform parent = board.GridRoot;
        GameObject go = new GameObject($"Chunk_{position}");
        go.transform.SetParent(parent, false);
        WorldChunkView view = go.AddComponent<WorldChunkView>();
        view.Build(board, stage);
        resident[(position.X, position.Y)] = view;
        Touch(position);
        WorldTerrainInstanceRenderer instancer = board.GetComponent<WorldTerrainInstanceRenderer>();
        if (instancer == null) instancer = board.gameObject.AddComponent<WorldTerrainInstanceRenderer>();
        instancer.RegisterChunk(view.transform);
        RefreshNeighborWalls(position);
        ChunkLoaded?.Invoke(position, view);
        EvictToCap();
    }

    private void SetKeepRect(int minX, int maxX, int minY, int maxY)
    {
        keepMinX = minX;
        keepMaxX = maxX;
        keepMinY = minY;
        keepMaxY = maxY;
    }

    private void EvictFarChunks()
    {
        List<ChunkPosition> drop = null;
        foreach (KeyValuePair<(int, int), WorldChunkView> pair in resident)
        {
            if (pair.Key.Item1 >= keepMinX && pair.Key.Item1 <= keepMaxX &&
                pair.Key.Item2 >= keepMinY && pair.Key.Item2 <= keepMaxY)
                continue;
            drop ??= new List<ChunkPosition>();
            drop.Add(new ChunkPosition(pair.Key.Item1, pair.Key.Item2));
        }

        if (drop == null) return;
        for (int i = 0; i < drop.Count; i++)
            Unload(drop[i]);
    }

    private void EvictToCap()
    {
        int scanned = 0;
        int limit = lru.Count;
        while (resident.Count > maxResident && lru.Count > 0 && scanned < limit)
        {
            ChunkPosition oldest = lru.First.Value;
            if (oldest.X == focus.X && oldest.Y == focus.Y)
            {
                lru.RemoveFirst();
                lru.AddLast(oldest);
                scanned++;
                continue;
            }

            Unload(oldest);
            scanned = 0;
            limit = lru.Count;
        }
    }

    private void Unload(ChunkPosition position)
    {
        if (!resident.TryGetValue((position.X, position.Y), out WorldChunkView view)) return;
        WorldTerrainInstanceRenderer instancer = board != null
            ? board.GetComponent<WorldTerrainInstanceRenderer>() : null;
        instancer?.UnregisterChunk(view.transform);
        view.Teardown(board);
        resident.Remove((position.X, position.Y));
        if (lruNodes.TryGetValue((position.X, position.Y), out LinkedListNode<ChunkPosition> node))
        {
            lru.Remove(node);
            lruNodes.Remove((position.X, position.Y));
        }

        if (view != null)
        {
            if (Application.isPlaying) Destroy(view.gameObject);
            else DestroyImmediate(view.gameObject);
        }

        provider?.Release(position);
        RefreshNeighborWalls(position);
        ChunkUnloaded?.Invoke(position);
    }

    private void RefreshNeighborWalls(ChunkPosition position)
    {
        RefreshWalls(position);
        RefreshWalls(new ChunkPosition(position.X + 1, position.Y));
        RefreshWalls(new ChunkPosition(position.X - 1, position.Y));
        RefreshWalls(new ChunkPosition(position.X, position.Y + 1));
        RefreshWalls(new ChunkPosition(position.X, position.Y - 1));
    }

    private void RefreshWalls(ChunkPosition position)
    {
        if (resident.TryGetValue((position.X, position.Y), out WorldChunkView view) && view != null)
            view.RebuildWalls(board);
    }

    private void Touch(ChunkPosition position)
    {
        if (lruNodes.TryGetValue((position.X, position.Y), out LinkedListNode<ChunkPosition> node))
        {
            lru.Remove(node);
            lru.AddLast(node);
            return;
        }

        lruNodes[(position.X, position.Y)] = lru.AddLast(position);
    }

    private void SampleViewportChunk(Camera camera, float vx, float vy, int tw, int th, out ChunkPosition chunk)
    {
        Ray ray = camera.ViewportPointToRay(new Vector3(vx, vy, 0f));
        float t = 0f;
        if (Mathf.Abs(ray.direction.y) > 0.0001f)
            t = -ray.origin.y / ray.direction.y;
        Vector3 hit = ray.origin + ray.direction * t;
        Vector3 local = board.GridRoot.InverseTransformPoint(hit);
        float spacing = board.Spacing;
        int worldX = Mathf.RoundToInt(local.x / spacing + originBiasX());
        int worldZ = Mathf.RoundToInt(local.z / spacing + originBiasZ());
        WorldCoords.ToChunk(worldX, worldZ, tw, th, out chunk, out _, out _);
    }

    private float originBiasX() => board.OriginWorldX + (board.Width - 1) / 2f;
    private float originBiasZ() => board.OriginWorldZ + (board.Height - 1) / 2f;
}
