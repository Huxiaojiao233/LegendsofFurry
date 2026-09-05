using UnityEngine;

/// <summary>
/// 单个棋盘格的数据与视觉控制组件。
/// 保存逻辑坐标，并负责创建、显示和隐藏移动范围覆盖层。
/// 地形外观由各自 Prefab 的模型决定，不再染色。
/// </summary>
public class BoardCell : MonoBehaviour
{
    private const string HighlightName = "MoveRangeHighlight";

    private GameObject moveHighlight;
    private Renderer moveHighlightRenderer;
    private GameObject hoverHighlight;
    private Renderer hoverHighlightRenderer;
    private GameObject intentHighlight;
    private Renderer intentHighlightRenderer;
    private Renderer[] terrainRenderers;
    private bool hovered;
    private float hoverPulse;

    public Vector2Int Coordinate { get; private set; }
    public string StageId { get; private set; }
    public int Height { get; private set; }
    public string TerrainId { get; private set; }
    /// <summary>由世界迷雾/关卡状态决定的基础地形可见性。Chunk LOD 不会覆盖这个状态。</summary>
    public bool BaseTerrainVisible { get; private set; } = true;

    public void Initialize(int x, int y, string stageId = null, int height = 0, string terrainId = null)
    {
        Coordinate = new Vector2Int(x, y);
        StageId = stageId ?? string.Empty;
        Height = height;
        TerrainId = terrainId ?? string.Empty;
        BaseTerrainVisible = true;
        name = string.IsNullOrEmpty(StageId) ? $"Cell_{x}_{y}" : $"Cell_{StageId}_{x}_{y}";
        terrainRenderers = null;
        RestLocalPosition = transform.localPosition;
        RestLocalScale = transform.localScale;
    }

    public Vector3 RestLocalPosition { get; private set; }
    public Vector3 RestLocalScale { get; private set; }

    /// <summary>开关地形网格显示（迷雾/战外关卡隐藏），不改材质颜色。</summary>
    public void SetTerrainVisible(bool visible)
    {
        BaseTerrainVisible = visible;
        EnsureTerrainRenderers();
        if (terrainRenderers == null) return;
        for (int i = 0; i < terrainRenderers.Length; i++)
        {
            if (terrainRenderers[i] != null)
                terrainRenderers[i].enabled = visible;
        }
    }

    /// <summary>Chunk LOD 用：切换地形之外的小型装饰，不影响逻辑格和碰撞体。</summary>
    public void SetSmallDecorationsVisible(bool visible)
    {
        EnsureTerrainRenderers();
        Renderer[] all = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Renderer renderer = all[i];
            if (renderer == null || IsTerrainRenderer(renderer)) continue;
            renderer.enabled = visible;
        }
    }

    /// <summary>Chunk LOD 用：在基础可见性之上隐藏/恢复所有地形渲染器。</summary>
    public void SetChunkTerrainVisible(bool visible)
    {
        EnsureTerrainRenderers();
        if (terrainRenderers == null) return;
        bool shouldShow = visible && BaseTerrainVisible;
        for (int i = 0; i < terrainRenderers.Length; i++)
            if (terrainRenderers[i] != null) terrainRenderers[i].enabled = shouldShow;
    }

    /// <summary>远景 Chunk 不参与鼠标射线，逻辑寻路仍使用 BoardCell 数据。</summary>
    public void SetChunkColliderEnabled(bool enabled)
    {
        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || collider.GetComponentInParent<WorldDecorationVisual>() != null)
                continue;
            collider.enabled = enabled;
        }
    }

    /// <summary>设置格子的移动/攻击范围高亮。草地格本身有渲染器，必须用独立覆盖层。</summary>
    public void SetMoveHighlight(bool visible, Color color)
    {
        EnsureOverlay(ref moveHighlight, ref moveHighlightRenderer, HighlightName, color, 0.03f, 0.02f);
        if (moveHighlightRenderer != null) ApplyColor(moveHighlightRenderer.material, color);
        if (moveHighlight != null) moveHighlight.SetActive(visible);
    }

    /// <summary>鼠标悬浮高亮；Update 里做轻微起伏与呼吸。</summary>
    public void SetHovered(bool value)
    {
        hovered = value;
        if (!value)
        {
            hoverPulse = 0f;
            transform.localPosition = RestLocalPosition;
            if (hoverHighlight != null) hoverHighlight.SetActive(false);
            return;
        }

        Color color = new Color(1f, 0.92f, 0.45f, 0.72f);
        EnsureOverlay(ref hoverHighlight, ref hoverHighlightRenderer, "HoverHighlight", color, 0.04f, 0.035f);
        if (hoverHighlightRenderer != null) ApplyColor(hoverHighlightRenderer.material, color);
        if (hoverHighlight != null) hoverHighlight.SetActive(true);
    }

    /// <summary>敌方攻击意图落点高亮，与移动范围覆盖层分开。</summary>
    public void SetIntentHighlight(bool visible, Color color)
    {
        EnsureOverlay(ref intentHighlight, ref intentHighlightRenderer, "IntentHighlight", color, 0.035f, 0.05f);
        if (intentHighlightRenderer != null) ApplyColor(intentHighlightRenderer.material, color);
        if (intentHighlight != null) intentHighlight.SetActive(visible);
    }

    private void Update()
    {
        if (!hovered) return;
        hoverPulse += Time.unscaledDeltaTime * 6.5f;
        float wave = (Mathf.Sin(hoverPulse) + 1f) * 0.5f;
        transform.localPosition = RestLocalPosition + Vector3.up * (0.04f + wave * 0.05f);
        if (hoverHighlight != null)
        {
            float thickness = 0.035f + wave * 0.02f;
            hoverHighlight.transform.localScale = new Vector3(0.92f + wave * 0.08f, thickness, 0.92f + wave * 0.08f);
            if (hoverHighlightRenderer != null)
            {
                Color color = Color.Lerp(
                    new Color(1f, 0.85f, 0.35f, 0.55f),
                    new Color(1f, 0.98f, 0.7f, 0.9f),
                    wave);
                ApplyColor(hoverHighlightRenderer.material, color);
            }
        }
    }

    private void EnsureTerrainRenderers()
    {
        if (terrainRenderers != null) return;
        Renderer[] all = GetComponentsInChildren<Renderer>(true);
        int count = 0;
        for (int i = 0; i < all.Length; i++)
        {
            if (IsTerrainRenderer(all[i])) count++;
        }

        terrainRenderers = new Renderer[count];
        int index = 0;
        for (int i = 0; i < all.Length; i++)
        {
            Renderer renderer = all[i];
            if (!IsTerrainRenderer(renderer)) continue;
            terrainRenderers[index++] = renderer;
        }
    }

    private static bool IsTerrainRenderer(Renderer renderer)
    {
        if (renderer == null || renderer is LineRenderer) return false;
        if (renderer.GetComponentInParent<WorldDecorationVisual>() != null) return false;
        string objectName = renderer.gameObject.name;
        return objectName != HighlightName &&
               objectName != "HoverHighlight" &&
               objectName != "IntentHighlight";
    }

    private void EnsureOverlay(ref GameObject overlay, ref Renderer overlayRenderer, string objectName, Color color,
        float thickness, float lift)
    {
        if (overlay != null) return;
        overlay = GameObject.CreatePrimitive(PrimitiveType.Cube);
        overlay.name = objectName;
        overlay.transform.SetParent(transform, false);
        overlay.transform.localRotation = Quaternion.identity;
        PlaceOverlay(overlay, thickness, lift);
        Collider overlayCollider = overlay.GetComponent<Collider>();
        if (overlayCollider != null) Destroy(overlayCollider);
        overlayRenderer = overlay.GetComponent<Renderer>();
        overlayRenderer.material = CreateHighlightMaterial(color);
        overlayRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        overlayRenderer.receiveShadows = false;
        overlay.SetActive(false);
    }

    private void PlaceOverlay(GameObject overlay, float thickness, float lift)
    {
        if (overlay == null) return;
        overlay.transform.localPosition = new Vector3(0f, ResolveTerrainTopLocalY() + thickness * 0.5f + lift, 0f);
        overlay.transform.localScale = new Vector3(1f, thickness, 1f);
    }

    private float ResolveTerrainTopLocalY()
    {
        EnsureTerrainRenderers();
        float topY = 0.5f;
        bool found = false;
        if (terrainRenderers == null) return topY;
        for (int i = 0; i < terrainRenderers.Length; i++)
        {
            Renderer renderer = terrainRenderers[i];
            if (renderer == null) continue;
            float candidate = renderer.transform.localPosition.y;
            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
            if (mesh != null)
            {
                Vector3 worldTop = renderer.transform.TransformPoint(new Vector3(
                    mesh.bounds.center.x,
                    mesh.bounds.max.y,
                    mesh.bounds.center.z));
                candidate = transform.InverseTransformPoint(worldTop).y;
            }

            if (!found || candidate > topY)
            {
                topY = candidate;
                found = true;
            }
        }

        return topY;
    }

    private static Material CreateHighlightMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                        Shader.Find("Sprites/Default");
        Material material = shader != null
            ? new Material(shader)
            : new Material(Resources.GetBuiltinResource<Material>("Default-Material.mat"));
        material.renderQueue = 3100;
        ApplyColor(material, color);
        return material;
    }

    private static void ApplyColor(Material material, Color color)
    {
        if (material == null) return;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        material.color = color;
    }

    private void OnDestroy()
    {
        DestroyOverlayMaterial(moveHighlight);
        DestroyOverlayMaterial(hoverHighlight);
        DestroyOverlayMaterial(intentHighlight);
        if (BoardTileHover.Current == this) BoardTileHover.Clear();
    }

    private static void DestroyOverlayMaterial(GameObject overlay)
    {
        if (overlay == null) return;
        Renderer highlightRenderer = overlay.GetComponent<Renderer>();
        if (highlightRenderer != null && highlightRenderer.material != null)
            Destroy(highlightRenderer.material);
    }
}
