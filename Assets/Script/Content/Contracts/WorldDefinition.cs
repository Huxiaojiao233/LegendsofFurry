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
    public int FormatVersion { get; set; } = 1;
    public string WorldId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Mode { get; set; } = "finite";
    public int Seed { get; set; }
    public string GeneratorId { get; set; } = string.Empty;
    public int BoundsMinX { get; set; }
    public int BoundsMinY { get; set; }
    public int StageGridWidth { get; set; } = 5;
    public int StageGridHeight { get; set; } = 5;
    public int TerrainWidth { get; set; } = 10;
    public int TerrainHeight { get; set; } = 10;
    public string StartStageId { get; set; } = string.Empty;
    public int StartTileX { get; set; } = 5;
    public int StartTileY { get; set; } = 5;
    public List<StageDefinition> Stages { get; set; } = new List<StageDefinition>();

    public string GetDefinitionKind() => "world";
    public string GetDefinitionId() => WorldId;

    public int BoundsMaxX => BoundsMinX + StageGridWidth - 1;
    public int BoundsMaxY => BoundsMinY + StageGridHeight - 1;
    public int OriginWorldX => BoundsMinX * TerrainWidth;
    public int OriginWorldY => BoundsMinY * TerrainHeight;
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
    public string[] TerrainIds { get; set; } = Array.Empty<string>();
    public List<WorldDecorationDefinition> Decorations { get; set; } = new List<WorldDecorationDefinition>();
    public List<WorldUnitPlacementDefinition> UnitPlacements { get; set; } = new List<WorldUnitPlacementDefinition>();
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

    public string TerrainAt(int localX, int localY, int terrainWidth, int terrainHeight)
    {
        if (TerrainIds == null || TerrainIds.Length != terrainWidth * terrainHeight)
            return WorldTerrainCatalog.Grass;
        if (localX < 0 || localY < 0 || localX >= terrainWidth || localY >= terrainHeight)
            return WorldTerrainCatalog.Grass;
        string id = TerrainIds[localY * terrainWidth + localX];
        return string.IsNullOrEmpty(id) ? WorldTerrainCatalog.Grass : id;
    }

    public void SetHeight(int localX, int localY, int terrainWidth, int terrainHeight, int height)
    {
        EnsureGrids(terrainWidth, terrainHeight);
        if (localX < 0 || localY < 0 || localX >= terrainWidth || localY >= terrainHeight) return;
        Heights[localY * terrainWidth + localX] = MathfClampHeight(height);
    }

    public void SetTerrain(int localX, int localY, int terrainWidth, int terrainHeight, string terrainId)
    {
        EnsureGrids(terrainWidth, terrainHeight);
        if (localX < 0 || localY < 0 || localX >= terrainWidth || localY >= terrainHeight) return;
        TerrainIds[localY * terrainWidth + localX] = string.IsNullOrEmpty(terrainId)
            ? WorldTerrainCatalog.Grass
            : terrainId;
    }

    public void EnsureGrids(int terrainWidth, int terrainHeight)
    {
        int length = Math.Max(1, terrainWidth) * Math.Max(1, terrainHeight);
        int[] heights = Heights ?? Array.Empty<int>();
        if (heights.Length != length)
        {
            heights = new int[length];
            Heights = heights;
        }

        if (TerrainIds == null || TerrainIds.Length != length)
        {
            string[] ids = new string[length];
            for (int i = 0; i < length; i++)
                ids[i] = WorldTerrainCatalog.FromHeight(heights[i]);
            TerrainIds = ids;
        }
    }

    private static int MathfClampHeight(int height)
    {
        if (height < WorldTerrain.MinHeight) return WorldTerrain.MinHeight;
        if (height > WorldTerrain.MaxHeight) return WorldTerrain.MaxHeight;
        return height;
    }
}

/// <summary>关卡只保存单位实例的部署信息，静态数值始终引用内容包 Unit。</summary>
public sealed class WorldUnitPlacementDefinition
{
    public string InstanceId { get; set; } = string.Empty;
    public string UnitId { get; set; } = string.Empty;
    public int LocalX { get; set; }
    public int LocalY { get; set; }
    public string FactionOverride { get; set; } = string.Empty;
    public string ControllerOverride { get; set; } = string.Empty;
    public string DeckIdOverride { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

/// <summary>块内稀疏装饰。运行时用简单几何体占位，不引入美术资源。</summary>
public sealed class WorldDecorationDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Definition { get; set; } = string.Empty;
    public int LocalX { get; set; }
    public int LocalY { get; set; }
    public int Rotation { get; set; }
}

/// <summary>按大世界坐标生成高度，保证相邻关卡接缝处高度连续。</summary>
public static class WorldTerrain
{
    public const int MinHeight = -5;
    public const int MaxHeight = 10;
    /// <summary>一层高度对应的世界 Y。地形方块高度是 0.5，抬高/降低一格就移动这么多。</summary>
    public const float StepY = 0.5f;

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

        foreach (StageDefinition stage in world.Stages)
            stage.EnsureGrids(tw, th);
    }

    /// <summary>西低东高、南侧略降一层，范围夹在 MinHeight 到 MaxHeight。</summary>
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
