using System.Collections.Generic;
using LegendsOfFurry.Content.Contracts;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 一整片 CloudSea 盖在地形上方，按已点亮关卡的格子形状挖洞。
/// 未解锁关卡额外放竖直云雾填充块，挡住地形隐藏后露出的空洞。
/// 悬停/点击用看不见的关卡碰撞盒，不把云海按格子切开。
/// </summary>
public sealed class WorldCloudCover : MonoBehaviour
{
    public const string PrefabResource = "Prefabs/CloudSea";
    private const string FillChildName = "CloudFill";
    private const float PrefabPlaneSize = 500f;
    private const int MaxHoles = 32;
    private const bool CloudHitsEnabled = false;

    private readonly List<WorldCloudPatch> patches = new List<WorldCloudPatch>();
    private readonly Vector4[] holes = new Vector4[MaxHoles];
    private GameObject sea;
    private Transform hitRoot;
    private WorldDefinition world;
    private BoardGenerator board;
    private Texture2D terrainHeightTex;
    private Material fillMaterial;

    public float DeckY => sea != null ? sea.transform.position.y + 0.75f : 1.6f;

    public void Bind(WorldDefinition worldDefinition, BoardGenerator worldBoard)
    {
        world = worldDefinition;
        board = worldBoard;
        EnsureSea();
        FitSea();
        PublishTerrainHeights();
        if (CloudHitsEnabled)
            RebuildHitBoxes();
        Refresh(world, null, false);
    }

    public void Refresh(WorldDefinition worldDefinition, StageDefinition current, bool combat)
    {
        world = worldDefinition != null ? worldDefinition : world;
        if (sea != null) sea.SetActive(!combat);
        if (hitRoot != null) hitRoot.gameObject.SetActive(CloudHitsEnabled && !combat);
        if (combat || world == null || board == null)
        {
            Shader.SetGlobalFloat("_CloudHoleCount", 0f);
            if (CloudHitsEnabled)
                SyncHitBoxes(current, true);
            return;
        }

        PublishBoardAxes();
        int count = 0;
        for (int i = 0; i < world.Stages.Count && count < MaxHoles; i++)
        {
            StageDefinition stage = world.Stages[i];
            if (stage == null || !stage.Enabled) continue;
            if (current != null && stage.StageId == current.StageId)
            {
                AddHole(stage, ref count);
                continue;
            }

            if (RunSession.IsExplored(stage.StageId))
                AddHole(stage, ref count);
        }

        Shader.SetGlobalFloat("_CloudHoleCount", count);
        Shader.SetGlobalVectorArray("_CloudHoles", holes);
        // 略外扩并收紧软边，相邻关卡洞重叠，避免接缝留下细云线。
        Shader.SetGlobalFloat("_CloudHoleSoft", 0.2f);
        Shader.SetGlobalFloat("_CloudHoleNoise", 0.05f);
        Shader.SetGlobalFloat("_CloudHoleRound", 0.15f);
        if (CloudHitsEnabled)
            SyncHitBoxes(current, false);
    }

    public static WorldCloudPatch FromHit(RaycastHit hit)
    {
        if (hit.collider == null) return null;
        return hit.collider.GetComponentInParent<WorldCloudPatch>();
    }

    private void OnDestroy()
    {
        Shader.SetGlobalFloat("_CloudHoleCount", 0f);
        Shader.SetGlobalTexture("_CloudTerrainHeightTex", null);
        if (terrainHeightTex != null) Destroy(terrainHeightTex);
        if (fillMaterial != null) Destroy(fillMaterial);
    }

    private void AddHole(StageDefinition stage, ref int count)
    {
        if (stage == null || count >= MaxHoles || board == null || world == null) return;
        StageCenter(stage.GridX, stage.GridY, out Vector3 center, out float halfW, out float halfH);
        holes[count] = new Vector4(center.x, center.z, halfW, halfH);
        count++;
    }

    private void PublishBoardAxes()
    {
        Transform root = board != null ? board.transform : null;
        Vector3 right = root != null ? root.right : Vector3.right;
        Vector3 forward = root != null ? root.forward : Vector3.forward;
        Shader.SetGlobalVector("_CloudBoardRight", new Vector4(right.x, right.z, 0f, 0f));
        Shader.SetGlobalVector("_CloudBoardForward", new Vector4(forward.x, forward.z, 0f, 0f));
    }

    private void EnsureSea()
    {
        if (sea != null) return;
        sea = GameObject.Find("CloudSea");
        if (sea == null)
        {
            GameObject prefab = Resources.Load<GameObject>(PrefabResource);
            if (prefab == null)
            {
                Debug.LogWarning("找不到 CloudSea 预制件 Resources/" + PrefabResource + "。", this);
                return;
            }

            sea = Instantiate(prefab);
            sea.name = "CloudSea";
        }

        for (int i = 0; i < sea.transform.childCount; i++)
            sea.transform.GetChild(i).gameObject.SetActive(true);

        MeshCollider[] colliders = sea.GetComponentsInChildren<MeshCollider>(true);
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null) colliders[i].enabled = false;

        Renderer[] renderers = sea.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            renderers[i].shadowCastingMode = ShadowCastingMode.Off;
            renderers[i].receiveShadows = true;
        }
    }

    private void PublishTerrainHeights()
    {
        if (board == null) return;
        int w = Mathf.Max(1, board.Width);
        int h = Mathf.Max(1, board.Height);
        if (terrainHeightTex == null || terrainHeightTex.width != w || terrainHeightTex.height != h)
        {
            if (terrainHeightTex != null) Destroy(terrainHeightTex);
            terrainHeightTex = new Texture2D(w, h, TextureFormat.RFloat, false, true)
            {
                name = "CloudTerrainHeight",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
        }

        Color[] pixels = new Color[w * h];
        for (int z = 0; z < h; z++)
        {
            for (int x = 0; x < w; x++)
            {
                Vector3 p = board.EvaluateTilePosition(board.OriginWorldX + x, board.OriginWorldZ + z);
                pixels[z * w + x] = new Color(p.y + WorldTerrain.StepY, 0f, 0f, 1f);
            }
        }

        terrainHeightTex.SetPixels(pixels);
        terrainHeightTex.Apply(false, false);
        Vector3 origin = board.EvaluateTilePosition(board.OriginWorldX, board.OriginWorldZ);
        Shader.SetGlobalTexture("_CloudTerrainHeightTex", terrainHeightTex);
        Shader.SetGlobalVector("_CloudTerrainOrigin", new Vector4(origin.x, origin.z, board.Spacing, 1f));
        Shader.SetGlobalVector("_CloudTerrainSize", new Vector4(w, h, 0f, 0f));
    }

    private void FitSea()
    {
        if (sea == null || world == null || board == null) return;
        StageCenter(world.BoundsMinX, world.BoundsMinY, out Vector3 minCenter, out _, out _);
        StageCenter(world.BoundsMaxX, world.BoundsMaxY, out Vector3 maxCenter, out _, out _);
        Vector3 center = (minCenter + maxCenter) * 0.5f;
        float boardW = world.WorldTerrainWidth * board.Spacing;
        float boardH = world.WorldTerrainHeight * board.Spacing;
        float size = Mathf.Max(boardW, boardH) * 2.4f;
        float scale = size / PrefabPlaneSize;
        float topY = Mathf.Max(minCenter.y, maxCenter.y) + 0.4f;
        sea.transform.SetParent(null, true);
        sea.transform.position = new Vector3(center.x, topY, center.z);
        sea.transform.rotation = WorldBoardPose.Of(board.transform);
        sea.transform.localScale = new Vector3(scale, 1f, scale);
        sea.SetActive(true);
    }

    private void RebuildHitBoxes()
    {
        for (int i = 0; i < patches.Count; i++)
            if (patches[i] != null) Destroy(patches[i].gameObject);
        patches.Clear();
        if (hitRoot != null) Destroy(hitRoot.gameObject);
        if (world == null || board == null) return;
        hitRoot = new GameObject("CloudHits").transform;
        hitRoot.SetParent(transform, false);
        int tw = Mathf.Max(1, world.TerrainWidth);
        int th = Mathf.Max(1, world.TerrainHeight);
        float sizeX = tw * board.Spacing;
        float sizeZ = th * board.Spacing;
        float bottomY = WorldTerrain.MinHeight * WorldTerrain.StepY - 1.5f;
        float topY = DeckY + 0.35f;
        float fillHeight = Mathf.Max(1f, topY - bottomY);
        for (int i = 0; i < world.Stages.Count; i++)
        {
            StageDefinition stage = world.Stages[i];
            if (stage == null || !stage.Enabled) continue;
            StageCenter(stage.GridX, stage.GridY, out Vector3 center, out _, out _);
            GameObject box = new GameObject("CloudHit_" + stage.StageId);
            box.transform.SetParent(hitRoot, false);
            box.transform.position = new Vector3(center.x, DeckY, center.z);
            box.transform.rotation = WorldBoardPose.Of(board.transform);
            BoxCollider collider = box.AddComponent<BoxCollider>();
            collider.size = new Vector3(sizeX, 3.2f, sizeZ);
            WorldCloudPatch patch = box.AddComponent<WorldCloudPatch>();
            patch.GridX = stage.GridX;
            patch.GridY = stage.GridY;
            patch.HasStage = true;
            patch.StageId = stage.StageId;
            patches.Add(patch);

            // 竖直填充：未解锁关卡地形被藏后，用雾块堵住斜视空洞。
            GameObject fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fill.name = FillChildName;
            fill.transform.SetParent(box.transform, false);
            fill.transform.localPosition = new Vector3(0f, (bottomY + topY) * 0.5f - DeckY, 0f);
            fill.transform.localRotation = Quaternion.identity;
            fill.transform.localScale = new Vector3(sizeX * 1.02f, fillHeight, sizeZ * 1.02f);
            Collider fillCollider = fill.GetComponent<Collider>();
            if (fillCollider != null) Destroy(fillCollider);
            MeshRenderer fillRenderer = fill.GetComponent<MeshRenderer>();
            if (fillRenderer != null)
            {
                fillRenderer.sharedMaterial = ResolveFillMaterial();
                fillRenderer.shadowCastingMode = ShadowCastingMode.Off;
                fillRenderer.receiveShadows = false;
                fillRenderer.lightProbeUsage = LightProbeUsage.Off;
                fillRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }

            fill.SetActive(false);
        }
    }

    private void SyncHitBoxes(StageDefinition current, bool combat)
    {
        for (int i = 0; i < patches.Count; i++)
        {
            WorldCloudPatch patch = patches[i];
            if (patch == null) continue;
            WorldCatalog.TryGetStage(world, patch.StageId, out StageDefinition stage);
            bool explored = stage != null && RunSession.IsExplored(stage.StageId);
            bool cloudy = WorldCloudRules.HasCloudOver(current, stage, explored, combat);
            Collider hit = patch.GetComponent<Collider>();
            if (hit != null) hit.enabled = cloudy && !combat;

            Transform fill = patch.transform.Find(FillChildName);
            if (fill != null) fill.gameObject.SetActive(cloudy && !combat);
        }
    }

    private Material ResolveFillMaterial()
    {
        if (fillMaterial != null) return fillMaterial;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                        Shader.Find("Unlit/Color") ??
                        Shader.Find("Sprites/Default");
        fillMaterial = new Material(shader) { name = "CloudFill" };
        // 贴近云海雾色，不透明，挡住空洞背景。
        Color fog = new Color(0.78f, 0.82f, 0.88f, 1f);
        fillMaterial.color = fog;
        if (fillMaterial.HasProperty("_BaseColor")) fillMaterial.SetColor("_BaseColor", fog);
        if (fillMaterial.HasProperty("_Color")) fillMaterial.SetColor("_Color", fog);
        return fillMaterial;
    }

    private void StageCenter(int gx, int gy, out Vector3 center, out float halfW, out float halfH)
    {
        int tw = Mathf.Max(1, world.TerrainWidth);
        int th = Mathf.Max(1, world.TerrainHeight);
        Vector3 c00 = board.EvaluateTilePosition(gx * tw, gy * th);
        Vector3 c10 = board.EvaluateTilePosition(gx * tw + tw - 1, gy * th);
        Vector3 c01 = board.EvaluateTilePosition(gx * tw, gy * th + th - 1);
        Vector3 c11 = board.EvaluateTilePosition(gx * tw + tw - 1, gy * th + th - 1);
        center = (c00 + c10 + c01 + c11) * 0.25f;
        center.y = Mathf.Max(c00.y, c10.y, c01.y, c11.y);
        // 半宽贴合关卡；软边只向外淡出，接缝不会留线。
        halfW = tw * board.Spacing * 0.5f;
        halfH = th * board.Spacing * 0.5f;
    }
}
