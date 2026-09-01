using UnityEngine;

/// <summary>
/// 棋子占位网格：侧面和外底一个子网格，顶面单独一个子网格，方便运行时分材质。
/// </summary>
public static class CombatantTokenMesh
{
    public const float Width = 0.8f;
    public const float Height = 0.25f;

    /// <summary>创建或复用运行时棋子网格。</summary>
    public static Mesh Create()
    {
        Mesh mesh = new Mesh { name = "CombatantToken" };
        float x = Width * 0.5f;
        float y = Height * 0.5f;
        float z = Width * 0.5f;

        Vector3[] vertices =
        {
            new Vector3(-x, -y, -z), new Vector3(-x, y, -z), new Vector3(x, y, -z), new Vector3(x, -y, -z),
            new Vector3(x, -y, z), new Vector3(x, y, z), new Vector3(-x, y, z), new Vector3(-x, -y, z),
            new Vector3(-x, y, -z), new Vector3(-x, y, z), new Vector3(x, y, z), new Vector3(x, y, -z),
            new Vector3(-x, -y, -z), new Vector3(x, -y, -z), new Vector3(x, -y, z), new Vector3(-x, -y, z),
            new Vector3(-x, -y, z), new Vector3(-x, y, z), new Vector3(-x, y, -z), new Vector3(-x, -y, -z),
            new Vector3(x, -y, -z), new Vector3(x, y, -z), new Vector3(x, y, z), new Vector3(x, -y, z)
        };

        Vector2[] uvs =
        {
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f),
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f),
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f),
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f),
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f),
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f)
        };

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.subMeshCount = 2;
        mesh.SetTriangles(new[]
        {
            0, 1, 2, 0, 2, 3,
            4, 5, 6, 4, 6, 7,
            12, 13, 14, 12, 14, 15,
            16, 17, 18, 16, 18, 19,
            20, 21, 22, 20, 22, 23
        }, 0);
        mesh.SetTriangles(new[] { 8, 9, 10, 8, 10, 11 }, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
