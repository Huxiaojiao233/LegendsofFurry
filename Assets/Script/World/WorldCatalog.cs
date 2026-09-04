using System;
using System.Collections.Generic;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;
using UnityEngine;

/// <summary>
/// 从 StreamingAssets/Content/Worlds 读取大地图。优先分块目录 world.json+chunks/，其次旧单文件。
/// </summary>
public static class WorldCatalog
{
    private static readonly List<WorldDefinition> worlds = new List<WorldDefinition>();
    private static bool loaded;

    public static IReadOnlyList<WorldDefinition> Worlds
    {
        get
        {
            EnsureLoaded();
            return worlds;
        }
    }

    public static WorldDefinition Default
    {
        get
        {
            EnsureLoaded();
            if (worlds.Count == 0) return null;
            string preferred = ContentRuntime.IsLoaded
                ? ContentRuntime.Registry.GameSettings.DefaultWorldId
                : string.Empty;
            if (!string.IsNullOrEmpty(preferred))
            {
                for (int i = 0; i < worlds.Count; i++)
                    if (worlds[i].WorldId == preferred) return worlds[i];
            }
            return worlds[0];
        }
    }

    public static bool TryGet(string worldId, out WorldDefinition world)
    {
        EnsureLoaded();
        for (int i = 0; i < worlds.Count; i++)
        {
            if (worlds[i].WorldId == worldId)
            {
                world = worlds[i];
                return true;
            }
        }
        world = null;
        return false;
    }

    public static bool TryGetStage(WorldDefinition world, string stageId, out StageDefinition stage)
    {
        stage = null;
        if (world == null) return false;
        for (int i = 0; i < world.Stages.Count; i++)
        {
            if (world.Stages[i].StageId == stageId)
            {
                stage = world.Stages[i];
                return true;
            }
        }
        return false;
    }

    public static bool TryGetStageAt(WorldDefinition world, int gridX, int gridY, out StageDefinition stage)
    {
        stage = null;
        if (world == null) return false;
        for (int i = 0; i < world.Stages.Count; i++)
        {
            StageDefinition candidate = world.Stages[i];
            if (candidate.Enabled && candidate.GridX == gridX && candidate.GridY == gridY)
            {
                stage = candidate;
                return true;
            }
        }
        return false;
    }

    public static void Reload()
    {
        loaded = false;
        EnsureLoaded();
    }

    private static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        worlds.Clear();
        worlds.AddRange(WorldMapIO.LoadAll());

        if (worlds.Count == 0)
        {
            worlds.Add(CreateDemoWorld());
            Debug.Log("未找到自定义世界 JSON，已使用内置 demo 大地图。");
        }

        foreach (WorldDefinition world in worlds)
        {
            WorldTerrain.FillMissingHeights(world);
            List<string> mismatches = WorldTerrain.FindBorderMismatches(world);
            for (int i = 0; i < mismatches.Count; i++)
                Debug.LogError(mismatches[i]);
        }
    }

    /// <summary>demo 路线：营地→小怪1→奖励→小怪2→商店→小怪3(钥匙)→可探索小怪4→钥匙开魔王。</summary>
    public static WorldDefinition CreateDemoWorld()
    {
        WorldDefinition world = new WorldDefinition
        {
            WorldId = "demo",
            DisplayName = "试炼原野",
            StageGridWidth = 5,
            StageGridHeight = 5,
            TerrainWidth = 10,
            TerrainHeight = 10,
            StartStageId = "start"
        };
        world.Stages.Add(Stage("start", "营地", ContentStageTypeKeys.Start, 0, 2));
        world.Stages.Add(Stage("fight-1", "林缘斥候", ContentStageTypeKeys.Battle, 1, 2));
        world.Stages.Add(Stage("reward-1", "战利品堆", ContentStageTypeKeys.Reward, 2, 2));
        world.Stages.Add(Stage("fight-2", "河谷巡逻", ContentStageTypeKeys.Battle, 3, 2));
        world.Stages.Add(Stage("shop-1", "流浪商摊", ContentStageTypeKeys.Shop, 4, 2));
        StageDefinition keyed = Stage("fight-3", "钥匙守卫", ContentStageTypeKeys.Battle, 4, 1);
        keyed.DropKeyId = RunSession.BossKeyId;
        world.Stages.Add(keyed);
        world.Stages.Add(Stage("fight-4", "侧翼哨所", ContentStageTypeKeys.Battle, 2, 1));
        StageDefinition boss = Stage("boss-1", "魔王祭坛", ContentStageTypeKeys.Boss, 2, 3);
        boss.RequiredKeyId = RunSession.BossKeyId;
        world.Stages.Add(boss);
        WorldTerrain.FillMissingHeights(world);
        return world;
    }

    private static StageDefinition Stage(string id, string name, string type, int x, int y) =>
        new StageDefinition { StageId = id, DisplayName = name, StageType = type, GridX = x, GridY = y };
}
