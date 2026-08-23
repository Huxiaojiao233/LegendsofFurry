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

    /// <summary>设置格子的移动范围高亮以及本次高亮颜色。</summary>
    public void SetMoveHighlight(bool visible, Color color)
    {
        if (moveHighlight == null)
        {
            CreateMoveHighlight(color);
        }

        moveHighlightRenderer.material.color = color;
        moveHighlight.SetActive(visible);
    }

    private void CreateMoveHighlight(Color color)
    {
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

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        Material material = new Material(shader);
        material.color = color;
        moveHighlightRenderer = moveHighlight.GetComponent<Renderer>();
        moveHighlightRenderer.material = material;
        moveHighlight.SetActive(false);
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
