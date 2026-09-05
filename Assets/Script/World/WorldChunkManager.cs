using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

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
    [SerializeField, Min(1f)] private float lowDistanceMultiplier = 1.35f;

    private sealed class ChunkState
    {
        public readonly List<BoardCell> Cells = new List<BoardCell>();
        public readonly List<MeshRenderer> CombinedRenderers = new List<MeshRenderer>();
        public Bounds Bounds;
        public ChunkLod Lod;
        public Transform CombinedRoot;

        public bool CanUseCombined
        {
            get
            {
                for (int i = 0; i < Cells.Count; i++)
                {
                    BoardCell cell = Cells[i];
                    if (cell == null || !cell.gameObject.activeInHierarchy || !cell.BaseTerrainVisible)
                        return false;
                }

                return Cells.Count > 0;
            }
        }
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
        DestroyCombinedMeshes();
        board = source;
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

    /// <summary>迷雾/测试解锁改变地形可见性后，立即重新应用当前 Chunk LOD。</summary>
    public void RefreshNow()
    {
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
            EnsureCombinedMeshes(state);
            // 只比较水平 XZ 距离，不能把俯视相机高度算入 LOD。
            float distance = HorizontalDistance(state.Bounds, position);
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
        bool useCombined = lod == ChunkLod.Low && state.CanUseCombined;
        for (int i = 0; i < state.Cells.Count; i++)
        {
            BoardCell cell = state.Cells[i];
            if (cell == null) continue;
            cell.SetChunkTerrainVisible(terrainVisible && !useCombined);
            cell.SetSmallDecorationsVisible(decorationsVisible);
            cell.SetChunkColliderEnabled(terrainVisible);
        }

        for (int i = 0; i < state.CombinedRenderers.Count; i++)
        {
            MeshRenderer renderer = state.CombinedRenderers[i];
            if (renderer != null) renderer.enabled = useCombined;
        }
    }

    private static float HorizontalDistance(Bounds bounds, Vector3 position)
    {
        float dx = Mathf.Max(Mathf.Abs(position.x - bounds.center.x) - bounds.extents.x, 0f);
        float dz = Mathf.Max(Mathf.Abs(position.z - bounds.center.z) - bounds.extents.z, 0f);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private void EnsureCombinedMeshes(ChunkState state)
    {
        if (state.CombinedRoot != null) return;

        GameObject rootObject = new GameObject("CombinedTerrain");
        state.CombinedRoot = rootObject.transform;
        state.CombinedRoot.SetParent(transform, false);
        Dictionary<Material, List<CombineInstance>> byMaterial =
            new Dictionary<Material, List<CombineInstance>>();

        for (int i = 0; i < state.Cells.Count; i++)
        {
            BoardCell cell = state.Cells[i];
            if (cell == null) continue;
            MeshFilter[] filters = cell.GetComponentsInChildren<MeshFilter>(true);
            for (int f = 0; f < filters.Length; f++)
            {
                MeshFilter filter = filters[f];
                if (filter == null || filter.sharedMesh == null ||
                    filter.GetComponentInParent<WorldDecorationVisual>() != null)
                    continue;
                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || renderer.sharedMaterials == null) continue;
                int subMeshCount = Mathf.Min(filter.sharedMesh.subMeshCount, renderer.sharedMaterials.Length);
                for (int sub = 0; sub < subMeshCount; sub++)
                {
                    Material material = renderer.sharedMaterials[sub];
                    if (material == null) continue;
                    if (!byMaterial.TryGetValue(material, out List<CombineInstance> list))
                    {
                        list = new List<CombineInstance>();
                        byMaterial.Add(material, list);
                    }

                    list.Add(new CombineInstance
                    {
                        mesh = filter.sharedMesh,
                        subMeshIndex = sub,
                        transform = state.CombinedRoot.worldToLocalMatrix * filter.transform.localToWorldMatrix
                    });
                }
            }
        }

        foreach (KeyValuePair<Material, List<CombineInstance>> pair in byMaterial)
        {
            if (pair.Value.Count == 0) continue;
            Mesh mesh = new Mesh { name = "ChunkTerrainMesh" };
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.CombineMeshes(pair.Value.ToArray(), true, true, false);
            GameObject meshObject = new GameObject("Terrain_" + pair.Key.name);
            meshObject.transform.SetParent(state.CombinedRoot, false);
            MeshFilter meshFilter = meshObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;
            MeshRenderer meshRenderer = meshObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = pair.Key;
            meshRenderer.shadowCastingMode = ShadowCastingMode.On;
            meshRenderer.receiveShadows = true;
            state.CombinedRenderers.Add(meshRenderer);
        }
    }

    private void DestroyCombinedMeshes()
    {
        foreach (ChunkState state in chunks.Values)
        {
            if (state.CombinedRoot != null)
                Destroy(state.CombinedRoot.gameObject);
        }

        chunks.Clear();
    }

    private void OnDestroy()
    {
        DestroyCombinedMeshes();
    }
}
