using System.Collections.Generic;
using LegendsOfFurry.Content.Contracts;
using UnityEngine;

/// <summary>用种子和柏林噪声生成有限世界的高度、地形、装饰和敌人部署。</summary>
public static class WorldNoiseGenerator
{
    public const string GeneratorId = "perlin.v1";

    public static void Generate(WorldDefinition world, WorldNoiseSettings settings)
    {
        if (world == null) throw new System.ArgumentNullException(nameof(world));
        settings ??= new WorldNoiseSettings();
        world.Seed = settings.Seed;
        world.GeneratorId = GeneratorId;
        int tw = Mathf.Max(1, world.TerrainWidth);
        int th = Mathf.Max(1, world.TerrainHeight);
        foreach (StageDefinition stage in world.Stages)
        {
            if (stage == null) continue;
            FillStageTerrain(world, stage, tw, th, settings);
            stage.Decorations.Clear();
            stage.UnitPlacements.Clear();
            stage.RewardPoolId = string.Empty;
            stage.RequiredKeyId = string.Empty;
            stage.DropKeyId = string.Empty;
        }

        SyncSeamHeights(world, tw, th, settings);
        AssignStageRoles(world, settings);
        PlaceDecorations(world, settings);
        PlaceEnemies(world, settings);
        ChooseStartTile(world);
    }

    private static void FillStageTerrain(WorldDefinition world, StageDefinition stage, int tw, int th,
        WorldNoiseSettings settings)
    {
        stage.EnsureGrids(tw, th);
        for (int y = 0; y < th; y++)
        {
            for (int x = 0; x < tw; x++)
            {
                int wx = stage.GridX * tw + x;
                int wy = stage.GridY * th + y;
                float heightNoise = Fractal(wx, wy, settings.HeightFrequency, settings.Seed, 4);
                float moist = Fractal(wx, wy, settings.MoistureFrequency, settings.Seed + 91, 3);
                int height = Mathf.RoundToInt(Mathf.Lerp(WorldTerrain.MinHeight, WorldTerrain.MaxHeight, heightNoise));
                height = Mathf.Clamp(height, WorldTerrain.MinHeight, WorldTerrain.MaxHeight);
                stage.SetHeight(x, y, tw, th, height);
                stage.SetTerrain(x, y, tw, th, ChooseTerrain(height, moist, settings));
            }
        }
    }

    private static void SyncSeamHeights(WorldDefinition world, int tw, int th, WorldNoiseSettings settings)
    {
        Dictionary<(int, int), StageDefinition> byGrid = new Dictionary<(int, int), StageDefinition>();
        foreach (StageDefinition stage in EnabledStages(world))
            byGrid[(stage.GridX, stage.GridY)] = stage;

        foreach (StageDefinition stage in byGrid.Values)
        {
            if (byGrid.TryGetValue((stage.GridX + 1, stage.GridY), out StageDefinition east))
            {
                for (int y = 0; y < th; y++)
                {
                    int height = stage.HeightAt(tw - 1, y, tw, th);
                    east.SetHeight(0, y, tw, th, height);
                    float moist = Fractal(east.GridX * tw, east.GridY * th + y, settings.MoistureFrequency,
                        settings.Seed + 91, 3);
                    east.SetTerrain(0, y, tw, th, ChooseTerrain(height, moist, settings));
                }
            }

            if (byGrid.TryGetValue((stage.GridX, stage.GridY + 1), out StageDefinition north))
            {
                for (int x = 0; x < tw; x++)
                {
                    int height = stage.HeightAt(x, th - 1, tw, th);
                    north.SetHeight(x, 0, tw, th, height);
                    float moist = Fractal(north.GridX * tw + x, north.GridY * th, settings.MoistureFrequency,
                        settings.Seed + 91, 3);
                    north.SetTerrain(x, 0, tw, th, ChooseTerrain(height, moist, settings));
                }
            }
        }
    }

    private static string ChooseTerrain(int height, float moist, WorldNoiseSettings settings)
    {
        if (height <= WorldTerrain.MinHeight + 1 && moist > settings.WaterLevel) return WorldTerrainCatalog.Water;
        if (height < 0) return moist > 0.5f ? WorldTerrainCatalog.Water : WorldTerrainCatalog.Sand;
        if (height >= 8) return WorldTerrainCatalog.Stone;
        if (height >= 6) return moist > 0.45f ? WorldTerrainCatalog.Stone : WorldTerrainCatalog.Dirt;
        if (moist > settings.ForestLevel && height <= 5) return WorldTerrainCatalog.Forest;
        if (moist < 0.32f) return height <= 1 ? WorldTerrainCatalog.Sand : WorldTerrainCatalog.Dirt;
        if (height >= 3) return WorldTerrainCatalog.Dirt;
        return WorldTerrainCatalog.Grass;
    }

    private static void AssignStageRoles(WorldDefinition world, WorldNoiseSettings settings)
    {
        List<StageDefinition> stages = EnabledStages(world);
        if (stages.Count == 0) return;
        StageDefinition start = stages[0];
        float best = float.MaxValue;
        Vector2 center = new Vector2(
            (world.BoundsMinX + world.BoundsMaxX) * 0.5f,
            (world.BoundsMinY + world.BoundsMaxY) * 0.5f);
        foreach (StageDefinition stage in stages)
        {
            float distance = Mathf.Abs(stage.GridX - center.x) + Mathf.Abs(stage.GridY - center.y);
            if (distance >= best) continue;
            best = distance;
            start = stage;
        }

        StageDefinition boss = start;
        float far = -1f;
        foreach (StageDefinition stage in stages)
        {
            float distance = Mathf.Abs(stage.GridX - start.GridX) + Mathf.Abs(stage.GridY - start.GridY);
            if (distance <= far) continue;
            far = distance;
            boss = stage;
        }

        foreach (StageDefinition stage in stages)
        {
            float roll = Hash01(stage.GridX, stage.GridY, settings.Seed + 3);
            if (stage == start)
            {
                stage.StageId = "start";
                stage.DisplayName = "营地";
                stage.StageType = ContentStageTypeKeys.Start;
            }
            else if (stage == boss && stages.Count > 1)
            {
                stage.StageId = "boss-1";
                stage.DisplayName = "魔王巢穴";
                stage.StageType = ContentStageTypeKeys.Boss;
                stage.RequiredKeyId = RunSession.BossKeyId;
            }
            else if (roll < 0.08f)
            {
                stage.StageId = $"shop-{stage.GridX}-{stage.GridY}";
                stage.DisplayName = "商队营地";
                stage.StageType = ContentStageTypeKeys.Shop;
            }
            else if (roll < 0.16f)
            {
                stage.StageId = $"reward-{stage.GridX}-{stage.GridY}";
                stage.DisplayName = "藏宝处";
                stage.StageType = ContentStageTypeKeys.Reward;
            }
            else if (roll < 0.3f)
            {
                stage.StageId = $"rest-{stage.GridX}-{stage.GridY}";
                stage.DisplayName = "歇脚处";
                stage.StageType = ContentStageTypeKeys.Rest;
            }
            else
            {
                stage.StageId = $"fight-{stage.GridX}-{stage.GridY}";
                stage.DisplayName = "遭遇地";
                stage.StageType = ContentStageTypeKeys.Battle;
            }
        }

        world.StartStageId = start.StageId;
        if (boss != null && boss != start && boss.StageType == ContentStageTypeKeys.Boss)
            AssignKeyDrop(stages, start, boss, settings);
    }

    private static void AssignKeyDrop(List<StageDefinition> stages, StageDefinition start, StageDefinition boss,
        WorldNoiseSettings settings)
    {
        StageDefinition drop = null;
        float best = float.MaxValue;
        foreach (StageDefinition stage in stages)
        {
            if (stage.StageType != ContentStageTypeKeys.Battle) continue;
            float toBoss = Mathf.Abs(stage.GridX - boss.GridX) + Mathf.Abs(stage.GridY - boss.GridY);
            float toStart = Mathf.Abs(stage.GridX - start.GridX) + Mathf.Abs(stage.GridY - start.GridY);
            float score = toBoss + toStart * 0.25f + Hash01(stage.GridX, stage.GridY, settings.Seed + 11) * 0.1f;
            if (score >= best) continue;
            best = score;
            drop = stage;
        }

        if (drop == null) return;
        drop.DropKeyId = RunSession.BossKeyId;
        drop.DisplayName = "钥匙遭遇";
    }

    private static void PlaceDecorations(WorldDefinition world, WorldNoiseSettings settings)
    {
        int tw = Mathf.Max(1, world.TerrainWidth);
        int th = Mathf.Max(1, world.TerrainHeight);
        foreach (StageDefinition stage in EnabledStages(world))
        {
            for (int y = 0; y < th; y++)
            {
                for (int x = 0; x < tw; x++)
                {
                    int wx = stage.GridX * tw + x;
                    int wy = stage.GridY * th + y;
                    string terrain = stage.TerrainAt(x, y, tw, th);
                    if (terrain == WorldTerrainCatalog.Void || terrain == WorldTerrainCatalog.Water) continue;
                    float chance = Hash01(wx, wy, settings.Seed + 21);
                    float field = Fractal(wx, wy, settings.DecorFrequency, settings.Seed + 27, 2);
                    if (stage.StageType == ContentStageTypeKeys.Start && x == tw / 2 && y == th / 2)
                    {
                        stage.Decorations.Add(MakeDecor(stage, x, y, WorldTerrainCatalog.Camp));
                        continue;
                    }

                    if (terrain == WorldTerrainCatalog.Forest && chance < settings.DecorDensity + field * 0.12f)
                        stage.Decorations.Add(MakeDecor(stage, x, y, WorldTerrainCatalog.Tree));
                    else if (terrain == WorldTerrainCatalog.Stone && chance < settings.DecorDensity * 0.55f)
                        stage.Decorations.Add(MakeDecor(stage, x, y, WorldTerrainCatalog.Rock));
                    else if (terrain == WorldTerrainCatalog.Grass && chance < settings.DecorDensity * 0.25f)
                        stage.Decorations.Add(MakeDecor(stage, x, y, WorldTerrainCatalog.Tree));
                }
            }
        }
    }

    private static void PlaceEnemies(WorldDefinition world, WorldNoiseSettings settings)
    {
        int tw = Mathf.Max(1, world.TerrainWidth);
        int th = Mathf.Max(1, world.TerrainHeight);
        foreach (StageDefinition stage in EnabledStages(world))
        {
            bool boss = stage.StageType == ContentStageTypeKeys.Boss;
            bool battle = stage.StageType == ContentStageTypeKeys.Battle;
            if (!boss && !battle) continue;
            // 每个战斗/Boss 关卡固定部署一只敌人；密度只影响后续扩展，不跳过整关。
            if (!TryFindOpenCell(stage, tw, th, settings, out int x, out int y)) continue;
            string unitId = boss ? settings.BossUnitId : settings.EnemyUnitId;
            stage.UnitPlacements.Add(new WorldUnitPlacementDefinition
            {
                InstanceId = $"{stage.StageId}-{unitId}-1",
                UnitId = unitId,
                LocalX = x,
                LocalY = y,
                FactionOverride = "enemy",
                ControllerOverride = "ai",
                Enabled = true
            });
        }
    }

    private static void ChooseStartTile(WorldDefinition world)
    {
        if (!TryGetStart(world, out StageDefinition start)) return;
        int tw = Mathf.Max(1, world.TerrainWidth);
        int th = Mathf.Max(1, world.TerrainHeight);
        int cx = tw / 2;
        int cy = th / 2;
        if (IsOpen(start, cx, cy, tw, th))
        {
            world.StartTileX = cx;
            world.StartTileY = cy;
            return;
        }

        for (int radius = 1; radius < Mathf.Max(tw, th); radius++)
        {
            for (int y = cy - radius; y <= cy + radius; y++)
            for (int x = cx - radius; x <= cx + radius; x++)
            {
                if (!IsOpen(start, x, y, tw, th)) continue;
                world.StartTileX = x;
                world.StartTileY = y;
                return;
            }
        }
    }

    private static bool TryFindOpenCell(StageDefinition stage, int tw, int th, WorldNoiseSettings settings,
        out int x, out int y)
    {
        int bestX = tw / 2;
        int bestY = th / 2;
        float best = float.MinValue;
        bool found = false;
        for (int ly = 0; ly < th; ly++)
        {
            for (int lx = 0; lx < tw; lx++)
            {
                if (!IsOpen(stage, lx, ly, tw, th)) continue;
                if (FindDecoration(stage, lx, ly)) continue;
                float score = Hash01(lx + stage.GridX * 17, ly + stage.GridY * 13, settings.Seed + 53);
                if (score <= best) continue;
                best = score;
                bestX = lx;
                bestY = ly;
                found = true;
            }
        }

        x = bestX;
        y = bestY;
        return found;
    }

    private static bool IsOpen(StageDefinition stage, int x, int y, int tw, int th)
    {
        if (x < 0 || y < 0 || x >= tw || y >= th) return false;
        string terrain = stage.TerrainAt(x, y, tw, th);
        return WorldTerrainCatalog.IsWalkable(terrain) && terrain != WorldTerrainCatalog.Water;
    }

    private static bool FindDecoration(StageDefinition stage, int x, int y)
    {
        for (int i = 0; i < stage.Decorations.Count; i++)
        {
            WorldDecorationDefinition deco = stage.Decorations[i];
            if (deco != null && deco.LocalX == x && deco.LocalY == y) return true;
        }

        return false;
    }

    private static WorldDecorationDefinition MakeDecor(StageDefinition stage, int x, int y, string definition)
    {
        return new WorldDecorationDefinition
        {
            Id = $"deco-{stage.StageId}-{x}-{y}",
            Definition = definition,
            LocalX = x,
            LocalY = y
        };
    }

    private static bool TryGetStart(WorldDefinition world, out StageDefinition start)
    {
        start = null;
        foreach (StageDefinition stage in world.Stages)
        {
            if (stage == null || !stage.Enabled) continue;
            if (stage.StageId != world.StartStageId) continue;
            start = stage;
            return true;
        }

        return false;
    }

    private static List<StageDefinition> EnabledStages(WorldDefinition world)
    {
        List<StageDefinition> list = new List<StageDefinition>();
        foreach (StageDefinition stage in world.Stages)
            if (stage != null && stage.Enabled) list.Add(stage);
        return list;
    }

    private static float Fractal(float x, float y, float frequency, int seed, int octaves)
    {
        float sum = 0f;
        float norm = 0f;
        float amp = 1f;
        float freq = Mathf.Max(0.001f, frequency);
        float ox = (seed % 997) * 0.137f;
        float oy = (seed % 991) * 0.271f + 13.7f;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * Mathf.PerlinNoise(x * freq + ox, y * freq + oy);
            norm += amp;
            freq *= 2f;
            amp *= 0.5f;
            ox += 19.13f;
            oy += 37.91f;
        }

        return norm <= 0f ? 0f : Mathf.Clamp01(sum / norm);
    }

    private static float Hash01(int x, int y, int seed)
    {
        unchecked
        {
            int hash = x * 374761393 + y * 668265263 + seed * 1274126177;
            hash = (hash ^ (hash >> 13)) * 1274126177;
            hash ^= hash >> 16;
            return (hash & 0x7fffffff) / (float)int.MaxValue;
        }
    }
}
