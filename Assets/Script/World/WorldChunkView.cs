using LegendsOfFurry.Content.Contracts;
using System.Collections.Generic;
using UnityEngine;

/// <summary>单个区块的 BoardCell、装饰、单位预览和生命周期。卸载时整块销毁。</summary>
public sealed class WorldChunkView : MonoBehaviour
{
    public ChunkPosition Position { get; private set; }
    public StageDefinition Stage { get; private set; }
    public int CellCount { get; private set; }
    private GameObject contoursObject;
    private GameObject boundsObject;

    public void Build(BoardGenerator board, StageDefinition stage)
    {
        Stage = stage;
        Position = new ChunkPosition(stage.GridX, stage.GridY);
        name = $"Chunk_{Position}";
        CellCount = 0;
        if (board == null || stage == null) return;
        int tw = board.WorldSource != null ? Mathf.Max(1, board.WorldSource.TerrainWidth) : 10;
        int th = board.WorldSource != null ? Mathf.Max(1, board.WorldSource.TerrainHeight) : 10;
        stage.EnsureGrids(tw, th);
        for (int localY = 0; localY < th; localY++)
        {
            for (int localX = 0; localX < tw; localX++)
            {
                int worldX = stage.GridX * tw + localX;
                int worldZ = stage.GridY * th + localY;
                string terrainId = stage.TerrainAt(localX, localY, tw, th);
                if (terrainId == WorldTerrainCatalog.Void) continue;
                var tile = new BoardGenerator.TileRecord
                {
                    Exists = true,
                    Height = stage.HeightAt(localX, localY, tw, th),
                    TerrainId = terrainId,
                    StageId = stage.StageId
                };
                BoardCell cell = board.SpawnCell(worldX, worldZ, tile, transform);
                if (cell != null) CellCount++;
            }
        }

        if (stage.Decorations != null && board.WorldSource != null)
        {
            for (int i = 0; i < stage.Decorations.Count; i++)
            {
                WorldDecorationDefinition deco = stage.Decorations[i];
                if (deco == null || string.IsNullOrWhiteSpace(deco.Definition)) continue;
                Vector2Int world = WorldLayout.ToBoard(stage, deco.LocalX, deco.LocalY, board.WorldSource);
                if (!board.TryGetCell(world.x, world.y, out BoardCell host)) continue;
                WorldDecorationCatalog.Spawn(deco, host.transform);
            }
        }

        WorldUnitPreview.SpawnAll(board, stage);
        RebuildWalls(board);
    }

    public void RebuildWalls(BoardGenerator board)
    {
        if (board?.WorldSource == null) return;
        int tw = Mathf.Max(1, board.WorldSource.TerrainWidth);
        int th = Mathf.Max(1, board.WorldSource.TerrainHeight);
        WorldFoundationWalls.RebuildChunk(transform, board, Position, tw, th);
    }

    /// <summary>编辑辅助线仅作用于已驻留区块，和流式渲染生命周期一致。</summary>
    public void SetEditorOverlays(BoardGenerator board, bool showContours, bool showBounds)
    {
        if (showContours) BuildContours(board);
        else SetOverlayActive(contoursObject, false);
        if (showBounds) BuildBounds(board);
        else SetOverlayActive(boundsObject, false);
    }

    private void BuildContours(BoardGenerator board)
    {
        if (board == null || Stage == null) return;
        int tw = Mathf.Max(1, board.WorldSource.TerrainWidth);
        int th = Mathf.Max(1, board.WorldSource.TerrainHeight);
        var lines = new List<Vector3>();
        float half = board.Spacing * 0.5f;
        for (int y = 0; y < th; y++)
        for (int x = 0; x < tw; x++)
        {
            int height = Stage.HeightAt(x, y, tw, th);
            Vector3 p = board.CellLocalPosition(Position.X * tw + x, Position.Y * th + y, height) +
                        Vector3.up * (WorldTerrain.StepY + 0.08f);
            if (x == tw - 1 || Stage.HeightAt(x + 1, y, tw, th) != height)
                AddLine(lines, p + new Vector3(half, 0f, -half), p + new Vector3(half, 0f, half));
            if (y == th - 1 || Stage.HeightAt(x, y + 1, tw, th) != height)
                AddLine(lines, p + new Vector3(-half, 0f, half), p + new Vector3(half, 0f, half));
            if (x == 0)
                AddLine(lines, p + new Vector3(-half, 0f, -half), p + new Vector3(-half, 0f, half));
            if (y == 0)
                AddLine(lines, p + new Vector3(-half, 0f, -half), p + new Vector3(half, 0f, -half));
        }
        contoursObject = BuildLineMesh("EditorContours", contoursObject, lines, new Color(0.95f, 0.85f, 0.2f, 0.85f));
    }

    private void BuildBounds(BoardGenerator board)
    {
        if (board == null || Stage == null) return;
        int tw = Mathf.Max(1, board.WorldSource.TerrainWidth);
        int th = Mathf.Max(1, board.WorldSource.TerrainHeight);
        var lines = new List<Vector3>();
        float half = board.Spacing * 0.5f;
        for (int x = 0; x < tw; x++)
        {
            AddBoundaryLine(board, lines, x, 0, tw, th, half, true, false);
            AddBoundaryLine(board, lines, x, th - 1, tw, th, half, true, true);
        }
        for (int y = 0; y < th; y++)
        {
            AddBoundaryLine(board, lines, 0, y, tw, th, half, false, false);
            AddBoundaryLine(board, lines, tw - 1, y, tw, th, half, false, true);
        }
        boundsObject = BuildLineMesh("EditorChunkBounds", boundsObject, lines, new Color(0.2f, 0.85f, 1f, 0.95f));
    }

    private void AddBoundaryLine(BoardGenerator board, List<Vector3> lines, int localX, int localY,
        int tw, int th, float half, bool horizontal, bool farSide)
    {
        int height = Stage.HeightAt(localX, localY, tw, th);
        Vector3 p = board.CellLocalPosition(Position.X * tw + localX, Position.Y * th + localY, height) +
                    Vector3.up * (WorldTerrain.StepY + 0.1f);
        if (horizontal)
        {
            float z = farSide ? half : -half;
            AddLine(lines, p + new Vector3(-half, 0f, z), p + new Vector3(half, 0f, z));
        }
        else
        {
            float x = farSide ? half : -half;
            AddLine(lines, p + new Vector3(x, 0f, -half), p + new Vector3(x, 0f, half));
        }
    }

    private GameObject BuildLineMesh(string overlayName, GameObject existing, List<Vector3> lines, Color color)
    {
        GameObject target = existing;
        if (target == null)
        {
            target = new GameObject(overlayName, typeof(MeshFilter), typeof(MeshRenderer));
            target.transform.SetParent(transform, false);
        }
        Mesh mesh = new Mesh { name = overlayName + "Mesh" };
        var vertices = new List<Vector3>(lines.Count * 2);
        var triangles = new List<int>(lines.Count * 3);
        const float halfWidth = 0.035f;
        for (int i = 0; i + 1 < lines.Count; i += 2)
        {
            Vector3 a = lines[i];
            Vector3 b = lines[i + 1];
            // 从上方观察时保持三角形正面朝上，避免被材质背面剔除。
            Vector3 side = Vector3.Cross(b - a, Vector3.up).normalized * halfWidth;
            int start = vertices.Count;
            vertices.Add(a - side); vertices.Add(a + side); vertices.Add(b + side); vertices.Add(b - side);
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
        }
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        MeshFilter filter = target.GetComponent<MeshFilter>();
        if (filter.sharedMesh != null) Destroy(filter.sharedMesh);
        filter.sharedMesh = mesh;
        MeshRenderer renderer = target.GetComponent<MeshRenderer>();
        if (renderer.sharedMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Standard");
            renderer.sharedMaterial = new Material(shader);
        }
        renderer.sharedMaterial.color = color;
        if (renderer.sharedMaterial.HasProperty("_BaseColor"))
            renderer.sharedMaterial.SetColor("_BaseColor", color);
        renderer.sharedMaterial.renderQueue = 3100;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.enabled = true;
        target.SetActive(true);
        return target;
    }

    private static void AddLine(List<Vector3> lines, Vector3 a, Vector3 b)
    {
        lines.Add(a); lines.Add(b);
    }

    private static void SetOverlayActive(GameObject value, bool active)
    {
        if (value != null) value.SetActive(active);
    }

    public void Teardown(BoardGenerator board)
    {
        if (board != null && board.WorldSource != null)
        {
            int tw = Mathf.Max(1, board.WorldSource.TerrainWidth);
            int th = Mathf.Max(1, board.WorldSource.TerrainHeight);
            board.UnregisterCellsInChunk(Position, tw, th);
        }

        CellCount = 0;
    }
}
