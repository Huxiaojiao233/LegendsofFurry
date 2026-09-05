using System;
using System.Collections.Generic;
using System.IO;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;
using UnityEngine;

/// <summary>
/// 一局冒险的内存状态和磁盘存档。场景切换不丢职业、血量、牌库、探索过的关卡和钥匙。
/// </summary>
public static class RunSession
{
    public const int SaveVersion = 1;
    public const string BossKeyId = "boss-key";

    private static RunSaveData current;

    public static bool HasActive => current != null && current.runActive;
    public static RunSaveData Current => current;

    public static string SavePath =>
        Path.Combine(Application.persistentDataPath, "Saves", "run.json");

    public static bool HasSaveFile => File.Exists(SavePath);

    /// <summary>选职业后开一局：只点亮起始关卡格子。</summary>
    public static void StartNew(string classId, WorldDefinition world)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
        ClassProfileDefinition profile = null;
        if (ContentRuntime.IsLoaded)
            ContentRuntime.Registry.TryGetClassProfile(classId, out profile);

        current = new RunSaveData
        {
            version = SaveVersion,
            runActive = true,
            classId = ContentId.Require(classId, nameof(classId)),
            worldId = world.WorldId,
            currentStageId = world.StartStageId,
            exploredStageIds = new[] { world.StartStageId },
            completedStageIds = Array.Empty<string>(),
            unlockedStageIds = Array.Empty<string>(),
            keys = Array.Empty<string>(),
            deckCardIds = Array.Empty<string>(),
            health = profile != null ? Mathf.Max(1, profile.InitialHealth) : 10,
            maxHealth = profile != null ? Mathf.Max(1, profile.InitialHealth) : 10,
            gold = 0
        };
        Persist();
    }

    /// <summary>从磁盘恢复；没有存档或版本不匹配时返回 false。</summary>
    public static bool TryLoad()
    {
        if (!File.Exists(SavePath)) return false;
        try
        {
            RunSaveData loaded = JsonUtility.FromJson<RunSaveData>(File.ReadAllText(SavePath));
            if (loaded == null || !loaded.runActive || loaded.version != SaveVersion) return false;
            if (!ContentId.IsValid(loaded.classId) || string.IsNullOrWhiteSpace(loaded.currentStageId))
                return false;
            current = loaded;
            current.exploredStageIds ??= Array.Empty<string>();
            current.completedStageIds ??= Array.Empty<string>();
            current.unlockedStageIds ??= Array.Empty<string>();
            current.keys ??= Array.Empty<string>();
            current.deckCardIds ??= Array.Empty<string>();
            GameSession.SelectClass(current.classId);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"读取存档失败：{exception.Message}");
            return false;
        }
    }

    public static void Clear()
    {
        current = null;
        if (File.Exists(SavePath)) File.Delete(SavePath);
    }

    public static void Persist()
    {
        if (current == null) return;
        string directory = Path.GetDirectoryName(SavePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(SavePath, JsonUtility.ToJson(current, true));
    }

    public static bool IsExplored(string stageId) =>
        HasActive && Contains(current.exploredStageIds, stageId);

    public static bool IsCompleted(string stageId) =>
        HasActive && Contains(current.completedStageIds, stageId);

    public static bool HasKey(string keyId) =>
        HasActive && Contains(current.keys, keyId);

    public static void Reveal(string stageId)
    {
        if (!HasActive || string.IsNullOrWhiteSpace(stageId) || IsExplored(stageId)) return;
        current.exploredStageIds = Append(current.exploredStageIds, stageId);
        Persist();
    }

    public static void MoveTo(string stageId)
    {
        if (!HasActive || string.IsNullOrWhiteSpace(stageId)) return;
        current.currentStageId = stageId;
        Reveal(stageId);
        Persist();
    }

    public static void SetDeck(IReadOnlyList<string> cardIds)
    {
        if (!HasActive) return;
        current.deckCardIds = cardIds == null ? Array.Empty<string>() : new List<string>(cardIds).ToArray();
        Persist();
    }

    public static void SetHealth(int health, int maxHealth)
    {
        if (!HasActive) return;
        current.maxHealth = Mathf.Max(1, maxHealth);
        current.health = Mathf.Clamp(health, 0, current.maxHealth);
        Persist();
    }

    public static void HealToFull()
    {
        if (!HasActive) return;
        current.health = current.maxHealth;
        Persist();
    }

    public static void AddCard(string cardId)
    {
        if (!HasActive || !ContentId.IsValid(cardId)) return;
        current.deckCardIds = Append(current.deckCardIds, cardId);
        Persist();
    }

    public static void GrantKey(string keyId)
    {
        if (!HasActive || string.IsNullOrWhiteSpace(keyId) || HasKey(keyId)) return;
        current.keys = Append(current.keys, keyId);
        Persist();
    }

    public static bool TryUnlockWithKey(StageDefinition stage)
    {
        if (!HasActive || stage == null || string.IsNullOrWhiteSpace(stage.RequiredKeyId)) return true;
        if (!HasKey(stage.RequiredKeyId)) return false;
        if (!Contains(current.unlockedStageIds, stage.StageId))
            current.unlockedStageIds = Append(current.unlockedStageIds, stage.StageId);
        Persist();
        return true;
    }

    public static bool IsLocked(StageDefinition stage)
    {
        if (stage == null || string.IsNullOrWhiteSpace(stage.RequiredKeyId)) return false;
        return !Contains(current?.unlockedStageIds, stage.StageId);
    }

    /// <summary>测试辅助：揭示并解锁世界中的全部关卡，不标记为已完成。</summary>
    public static void DebugUnlockAllStages(WorldDefinition world)
    {
        if (!HasActive || world == null || world.Stages == null) return;
        List<string> explored = new List<string>(current.exploredStageIds ?? Array.Empty<string>());
        List<string> unlocked = new List<string>(current.unlockedStageIds ?? Array.Empty<string>());
        List<string> keys = new List<string>(current.keys ?? Array.Empty<string>());
        for (int i = 0; i < world.Stages.Count; i++)
        {
            StageDefinition stage = world.Stages[i];
            if (stage == null || string.IsNullOrWhiteSpace(stage.StageId)) continue;
            if (!explored.Contains(stage.StageId)) explored.Add(stage.StageId);
            if (!unlocked.Contains(stage.StageId)) unlocked.Add(stage.StageId);
            if (!string.IsNullOrWhiteSpace(stage.RequiredKeyId) && !keys.Contains(stage.RequiredKeyId))
                keys.Add(stage.RequiredKeyId);
        }
        current.exploredStageIds = explored.ToArray();
        current.unlockedStageIds = unlocked.ToArray();
        current.keys = keys.ToArray();
        Persist();
    }

    /// <summary>战斗或事件完成后写入血量、牌库，并处理钥匙掉落。</summary>
    public static void CompleteCurrentStage(int remainingHealth, int maxHealth, IReadOnlyList<string> deckCardIds,
        StageDefinition stage)
    {
        if (!HasActive) return;
        SetHealth(remainingHealth, maxHealth);
        SetDeck(deckCardIds);
        if (stage != null)
        {
            if (!IsCompleted(stage.StageId))
                current.completedStageIds = Append(current.completedStageIds, stage.StageId);
            if (!string.IsNullOrWhiteSpace(stage.DropKeyId)) GrantKey(stage.DropKeyId);
        }
        Persist();
    }

    private static bool Contains(string[] items, string value)
    {
        if (items == null || string.IsNullOrEmpty(value)) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == value) return true;
        return false;
    }

    private static string[] Append(string[] items, string value)
    {
        List<string> list = new List<string>(items ?? Array.Empty<string>());
        if (!list.Contains(value)) list.Add(value);
        return list.ToArray();
    }
}

/// <summary>写入 persistentDataPath 的一局存档。</summary>
[Serializable]
public sealed class RunSaveData
{
    public int version = 1;
    public bool runActive;
    public string classId;
    public string worldId;
    public string currentStageId;
    public string[] exploredStageIds;
    public string[] completedStageIds;
    public string[] unlockedStageIds;
    public string[] keys;
    public string[] deckCardIds;
    public int health = 10;
    public int maxHealth = 10;
    public int gold;
}
