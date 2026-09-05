using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 世界棋盘的渲染调度层。逻辑格仍由 BoardGenerator/BoardCell 管理，
/// 本组件只按 Chunk 切换视觉细节，避免每帧遍历整张地图。
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("LegendsOfFurry/World Chunk Manager")]
public sealed class WorldChunkManager : MonoBehaviour
{
    public enum ChunkLod
    {
        High,
        Medium,
        Low,
        Hidden
    }

    [Header("Chunk 配置")]
    [SerializeField, Min(1)] private int chunkSize = 8;
    [SerializeField, Min(0.1f)] private float refreshPositionThreshold = 1f;
    [SerializeField, Min(0.01f)] private float refreshZoomThreshold = 0.25f;

    [Header("LOD 距离（相对于正交相机尺寸）")]
    [SerializeField, Min(1f)] private float highDistanceMultiplier = 0.65f;
    [SerializeField, Min(1f)] private float mediumDistanceMultiplier = 1.5f;
    [SerializeField, Min(1f)] private float lowDistanceMultiplier = 3f;

    private sealed class ChunkState
    {
        public readonly List<BoardCell> Cells = new List<BoardCell>();
        public Bounds Bounds;
        public ChunkLod Lod;
    }

    private readonly Dictionary<ChunkPosition, ChunkState> chunks =
        new Dictionary<ChunkPosition, ChunkState>();
    private BoardGenerator board;
    private Camera targetCamera;
    private Vector3 lastCameraPosition;
    private float lastOrthographicSize = -1f;
    private bool dirty = true;

    public int ChunkSize => Mathf.Max(1, chunkSize);

    public void Rebuild(BoardGenerator source)
    {
        board = source;
        chunks.Clear();
        if (board == null) return;

        BoardCell[] cells = board.GetComponentsInChildren<BoardCell>(true);
        for (int i = 0; i < cells.Length; i++)
        {
            BoardCell cell = cells[i];
            if (cell == null) continue;
            WorldCoords.ToChunk(cell.Coordinate.x, cell.Coordinate.y, ChunkSize, ChunkSize,
                out ChunkPosition position, out _, out _);
            if (!chunks.TryGetValue(position, out ChunkState state))
            {
                state = new ChunkState();
                chunks.Add(position, state);
                state.Bounds = new Bounds(cell.transform.position, Vector3.zero);
            }

            state.Cells.Add(cell);
            state.Bounds.Encapsulate(cell.transform.position);
        }

        dirty = true;
        RefreshIfNeeded(true);
    }

    private void LateUpdate()
    {
        RefreshIfNeeded(false);
    }

    private void RefreshIfNeeded(bool force)
    {
        if (chunks.Count == 0) return;
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null) return;

        Vector3 position = targetCamera.transform.position;
        float zoom = targetCamera.orthographic ? targetCamera.orthographicSize : 0f;
        bool moved = (position - lastCameraPosition).sqrMagnitude >=
                     refreshPositionThreshold * refreshPositionThreshold;
        bool zoomed = Mathf.Abs(zoom - lastOrthographicSize) >= refreshZoomThreshold;
        if (!force && !dirty && !moved && !zoomed) return;

        lastCameraPosition = position;
        lastOrthographicSize = zoom;
        dirty = false;
        float viewSize = targetCamera.orthographic ? Mathf.Max(1f, zoom) : 8f;
        float highDistance = Mathf.Max(ChunkSize, viewSize * highDistanceMultiplier);
        float mediumDistance = Mathf.Max(highDistance + 1f, viewSize * mediumDistanceMultiplier);
        float lowDistance = Mathf.Max(mediumDistance + 1f, viewSize * lowDistanceMultiplier);

        foreach (ChunkState state in chunks.Values)
        {
            float distance = Mathf.Sqrt(state.Bounds.SqrDistance(position));
            ChunkLod lod = distance <= highDistance ? ChunkLod.High
                : distance <= mediumDistance ? ChunkLod.Medium
                : distance <= lowDistance ? ChunkLod.Low
                : ChunkLod.Hidden;
            if (!force && state.Lod == lod) continue;
            state.Lod = lod;
            ApplyLod(state, lod);
        }
    }

    private static void ApplyLod(ChunkState state, ChunkLod lod)
    {
        bool terrainVisible = lod != ChunkLod.Hidden;
        bool decorationsVisible = lod == ChunkLod.High;
        for (int i = 0; i < state.Cells.Count; i++)
        {
            BoardCell cell = state.Cells[i];
            if (cell == null) continue;
            cell.SetChunkTerrainVisible(terrainVisible);
            cell.SetSmallDecorationsVisible(decorationsVisible);
            cell.SetChunkColliderEnabled(terrainVisible);
        }
    }
}
