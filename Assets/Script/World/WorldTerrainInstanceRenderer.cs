using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 按关卡空间块合批提交基础地形 Mesh。格子上的 Collider、寻路和高亮仍逐格存在，
/// 只关闭 TerrainBatchSource 的 MeshRenderer，改走 Graphics.RenderMeshInstanced。
/// </summary>
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
public sealed class WorldTerrainInstanceRenderer : MonoBehaviour
{
    public const int MaxInstancesPerBatch = 511;
    public const int ChunkSize = 10;

    private static readonly Vector3 ExtentsX = new Vector3(1f, 0f, 0f);
    private static readonly Vector3 ExtentsY = new Vector3(0f, 1f, 0f);
    private static readonly Vector3 ExtentsZ = new Vector3(0f, 0f, 1f);

    private readonly List<Source> sources = new List<Source>(256);
    private readonly List<Batch> batches = new List<Batch>(64);
    private readonly Dictionary<BatchKey, Batch> batchLookup = new Dictionary<BatchKey, Batch>(64);
    private readonly Dictionary<string, int> chunkIndex = new Dictionary<string, int>(64);
    private readonly List<string> chunkKeys = new List<string>(64);
    private readonly HashSet<TerrainBatchSource> capturedMarkers = new HashSet<TerrainBatchSource>();
    private readonly Plane[] frustumPlanes = new Plane[6];
    private Bounds[] chunkWorldBounds = Array.Empty<Bounds>();
    private bool[] chunkBoundsInit = Array.Empty<bool>();
    private bool[] chunkInFrustum = Array.Empty<bool>();
    private Matrix4x4[] splitScratch;
    private bool fallbackWarned;

    public int VisibleInstanceCount { get; private set; }
    public int SubmittedBatchCount { get; private set; }
    public int FallbackRendererCount { get; private set; }
    public int BatchGroupCount => batches.Count;

    private void LateUpdate()
    {
        if (!isActiveAndEnabled) return;
        RefreshFrame(ResolveCamera(), Application.isPlaying);
    }

    public void Rebuild()
    {
        ClearRuntimeState();
        TerrainBatchSource[] markers = GetComponentsInChildren<TerrainBatchSource>(true);
        for (int i = 0; i < markers.Length; i++)
            TryAddMarker(markers[i]);
        AllocateBatchMatrices();
        WarnFallback();
    }

    public void RegisterChunk(Transform root)
    {
        if (root == null) return;
        TerrainBatchSource[] markers = root.GetComponentsInChildren<TerrainBatchSource>(true);
        for (int i = 0; i < markers.Length; i++)
            TryAddMarker(markers[i]);
        AllocateBatchMatrices();
        WarnFallback();
    }

    public void UnregisterChunk(Transform root)
    {
        if (root == null) return;
        RemoveSources(source => source.Transform != null && source.Transform.IsChildOf(root));
    }

    public void UnregisterChunk(string chunkKey)
    {
        if (string.IsNullOrEmpty(chunkKey)) return;
        RemoveSources(source => ResolveChunkKey(source.Cell) == chunkKey);
    }

    public void RefreshFrame(Camera camera, bool submit)
    {
        VisibleInstanceCount = 0;
        SubmittedBatchCount = 0;
        if (batches.Count == 0) return;

        bool cull = camera != null && camera.enabled && camera.gameObject.activeInHierarchy;
        if (cull)
            GeometryUtility.CalculateFrustumPlanes(camera, frustumPlanes);

        RebuildChunkWorldBounds(cull);

        for (int i = 0; i < batches.Count; i++)
        {
            Batch batch = batches[i];
            if (cull &&
                chunkIndex.TryGetValue(batch.ChunkKey, out int chunkSlot) &&
                !chunkInFrustum[chunkSlot])
            {
                batch.VisibleCount = 0;
                continue;
            }

            int visible = 0;
            List<int> indices = batch.SourceIndices;
            for (int s = 0; s < indices.Count; s++)
            {
                Source source = sources[indices[s]];
                if (!IsVisible(source)) continue;
                batch.Matrices[visible] = source.Transform.localToWorldMatrix;
                visible++;
            }

            batch.VisibleCount = visible;
            if (visible == 0) continue;

            Bounds worldBounds = chunkIndex.TryGetValue(batch.ChunkKey, out int boundsSlot)
                ? chunkWorldBounds[boundsSlot]
                : ComputeBatchWorldBounds(batch);
            if (cull && !GeometryUtility.TestPlanesAABB(frustumPlanes, worldBounds))
                continue;

            VisibleInstanceCount += visible;
            int draws = (visible + MaxInstancesPerBatch - 1) / MaxInstancesPerBatch;
            SubmittedBatchCount += draws;
            if (!submit) continue;

            var rp = new RenderParams(batch.Material)
            {
                worldBounds = worldBounds,
                shadowCastingMode = batch.ShadowCasting,
                receiveShadows = batch.ReceiveShadows,
                layer = batch.Layer,
                renderingLayerMask = batch.RenderingLayerMask,
                lightProbeUsage = batch.LightProbeUsage,
                reflectionProbeUsage = batch.ReflectionProbeUsage,
                camera = camera
            };

            for (int offset = 0; offset < visible; offset += MaxInstancesPerBatch)
            {
                int count = Mathf.Min(MaxInstancesPerBatch, visible - offset);
                if (offset == 0)
                {
                    Graphics.RenderMeshInstanced(rp, batch.Mesh, batch.SubMesh, batch.Matrices, count);
                    continue;
                }

                if (splitScratch == null || splitScratch.Length < count)
                    splitScratch = new Matrix4x4[MaxInstancesPerBatch];
                Array.Copy(batch.Matrices, offset, splitScratch, 0, count);
                Graphics.RenderMeshInstanced(rp, batch.Mesh, batch.SubMesh, splitScratch, count);
            }
        }
    }

    public bool TryGetBatch(int index, out string chunkKey, out Mesh mesh, out Material material, out int subMesh,
        out int sourceCount)
    {
        if (index < 0 || index >= batches.Count)
        {
            chunkKey = null;
            mesh = null;
            material = null;
            subMesh = 0;
            sourceCount = 0;
            return false;
        }

        Batch batch = batches[index];
        chunkKey = batch.ChunkKey;
        mesh = batch.Mesh;
        material = batch.Material;
        subMesh = batch.SubMesh;
        sourceCount = batch.SourceIndices.Count;
        return true;
    }

    public bool TryGetInstanceMatrix(int batchIndex, int instanceIndex, out Matrix4x4 matrix)
    {
        matrix = default;
        if (batchIndex < 0 || batchIndex >= batches.Count) return false;
        Batch batch = batches[batchIndex];
        if (instanceIndex < 0 || instanceIndex >= batch.VisibleCount) return false;
        matrix = batch.Matrices[instanceIndex];
        return true;
    }

    public static string ResolveChunkKey(BoardCell cell)
    {
        if (cell == null) return "0_0";
        if (!string.IsNullOrEmpty(cell.StageId)) return cell.StageId;
        Vector2Int coordinate = cell.Coordinate;
        int chunkX = WorldCoords.FloorDiv(coordinate.x, ChunkSize);
        int chunkZ = WorldCoords.FloorDiv(coordinate.y, ChunkSize);
        return chunkX + "_" + chunkZ;
    }

    private void ClearRuntimeState()
    {
        for (int i = 0; i < sources.Count; i++)
        {
            Source source = sources[i];
            if (source.Marker != null)
                source.Marker.Instanced = false;
        }

        sources.Clear();
        batches.Clear();
        batchLookup.Clear();
        chunkIndex.Clear();
        chunkKeys.Clear();
        capturedMarkers.Clear();
        FallbackRendererCount = 0;
        VisibleInstanceCount = 0;
        SubmittedBatchCount = 0;
    }

    private bool TryAddMarker(TerrainBatchSource marker)
    {
        if (marker == null) return false;
        if (!capturedMarkers.Add(marker)) return false;

        MeshRenderer meshRenderer = marker.GetComponent<MeshRenderer>();
        MeshFilter filter = marker.GetComponent<MeshFilter>();
        BoardCell cell = marker.GetComponentInParent<BoardCell>(true);
        Mesh mesh = filter != null ? filter.sharedMesh : null;
        Material[] materials = meshRenderer != null ? meshRenderer.sharedMaterials : null;
        bool canBatch = SystemInfo.supportsInstancing &&
                        meshRenderer != null &&
                        mesh != null &&
                        mesh.subMeshCount > 0 &&
                        materials != null &&
                        CanBatchMaterials(materials, mesh.subMeshCount);
        marker.Instanced = canBatch;
        if (!canBatch)
        {
            FallbackRendererCount++;
            if (meshRenderer != null)
                meshRenderer.enabled = cell == null || cell.TerrainVisible;
            return false;
        }

        meshRenderer.enabled = false;
        int sourceIndex = sources.Count;
        sources.Add(new Source
        {
            Marker = marker,
            Transform = marker.transform,
            Cell = cell,
            LocalBounds = mesh.bounds
        });

        string chunkKey = ResolveChunkKey(cell);
        EnsureChunkIndex(chunkKey);
        int subMeshCount = Mathf.Min(mesh.subMeshCount, materials.Length);
        for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
        {
            Material material = materials[subMesh];
            if (material == null) continue;
            BatchKey key = new BatchKey(chunkKey, mesh, material, subMesh);
            if (!batchLookup.TryGetValue(key, out Batch batch))
            {
                batch = new Batch
                {
                    ChunkKey = chunkKey,
                    Mesh = mesh,
                    Material = material,
                    SubMesh = subMesh,
                    ShadowCasting = meshRenderer.shadowCastingMode,
                    ReceiveShadows = meshRenderer.receiveShadows,
                    Layer = marker.gameObject.layer,
                    RenderingLayerMask = meshRenderer.renderingLayerMask,
                    LightProbeUsage = meshRenderer.lightProbeUsage,
                    ReflectionProbeUsage = meshRenderer.reflectionProbeUsage,
                    SourceIndices = new List<int>(32)
                };
                batchLookup.Add(key, batch);
                batches.Add(batch);
            }

            batch.SourceIndices.Add(sourceIndex);
        }

        return true;
    }

    private void RemoveSources(Predicate<Source> match)
    {
        bool removed = false;
        for (int i = sources.Count - 1; i >= 0; i--)
        {
            Source source = sources[i];
            if (!match(source)) continue;
            if (source.Marker != null)
            {
                capturedMarkers.Remove(source.Marker);
                source.Marker.Instanced = false;
                MeshRenderer renderer = source.Marker.GetComponent<MeshRenderer>();
                if (renderer != null)
                    renderer.enabled = source.Cell == null || source.Cell.TerrainVisible;
            }

            sources.RemoveAt(i);
            removed = true;
        }

        if (!removed) return;
        ReindexBatches();
    }

    private void ReindexBatches()
    {
        List<Source> remaining = new List<Source>(sources);
        sources.Clear();
        batches.Clear();
        batchLookup.Clear();
        chunkIndex.Clear();
        chunkKeys.Clear();
        capturedMarkers.Clear();
        FallbackRendererCount = 0;
        for (int i = 0; i < remaining.Count; i++)
        {
            TerrainBatchSource marker = remaining[i].Marker;
            if (marker != null) TryAddMarker(marker);
        }

        AllocateBatchMatrices();
    }

    private void AllocateBatchMatrices()
    {
        for (int i = 0; i < batches.Count; i++)
        {
            Batch batch = batches[i];
            int count = batch.SourceIndices.Count;
            if (batch.Matrices == null || batch.Matrices.Length != count)
                batch.Matrices = count > 0 ? new Matrix4x4[count] : Array.Empty<Matrix4x4>();
        }
    }

    private void WarnFallback()
    {
        if (FallbackRendererCount > 0 && !fallbackWarned)
        {
            fallbackWarned = true;
            Debug.LogWarning(
                $"WorldTerrainInstanceRenderer 有 {FallbackRendererCount} 个地形 Renderer 回退到 MeshRenderer。",
                this);
        }
    }

    private void EnsureChunkIndex(string chunkKey)
    {
        if (chunkIndex.ContainsKey(chunkKey)) return;
        chunkIndex[chunkKey] = chunkKeys.Count;
        chunkKeys.Add(chunkKey);
    }

    private void RebuildChunkWorldBounds(bool cull)
    {
        int chunkCount = chunkKeys.Count;
        if (chunkWorldBounds.Length < chunkCount)
        {
            chunkWorldBounds = new Bounds[Mathf.Max(8, chunkCount)];
            chunkBoundsInit = new bool[chunkWorldBounds.Length];
            chunkInFrustum = new bool[chunkWorldBounds.Length];
        }

        Array.Clear(chunkBoundsInit, 0, chunkCount);
        for (int i = 0; i < sources.Count; i++)
        {
            Source source = sources[i];
            if (!IsVisible(source)) continue;
            string key = ResolveChunkKey(source.Cell);
            if (!chunkIndex.TryGetValue(key, out int slot)) continue;
            Encapsulate(ref chunkWorldBounds[slot], ref chunkBoundsInit[slot], source.LocalBounds,
                source.Transform.localToWorldMatrix);
        }

        for (int i = 0; i < chunkCount; i++)
        {
            if (!cull)
            {
                chunkInFrustum[i] = chunkBoundsInit[i];
                continue;
            }

            chunkInFrustum[i] = chunkBoundsInit[i] &&
                                GeometryUtility.TestPlanesAABB(frustumPlanes, chunkWorldBounds[i]);
        }
    }

    private Bounds ComputeBatchWorldBounds(Batch batch)
    {
        Bounds worldBounds = default;
        bool boundsInit = false;
        List<int> indices = batch.SourceIndices;
        for (int s = 0; s < indices.Count; s++)
        {
            Source source = sources[indices[s]];
            if (!IsVisible(source)) continue;
            Encapsulate(ref worldBounds, ref boundsInit, source.LocalBounds, source.Transform.localToWorldMatrix);
        }

        return worldBounds;
    }

    private static Camera ResolveCamera()
    {
        Camera main = Camera.main;
        if (main != null && main.isActiveAndEnabled) return main;
        Camera[] cameras = Camera.allCameras;
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (camera != null && camera.isActiveAndEnabled && camera.cameraType == CameraType.Game)
                return camera;
        }

        return null;
    }

    private static bool IsVisible(Source source)
    {
        if (source.Transform == null || !source.Transform.gameObject.activeInHierarchy)
            return false;
        BoardCell cell = source.Cell;
        if (cell == null) return source.Transform.gameObject.activeInHierarchy;
        return cell.TerrainVisible && cell.isActiveAndEnabled && cell.gameObject.activeInHierarchy;
    }

    private static bool CanBatchMaterials(Material[] materials, int subMeshCount)
    {
        int count = Mathf.Min(subMeshCount, materials.Length);
        if (count <= 0) return false;
        bool any = false;
        for (int i = 0; i < count; i++)
        {
            Material material = materials[i];
            if (material == null) continue;
            any = true;
            if (!material.enableInstancing || material.shader == null || !material.shader.isSupported)
                return false;
        }

        return any;
    }

    private static void Encapsulate(ref Bounds worldBounds, ref bool initialized, in Bounds localBounds,
        in Matrix4x4 matrix)
    {
        Bounds transformed = TransformBounds(matrix, localBounds);
        if (!initialized)
        {
            worldBounds = transformed;
            initialized = true;
            return;
        }

        worldBounds.Encapsulate(transformed);
    }

    private static Bounds TransformBounds(in Matrix4x4 matrix, in Bounds local)
    {
        Vector3 center = matrix.MultiplyPoint3x4(local.center);
        Vector3 extents = local.extents;
        Vector3 axisX = matrix.MultiplyVector(ExtentsX * extents.x);
        Vector3 axisY = matrix.MultiplyVector(ExtentsY * extents.y);
        Vector3 axisZ = matrix.MultiplyVector(ExtentsZ * extents.z);
        Vector3 worldExtents = new Vector3(
            Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
            Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
            Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
        return new Bounds(center, worldExtents * 2f);
    }

    private struct BatchKey : IEquatable<BatchKey>
    {
        public readonly string Chunk;
        public readonly Mesh Mesh;
        public readonly Material Material;
        public readonly int SubMesh;

        public BatchKey(string chunk, Mesh mesh, Material material, int subMesh)
        {
            Chunk = chunk ?? string.Empty;
            Mesh = mesh;
            Material = material;
            SubMesh = subMesh;
        }

        public bool Equals(BatchKey other)
        {
            return SubMesh == other.SubMesh &&
                   Chunk == other.Chunk &&
                   ReferenceEquals(Mesh, other.Mesh) &&
                   ReferenceEquals(Material, other.Material);
        }

        public override bool Equals(object obj) => obj is BatchKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Chunk != null ? Chunk.GetHashCode() : 0;
                hash = (hash * 397) ^ (Mesh != null ? Mesh.GetHashCode() : 0);
                hash = (hash * 397) ^ (Material != null ? Material.GetHashCode() : 0);
                hash = (hash * 397) ^ SubMesh;
                return hash;
            }
        }
    }

    private sealed class Source
    {
        public TerrainBatchSource Marker;
        public Transform Transform;
        public BoardCell Cell;
        public Bounds LocalBounds;
    }

    private sealed class Batch
    {
        public string ChunkKey;
        public Mesh Mesh;
        public Material Material;
        public int SubMesh;
        public List<int> SourceIndices;
        public Matrix4x4[] Matrices;
        public ShadowCastingMode ShadowCasting;
        public bool ReceiveShadows;
        public int Layer;
        public uint RenderingLayerMask;
        public LightProbeUsage LightProbeUsage;
        public ReflectionProbeUsage ReflectionProbeUsage;
        public int VisibleCount;
    }
}
