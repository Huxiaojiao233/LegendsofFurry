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
    private Renderer cellRenderer;
    private MaterialPropertyBlock propertyBlock;

    public Vector2Int Coordinate { get; private set; }

    public void Initialize(int x, int y)
    {
        Coordinate = new Vector2Int(x, y);
        name = $"Cell_{x}_{y}";
    }

    /// <summary>设置格子的移动范围高亮以及本次高亮颜色。</summary>
    public void SetMoveHighlight(bool visible, Color color)
    {
        if (cellRenderer == null)
        {
            cellRenderer = FindCellRenderer();
        }

        if (cellRenderer != null)
        {
            SetRendererHighlight(visible, color);
            return;
        }

        if (moveHighlight == null)
        {
            CreateMoveHighlight(color);
        }

        ApplyColor(moveHighlightRenderer.material, color);
        moveHighlight.SetActive(visible);
    }

    private Renderer FindCellRenderer()
    {
        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            return renderer;
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != moveHighlightRenderer)
            {
                return renderers[i];
            }
        }

        return null;
    }

    private void SetRendererHighlight(bool visible, Color color)
    {
        propertyBlock ??= new MaterialPropertyBlock();
        if (!visible)
        {
            cellRenderer.SetPropertyBlock(null);
            return;
        }

        cellRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor("_BaseColor", color);
        propertyBlock.SetColor("_Color", color);
        cellRenderer.SetPropertyBlock(propertyBlock);
    }

    private void CreateMoveHighlight(Color color)
    {
        Renderer cellRenderer = GetComponent<Renderer>();
        if (cellRenderer == null)
        {
            cellRenderer = GetComponentInChildren<Renderer>();
        }

        moveHighlight = GameObject.CreatePrimitive(PrimitiveType.Cube);
        moveHighlight.name = HighlightName;
        moveHighlight.transform.SetParent(transform, false);
        moveHighlight.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        moveHighlight.transform.localRotation = Quaternion.identity;
        moveHighlight.transform.localScale = new Vector3(0.9f, 0.025f, 0.9f);

        Collider highlightCollider = moveHighlight.GetComponent<Collider>();
        if (highlightCollider != null)
        {
            Destroy(highlightCollider);
        }

        moveHighlightRenderer = moveHighlight.GetComponent<Renderer>();
        Material material = CreateHighlightMaterial(cellRenderer, moveHighlightRenderer, color);
        moveHighlightRenderer.material = material;
        moveHighlight.SetActive(false);
    }

    private static Material CreateHighlightMaterial(Renderer sourceRenderer, Renderer fallbackRenderer, Color color)
    {
        Material source = null;
        if (sourceRenderer != null && sourceRenderer.sharedMaterial != null)
        {
            source = sourceRenderer.sharedMaterial;
        }
        else if (fallbackRenderer != null && fallbackRenderer.sharedMaterial != null)
        {
            source = fallbackRenderer.sharedMaterial;
        }

        Material material = source != null ? new Material(source) : null;
        if (material == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            material = shader != null
                ? new Material(shader)
                : new Material(Resources.GetBuiltinResource<Material>("Default-Material.mat"));
        }

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
