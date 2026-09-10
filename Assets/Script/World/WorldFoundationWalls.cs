using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using LegendsOfFurry.Content.Contracts;

/// <summary>
/// 用一张合并网格画悬崖和地图边缘的地基墙，不生成额外地形方块。
/// 材质使用 Terrain/Resources 下的 Dirt。
/// </summary>
public static class WorldFoundationWalls
{
    public const string ChildName = "FoundationWalls";
    private const string DirtMaterialResource = "Materials/Dirt";
    private const string DirtTextureResource = "Dirt";
    private static readonly Vector2Int[] EdgeOffsets =
    {
        new Vector2Int(1, 0),
        new Vector2Int(-1, 0),
        new Vector2Int(0, 1),
        new Vector2Int(0, -1)
    };

    private static Material cachedDirtMaterial;

    public static void Rebuild(Transform root, int[,] boardMap, int[,] cellHeights, float spacing,
        float heightStep)
    {
        if (root == null || boardMap == null || cellHeights == null) return;
        Clear(root);

        int mapHeight = boardMap.GetLength(0);
        int mapWidth = boardMap.GetLength(1);
        if (mapWidth <= 0 || mapHeight <= 0) return;
        if (cellHeights.GetLength(0) != mapWidth || cellHeights.GetLength(1) != mapHeight) return;

        List<Vector3> vertices = new List<Vector3>(mapWidth * mapHeight * 8);
        List<Vector2> uvs = new List<Vector2>(mapWidth * mapHeight * 8);
        List<int> triangles = new List<int>(mapWidth * mapHeight * 12);
        int skirtFloor = WorldTerrain.MinHeight - 1;
        float half = spacing * 0.5f;

        for (int z = 0; z < mapHeight; z++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                if (boardMap[z, x] == 0) continue;
                int thisHeight = cellHeights[x, z];
                float posX = (x - (mapWidth - 1) * 0.5f) * spacing;
                float posZ = (z - (mapHeight - 1) * 0.5f) * spacing;

                for (int edge = 0; edge < EdgeOffsets.Length; edge++)
                {
                    Vector2Int offset = EdgeOffsets[edge];
                    int nx = x + offset.x;
                    int nz = z + offset.y;
                    int neighborHeight = skirtFloor;
                    if (nx >= 0 && nz >= 0 && nx < mapWidth && nz < mapHeight && boardMap[nz, nx] != 0)
                        neighborHeight = cellHeights[nx, nz];
                    if (thisHeight <= neighborHeight) continue;

                    float y0 = neighborHeight * heightStep;
                    float y1 = thisHeight * heightStep;
                    EmitWall(vertices, uvs, triangles, posX, posZ, half, y0, y1, offset.x, offset.y);
                }
            }
        }

        CommitMesh(root, vertices, uvs, triangles);
    }

    /// <summary>按已驻留瓦片画一块的墙。缺席邻块当悬崖，等邻块加载后再刷接缝。</summary>
    public static void RebuildChunk(Transform root, BoardGenerator board, ChunkPosition chunk, int tw, int th)
    {
        if (root == null || board == null) return;
        Clear(root);
        tw = Mathf.Max(1, tw);
        th = Mathf.Max(1, th);
        List<Vector3> vertices = new List<Vector3>(tw * th * 8);
        List<Vector2> uvs = new List<Vector2>(tw * th * 8);
        List<int> triangles = new List<int>(tw * th * 12);
        int skirtFloor = WorldTerrain.MinHeight - 1;
        float half = board.Spacing * 0.5f;
        float heightStep = board.HeightStep;

        for (int localY = 0; localY < th; localY++)
        {
            for (int localX = 0; localX < tw; localX++)
            {
                int worldX = chunk.X * tw + localX;
                int worldZ = chunk.Y * th + localY;
                if (!TryGetVisibleTile(board, worldX, worldZ, out BoardGenerator.TileRecord tile))
                    continue;
                Vector3 local = board.CellLocalPosition(worldX, worldZ, 0);
                for (int edge = 0; edge < EdgeOffsets.Length; edge++)
                {
                    Vector2Int offset = EdgeOffsets[edge];
                    int neighborHeight = skirtFloor;
                    if (TryGetVisibleTile(board, worldX + offset.x, worldZ + offset.y,
                            out BoardGenerator.TileRecord neighbor))
                        neighborHeight = neighbor.Height;
                    if (tile.Height <= neighborHeight) continue;
                    EmitWall(vertices, uvs, triangles, local.x, local.z, half,
                        neighborHeight * heightStep, tile.Height * heightStep, offset.x, offset.y);
                }
            }
        }

        CommitMesh(root, vertices, uvs, triangles);
    }

    private static bool TryGetVisibleTile(BoardGenerator board, int worldX, int worldZ,
        out BoardGenerator.TileRecord tile)
    {
        if (!board.TryPeekTile(worldX, worldZ, out tile) || !tile.Exists)
            return false;
        return board.TryGetCell(worldX, worldZ, out BoardCell cell) && cell != null &&
               cell.gameObject.activeInHierarchy && cell.TerrainVisible;
    }

    private static void CommitMesh(Transform root, List<Vector3> vertices, List<Vector2> uvs, List<int> triangles)
    {
        if (triangles.Count == 0) return;

        Mesh mesh = new Mesh { name = ChildName };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GameObject wallObject = new GameObject(ChildName);
        wallObject.transform.SetParent(root, false);
        wallObject.transform.localPosition = Vector3.zero;
        wallObject.transform.localRotation = Quaternion.identity;
        wallObject.transform.localScale = Vector3.one;
        MeshFilter filter = wallObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        MeshRenderer renderer = wallObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = ResolveDirtMaterial();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    public static void Clear(Transform root)
    {
        if (root == null) return;
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Transform child = root.GetChild(i);
            if (child == null || child.name != ChildName) continue;
            if (Application.isPlaying) Object.Destroy(child.gameObject);
            else Object.DestroyImmediate(child.gameObject);
        }
    }

    private static void EmitWall(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles,
        float centerX, float centerZ, float half, float y0, float y1, int dirX, int dirZ)
    {
        int start = vertices.Count;
        float width = half * 2f;
        float height = Mathf.Max(0.001f, y1 - y0);
        if (dirX != 0)
        {
            float wallX = centerX + dirX * half;
            float z0 = centerZ - half;
            float z1 = centerZ + half;
            if (dirX > 0)
            {
                vertices.Add(new Vector3(wallX, y0, z0));
                vertices.Add(new Vector3(wallX, y1, z0));
                vertices.Add(new Vector3(wallX, y1, z1));
                vertices.Add(new Vector3(wallX, y0, z1));
            }
            else
            {
                vertices.Add(new Vector3(wallX, y0, z1));
                vertices.Add(new Vector3(wallX, y1, z1));
                vertices.Add(new Vector3(wallX, y1, z0));
                vertices.Add(new Vector3(wallX, y0, z0));
            }
        }
        else
        {
            float wallZ = centerZ + dirZ * half;
            float x0 = centerX - half;
            float x1 = centerX + half;
            if (dirZ > 0)
            {
                vertices.Add(new Vector3(x1, y0, wallZ));
                vertices.Add(new Vector3(x1, y1, wallZ));
                vertices.Add(new Vector3(x0, y1, wallZ));
                vertices.Add(new Vector3(x0, y0, wallZ));
            }
            else
            {
                vertices.Add(new Vector3(x0, y0, wallZ));
                vertices.Add(new Vector3(x0, y1, wallZ));
                vertices.Add(new Vector3(x1, y1, wallZ));
                vertices.Add(new Vector3(x1, y0, wallZ));
            }
        }

        // 按墙面世界尺寸铺 Dirt 贴图，避免整面只有一种色。
        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(0f, height));
        uvs.Add(new Vector2(width, height));
        uvs.Add(new Vector2(width, 0f));

        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
    }

    private static Material ResolveDirtMaterial()
    {
        if (cachedDirtMaterial != null) return cachedDirtMaterial;

        cachedDirtMaterial = Resources.Load<Material>(DirtMaterialResource);
        if (cachedDirtMaterial != null) return cachedDirtMaterial;

        Texture2D dirt = Resources.Load<Texture2D>(DirtTextureResource);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
                        Shader.Find("Universal Render Pipeline/Unlit") ??
                        Shader.Find("Standard");
        cachedDirtMaterial = new Material(shader) { name = "FoundationWall_Dirt" };
        if (dirt != null)
        {
            if (cachedDirtMaterial.HasProperty("_BaseMap"))
                cachedDirtMaterial.SetTexture("_BaseMap", dirt);
            if (cachedDirtMaterial.HasProperty("_MainTex"))
                cachedDirtMaterial.SetTexture("_MainTex", dirt);
        }

        return cachedDirtMaterial;
    }
}
