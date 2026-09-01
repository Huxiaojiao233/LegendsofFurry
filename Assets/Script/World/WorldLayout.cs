using System.Collections.Generic;
using LegendsOfFurry.Content.Contracts;
using UnityEngine;

/// <summary>
/// 把关卡格子坐标换算成 S_Battle 棋盘坐标：boardX = GridX * TerrainWidth + localX。
/// </summary>
public static class WorldLayout
{
    public static Vector2Int StageOrigin(StageDefinition stage, WorldDefinition world)
    {
        int tw = Mathf.Max(1, world.TerrainWidth);
        int th = Mathf.Max(1, world.TerrainHeight);
        return new Vector2Int(stage.GridX * tw, stage.GridY * th);
    }

    public static Vector2Int ToBoard(StageDefinition stage, int localX, int localY, WorldDefinition world)
    {
        Vector2Int origin = StageOrigin(stage, world);
        return new Vector2Int(origin.x + localX, origin.y + localY);
    }

    public static Vector2Int StageCenterCell(StageDefinition stage, WorldDefinition world)
    {
        int tw = Mathf.Max(1, world.TerrainWidth);
        int th = Mathf.Max(1, world.TerrainHeight);
        return ToBoard(stage, tw / 2, th / 2, world);
    }

    public static bool ContainsCell(StageDefinition stage, WorldDefinition world, int boardX, int boardZ)
    {
        if (stage == null || world == null) return false;
        Vector2Int origin = StageOrigin(stage, world);
        return boardX >= origin.x && boardX < origin.x + world.TerrainWidth &&
               boardZ >= origin.y && boardZ < origin.y + world.TerrainHeight;
    }

    public static bool TryGetStage(WorldDefinition world, int boardX, int boardZ, out StageDefinition stage)
    {
        stage = null;
        if (world == null) return false;
        int tw = Mathf.Max(1, world.TerrainWidth);
        int th = Mathf.Max(1, world.TerrainHeight);
        if (boardX < 0 || boardZ < 0) return false;
        return WorldCatalog.TryGetStageAt(world, boardX / tw, boardZ / th, out stage);
    }

    public static bool IsOrthogonalNeighbor(StageDefinition a, StageDefinition b)
    {
        if (a == null || b == null) return false;
        return Mathf.Abs(a.GridX - b.GridX) + Mathf.Abs(a.GridY - b.GridY) == 1;
    }

    /// <summary>当前关卡上下左右四个相邻关卡（等距 45° 视角下看起来像斜向）。</summary>
    public static List<StageDefinition> OrthogonalNeighbors(WorldDefinition world, StageDefinition stage)
    {
        List<StageDefinition> neighbors = new List<StageDefinition>(4);
        if (world == null || stage == null) return neighbors;
        TryAdd(world, stage.GridX, stage.GridY + 1, neighbors);
        TryAdd(world, stage.GridX + 1, stage.GridY, neighbors);
        TryAdd(world, stage.GridX, stage.GridY - 1, neighbors);
        TryAdd(world, stage.GridX - 1, stage.GridY, neighbors);
        return neighbors;
    }

    private static void TryAdd(WorldDefinition world, int gridX, int gridY, List<StageDefinition> list)
    {
        if (WorldCatalog.TryGetStageAt(world, gridX, gridY, out StageDefinition neighbor))
            list.Add(neighbor);
    }
}
