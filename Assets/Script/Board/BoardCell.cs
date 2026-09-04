using UnityEngine;

/// <summary>
/// 单个棋盘格的数据与视觉控制组件。
/// 保存逻辑坐标，并负责创建、显示和隐藏移动范围覆盖层。
/// </summary>
public class BoardCell : MonoBehaviour
{
    private const string HighlightName = "MoveRangeHighlight";

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private GameObject moveHighlight;
    private Renderer moveHighlightRenderer;
    private Renderer terrainRenderer;
    private Renderer[] terrainRenderers;
    private Color[] baseColors;
    private MaterialPropertyBlock tintBlock;

    public Vector2Int Coordinate { get; private set; }
    public string StageId { get; private set; }
    public int Height { get; private set; }
    public string TerrainId { get; private set; }

    public void Initialize(int x, int y, string stageId = null, int height = 0, string terrainId = null)
    {
        Coordinate = new Vector2Int(x, y);
        StageId = stageId ?? string.Empty;
        Height = height;
        TerrainId = terrainId ?? string.Empty;
        name = string.IsNullOrEmpty(StageId) ? $"Cell_{x}_{y}" : $"Cell_{StageId}_{x}_{y}";
        CaptureBaseColor();
        RestLocalPosition = transform.localPosition;
        RestLocalScale = transform.localScale;
    }

    public void SetTerrainIdentity(string terrainId, Color terrainColor)
    {
        TerrainId = terrainId ?? string.Empty;
        EnsureTerrainRenderers();
        if (baseColors == null) return;
        for (int i = 0; i < baseColors.Length; i++)
            baseColors[i] = terrainColor;
        SetTerrainTint(Color.white);
    }

    public Vector3 RestLocalPosition { get; private set; }
    public Vector3 RestLocalScale { get; private set; }

    /// <summary>用 PropertyBlock 给整块格子上的所有网格染色，子物体草地也会一起变暗。</summary>
    public void SetTerrainTint(Color tint)
    {
        EnsureTerrainRenderers();
        if (terrainRenderers == null || terrainRenderers.Length == 0) return;
        tintBlock ??= new MaterialPropertyBlock();
        for (int i = 0; i < terrainRenderers.Length; i++)
        {
            Renderer renderer = terrainRenderers[i];
            if (renderer == null) continue;
            renderer.GetPropertyBlock(tintBlock);
            Color color = new Color(
                baseColors[i].r * tint.r,
                baseColors[i].g * tint.g,
                baseColors[i].b * tint.b,
                baseColors[i].a * tint.a);
            tintBlock.SetColor(BaseColorId, color);
            tintBlock.SetColor(ColorId, color);
            tintBlock.SetColor(EmissionColorId, Color.black);
            renderer.SetPropertyBlock(tintBlock);
        }
    }

    private void CaptureBaseColor()
    {
        terrainRenderers = null;
        EnsureTerrainRenderers();
        terrainRenderer = terrainRenderers != null && terrainRenderers.Length > 0 ? terrainRenderers[0] : null;
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
        baseColors = new Color[count];
        int index = 0;
        for (int i = 0; i < all.Length; i++)
        {
            Renderer renderer = all[i];
            if (!IsTerrainRenderer(renderer)) continue;
            terrainRenderers[index] = renderer;
            baseColors[index] = ReadBaseColor(renderer);
            index++;
        }
    }

    private static bool IsTerrainRenderer(Renderer renderer)
    {
        if (renderer == null || renderer is LineRenderer) return false;
        return renderer.gameObject.name != HighlightName;
    }

    private static Color ReadBaseColor(Renderer renderer)
    {
        Material shared = renderer.sharedMaterial;
        if (shared == null) return Color.white;
        if (shared.HasProperty(BaseColorId)) return shared.GetColor(BaseColorId);
        if (shared.HasProperty(ColorId)) return shared.GetColor(ColorId);
        return shared.color;
    }

    /// <summary>设置格子的移动/攻击范围高亮。草地格本身有渲染器，必须用独立覆盖层，不能只改格子染色。</summary>
    public void SetMoveHighlight(bool visible, Color color)
    {
        if (moveHighlight == null)
        {
            CreateMoveHighlight(color);
        }

        if (moveHighlightRenderer != null)
        {
            ApplyColor(moveHighlightRenderer.material, color);
        }

        if (moveHighlight != null)
        {
            moveHighlight.SetActive(visible);
        }
    }

    private void CreateMoveHighlight(Color color)
    {
        moveHighlight = GameObject.CreatePrimitive(PrimitiveType.Cube);
        moveHighlight.name = HighlightName;
        moveHighlight.transform.SetParent(transform, false);
        moveHighlight.transform.localRotation = Quaternion.identity;
        PlaceHighlightOnTile();

        Collider highlightCollider = moveHighlight.GetComponent<Collider>();
        if (highlightCollider != null)
        {
            Destroy(highlightCollider);
        }

        moveHighlightRenderer = moveHighlight.GetComponent<Renderer>();
        Material material = CreateHighlightMaterial(color);
        moveHighlightRenderer.material = material;
        moveHighlightRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        moveHighlightRenderer.receiveShadows = false;
        moveHighlight.SetActive(false);
    }

    /// <summary>
    /// 覆盖层 XZ 永远跟格子一样是 1×1。高度只取地形子物体顶面，不用世界包围盒对角点反推尺寸——
    /// 那种算法会把扁草地的 AABB 收成一根棍（X 被夹成 0.01、Z 拉成 2）。
    /// </summary>
    private void PlaceHighlightOnTile()
    {
        const float plateThickness = 0.03f;
        const float lift = 0.02f;
        moveHighlight.transform.localPosition = new Vector3(0f, ResolveTerrainTopLocalY() + plateThickness * 0.5f + lift, 0f);
        moveHighlight.transform.localScale = new Vector3(1f, plateThickness, 1f);
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
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        material.color = color;
    }

    private void OnDestroy()
    {
        if (moveHighlight != null)
        {
            Renderer highlightRenderer = moveHighlight.GetComponent<Renderer>();
            if (highlightRenderer != null)
            {
                Destroy(highlightRenderer.material);
            }
        }
    }
}
