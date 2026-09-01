using UnityEngine;

/// <summary>
/// 单个棋盘格的数据与视觉控制组件。
/// 保存逻辑坐标，并负责创建、显示和隐藏移动范围覆盖层。
/// </summary>
public class BoardCell : MonoBehaviour
{
    private const string HighlightName = "MoveRangeHighlight";

    private GameObject moveHighlight;
    private Renderer moveHighlightRenderer;

    public Vector2Int Coordinate { get; private set; }

    public void Initialize(int x, int y)
    {
        Coordinate = new Vector2Int(x, y);
        name = $"Cell_{x}_{y}";
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
        Renderer tileRenderer = GetComponent<Renderer>();
        if (tileRenderer == null)
        {
            tileRenderer = GetComponentInChildren<Renderer>();
        }

        moveHighlight = GameObject.CreatePrimitive(PrimitiveType.Cube);
        moveHighlight.name = HighlightName;
        moveHighlight.transform.SetParent(transform, false);
        moveHighlight.transform.localRotation = Quaternion.identity;
        PlaceHighlightOnTile(tileRenderer);

        Collider highlightCollider = moveHighlight.GetComponent<Collider>();
        if (highlightCollider != null)
        {
            Destroy(highlightCollider);
        }

        moveHighlightRenderer = moveHighlight.GetComponent<Renderer>();
        Material material = CreateHighlightMaterial(color);
        moveHighlightRenderer.material = material;
        moveHighlight.SetActive(false);
    }

    /// <summary>覆盖层与草地格同一平面尺寸，只在顶面加一层薄板。</summary>
    private void PlaceHighlightOnTile(Renderer tileRenderer)
    {
        Bounds localBounds = ResolveTileLocalBounds(tileRenderer);
        const float plateThickness = 0.02f;
        moveHighlight.transform.localPosition = new Vector3(
            localBounds.center.x,
            localBounds.max.y + plateThickness * 0.5f,
            localBounds.center.z);
        moveHighlight.transform.localScale = new Vector3(
            Mathf.Max(0.01f, localBounds.size.x),
            plateThickness,
            Mathf.Max(0.01f, localBounds.size.z));
    }

    private static Bounds ResolveTileLocalBounds(Renderer tileRenderer)
    {
        MeshFilter meshFilter = tileRenderer != null
            ? tileRenderer.GetComponent<MeshFilter>()
            : null;
        Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
        if (mesh != null)
        {
            return mesh.bounds;
        }

        if (tileRenderer != null)
        {
            return tileRenderer.localBounds;
        }

        return new Bounds(Vector3.zero, new Vector3(1f, 0.25f, 1f));
    }

    private static Material CreateHighlightMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                        Shader.Find("Sprites/Default");
        Material material = shader != null
            ? new Material(shader)
            : new Material(Resources.GetBuiltinResource<Material>("Default-Material.mat"));
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
