#nullable enable
using System;
using System.Collections.Generic;

namespace LegendsOfFurry.Content.Contracts
{
/// <summary>关卡种类稳定 key，供内容 JSON 与运行时共用。</summary>
public static class ContentStageTypeKeys
{
    public const string Start = "start";
    public const string Battle = "battle";
    public const string Rest = "rest";
    public const string Reward = "reward";
    public const string Shop = "shop";
    public const string Boss = "boss";

    public static bool IsKnown(string? value) =>
        value == Start || value == Battle || value == Rest || value == Reward ||
        value == Shop || value == Boss;
}

/// <summary>一张大地图：由关卡格子拼成，每个关卡格子再由地形单位格子拼成。</summary>
public sealed class WorldDefinition : IContentDefinition
{
    public string WorldId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int StageGridWidth { get; set; } = 5;
    public int StageGridHeight { get; set; } = 5;
    public int TerrainWidth { get; set; } = 10;
    public int TerrainHeight { get; set; } = 10;
    public string StartStageId { get; set; } = string.Empty;
    public List<StageDefinition> Stages { get; set; } = new List<StageDefinition>();

    public string GetDefinitionKind() => "world";
    public string GetDefinitionId() => WorldId;

    public int WorldTerrainWidth => StageGridWidth * TerrainWidth;
    public int WorldTerrainHeight => StageGridHeight * TerrainHeight;
}

/// <summary>一个关卡格子：类型、在大地图上的坐标、10x10 高度层，以及可选战斗/掉落。</summary>
public sealed class StageDefinition
{
    public string StageId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string StageType { get; set; } = ContentStageTypeKeys.Battle;
    public int GridX { get; set; }
    public int GridY { get; set; }
    public int[] Heights { get; set; } = Array.Empty<int>();
    public List<string> EnemyCharacterIds { get; set; } = new List<string>();
    public string RewardPoolId { get; set; } = string.Empty;
    public string RequiredKeyId { get; set; } = string.Empty;
    public string DropKeyId { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    public int HeightAt(int localX, int localY, int terrainWidth, int terrainHeight)
    {
        if (Heights == null || Heights.Length != terrainWidth * terrainHeight) return 0;
        if (localX < 0 || localY < 0 || localX >= terrainWidth || localY >= terrainHeight) return 0;
        return Heights[localY * terrainWidth + localX];
    }
}

/// <summary>按大世界坐标生成高度，保证相邻关卡接缝处高度连续。</summary>
public static class WorldTerrain
{
    public const int MinHeight = -1;
    public const int MaxHeight = 2;

    /// <summary>为缺高度的关卡按同一张大世界坡度填 10x10，使边界必然相接。</summary>
    public static void FillMissingHeights(WorldDefinition world)
    {
        if (world == null) return;
        int tw = Math.Max(1, world.TerrainWidth);
        int th = Math.Max(1, world.TerrainHeight);
        foreach (StageDefinition stage in world.Stages)
        {
            if (stage.Heights != null && stage.Heights.Length == tw * th) continue;
            stage.Heights = new int[tw * th];
            for (int y = 0; y < th; y++)
            {
                for (int x = 0; x < tw; x++)
                {
                    int worldX = stage.GridX * tw + x;
                    int worldY = stage.GridY * th + y;
                    stage.Heights[y * tw + x] = HeightAt(world, worldX, worldY);
                }
            }
        }
    }

    /// <summary>西低东高、南侧略降一层，范围夹在 -1 到 2。</summary>
    public static int HeightAt(WorldDefinition world, int worldX, int worldY)
    {
        int maxX = Math.Max(1, world.WorldTerrainWidth - 1);
        float t = worldX / (float)maxX;
        int band = t < 0.28f ? 0 : t < 0.62f ? 1 : 2;
        if (worldY < world.TerrainHeight * 0.4f) band = band < MinHeight + 1 ? MinHeight : band - 1;
        if (band < MinHeight) band = MinHeight;
        if (band > MaxHeight) band = MaxHeight;
        return band;
    }

    /// <summary>检查左右、上下相邻关卡共享边的高度是否一致。</summary>
    public static List<string> FindBorderMismatches(WorldDefinition world)
    {
        List<string> issues = new List<string>();
        if (world == null) return issues;
        int tw = Math.Max(1, world.TerrainWidth);
        int th = Math.Max(1, world.TerrainHeight);
        Dictionary<(int x, int y), StageDefinition> byGrid = new Dictionary<(int, int), StageDefinition>();
        foreach (StageDefinition stage in world.Stages)
        {
            if (stage == null || !stage.Enabled) continue;
            byGrid[(stage.GridX, stage.GridY)] = stage;
        }

        foreach (StageDefinition stage in byGrid.Values)
        {
            if (byGrid.TryGetValue((stage.GridX + 1, stage.GridY), out StageDefinition east))
            {
                for (int y = 0; y < th; y++)
                {
                    int left = stage.HeightAt(tw - 1, y, tw, th);
                    int right = east.HeightAt(0, y, tw, th);
                    if (left != right)
                        issues.Add($"{stage.StageId} 东接 {east.StageId} 在 y={y} 高度 {left}/{right} 不一致。");
                }
            }

            if (byGrid.TryGetValue((stage.GridX, stage.GridY + 1), out StageDefinition north))
            {
                for (int x = 0; x < tw; x++)
                {
                    int south = stage.HeightAt(x, th - 1, tw, th);
                    int up = north.HeightAt(x, 0, tw, th);
                    if (south != up)
                        issues.Add($"{stage.StageId} 北接 {north.StageId} 在 x={x} 高度 {south}/{up} 不一致。");
                }
            }
        }

        return issues;
    }
}
}
