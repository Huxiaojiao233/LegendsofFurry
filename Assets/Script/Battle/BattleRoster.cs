using System.Collections.Generic;
using UnityEngine;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;

/// <summary>
/// 本场战斗的己方与敌人编制。场景里不放棋子，由该对象在运行时生成。
/// </summary>
[DefaultExecutionOrder(-50)]
public sealed class BattleRoster : MonoBehaviour
{
    public const int MaximumAllies = 3;

    private readonly List<Unit> allies = new List<Unit>();
    private readonly List<Unit> enemies = new List<Unit>();

    public static BattleRoster Instance { get; private set; }
    public IReadOnlyList<Unit> Allies => allies;
    public IReadOnlyList<Unit> Enemies => enemies;
    public Unit PrimaryAlly => allies.Count > 0 ? allies[0] : null;
    public Unit PrimaryEnemy => enemies.Count > 0 ? enemies[0] : null;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>按内容包与可选场景编制生成棋子并放到棋盘上。</summary>
    public void SpawnEncounter()
    {
        if (!ContentRuntime.IsLoaded)
        {
            Debug.LogError($"战斗编制无法读取内容包：{ContentRuntime.LoadError}", this);
            return;
        }

        BoardGenerator board = FindAnyObjectByType<BoardGenerator>();
        if (board == null)
        {
            Debug.LogError("战斗场景缺少棋盘，无法放置棋子。", this);
            return;
        }

        ClearScenePlaceholders();
        BattleEncounterConfig config = FindAnyObjectByType<BattleEncounterConfig>();
        GameSettingsDefinition settings = ContentRuntime.Registry.GameSettings;
        string[] runEnemies = ResolveRunEnemyIds();
        SpawnSide(true, ResolveIds(true, config != null ? config.AllyCharacterIds : null, settings.PlayerCharacterId, MaximumAllies),
            config != null ? config.AllySpawnCells : null, new Vector2Int(3, 2), board);
        SpawnSide(false, ResolveIds(false, runEnemies ?? (config != null ? config.EnemyCharacterIds : null), settings.EnemyCharacterId, 8),
            config != null ? config.EnemySpawnCells : null, new Vector2Int(5, 6), board);
    }

    /// <summary>大地图冒险：己方放在当前关卡中心，仅在需要开战时刷敌人。</summary>
    public void SpawnRunParty(Vector2Int allyCell, bool spawnEnemies, Vector2Int enemyCell)
    {
        if (!ContentRuntime.IsLoaded)
        {
            Debug.LogError($"战斗编制无法读取内容包：{ContentRuntime.LoadError}", this);
            return;
        }

        BoardGenerator board = FindAnyObjectByType<BoardGenerator>();
        if (board == null)
        {
            Debug.LogError("战斗场景缺少棋盘，无法放置棋子。", this);
            return;
        }

        ClearScenePlaceholders();
        GameSettingsDefinition settings = ContentRuntime.Registry.GameSettings;
        SpawnSide(true, ResolveIds(true, null, settings.PlayerCharacterId, MaximumAllies),
            null, allyCell, board);
        if (spawnEnemies)
            SpawnEnemiesAt(enemyCell);
    }

    /// <summary>走进战斗关后在当前 10x10 里生成敌人。</summary>
    public void SpawnEnemiesAt(Vector2Int enemyCell)
    {
        BoardGenerator board = FindAnyObjectByType<BoardGenerator>();
        if (board == null || !ContentRuntime.IsLoaded) return;
        ClearEnemies();
        GameSettingsDefinition settings = ContentRuntime.Registry.GameSettings;
        string[] runEnemies = ResolveRunEnemyIds();
        SpawnSide(false, ResolveIds(false, runEnemies, settings.EnemyCharacterId, 8),
            null, enemyCell, board);
    }

    /// <summary>战斗结束后清掉敌人棋子，玩家留在原格。</summary>
    public void ClearEnemies()
    {
        BoardGenerator board = FindAnyObjectByType<BoardGenerator>();
        for (int i = 0; i < enemies.Count; i++)
        {
            Unit unit = enemies[i];
            if (unit == null) continue;
            board?.RemoveOccupant(unit);
            Destroy(unit.gameObject);
        }

        enemies.Clear();
    }

    /// <summary>是否还有存活的己方单位。</summary>
    public bool HasLivingAlly()
    {
        return HasLiving(allies);
    }

    /// <summary>是否还有存活的敌人。</summary>
    public bool HasLivingEnemy()
    {
        return HasLiving(enemies);
    }

    /// <summary>离指定单位最近的存活敌对单位。</summary>
    public Unit FindNearestLivingOpponent(Unit from)
    {
        if (from == null) return null;
        IReadOnlyList<Unit> opponents = from.IsPlayer ? enemies : allies;
        Unit best = null;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < opponents.Count; i++)
        {
            Unit candidate = opponents[i];
            if (candidate == null || !candidate.IsAlive) continue;
            int distance = Mathf.Abs(candidate.Position.x - from.Position.x) +
                           Mathf.Abs(candidate.Position.y - from.Position.y);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = candidate;
        }

        return best;
    }

    private void SpawnSide(bool ally, List<string> ids, Vector2Int[] cells, Vector2Int fallbackCell, BoardGenerator board)
    {
        UnitFaction faction = ally ? UnitFaction.Player : UnitFaction.Enemy;
        for (int i = 0; i < ids.Count; i++)
        {
            if (!ContentRuntime.Registry.TryGetCharacter(ids[i], out CharacterDefinition definition))
            {
                Debug.LogWarning($"内容包没有角色 {ids[i]}，跳过生成。", this);
                continue;
            }

            string objectName = ally
                ? (i == 0 ? "Player" : $"Ally_{i + 1}")
                : (i == 0 ? "Monster" : $"Enemy_{i + 1}");
            Unit unit = CombatantTokenFactory.Spawn(definition, faction, objectName);
            unit.SetBoard(board);
            Vector2Int preferred = cells != null && i < cells.Length ? cells[i]
                : fallbackCell + new Vector2Int(ally ? -i : i, 0);
            if (!board.TryFindNearestFreeCell(preferred, unit, out Vector2Int cell))
            {
                Debug.LogWarning($"{unit.name} 找不到可放置的格子。", unit);
                Destroy(unit.gameObject);
                continue;
            }

            unit.MoveTo(cell.x, cell.y);
            if (ally) allies.Add(unit);
            else enemies.Add(unit);
        }
    }

    private static List<string> ResolveIds(bool ally, string[] authored, string fallbackId, int cap)
    {
        List<string> ids = new List<string>();
        if (authored != null)
        {
            for (int i = 0; i < authored.Length && ids.Count < cap; i++)
            {
                if (!string.IsNullOrWhiteSpace(authored[i])) ids.Add(authored[i].Trim());
            }
        }

        if (ids.Count == 0 && !string.IsNullOrWhiteSpace(fallbackId)) ids.Add(fallbackId.Trim());
        if (ids.Count == 0)
        {
            List<CharacterDefinition> ordered = new List<CharacterDefinition>(ContentRuntime.Registry.Characters);
            ordered.Sort((left, right) =>
            {
                int byOrder = left.SortOrder.CompareTo(right.SortOrder);
                return byOrder != 0 ? byOrder : string.CompareOrdinal(left.CharacterId, right.CharacterId);
            });
            if (ally && ordered.Count > 0) ids.Add(ordered[0].CharacterId);
            else if (!ally && ordered.Count > 1) ids.Add(ordered[1].CharacterId);
            else if (!ally && ordered.Count == 1) ids.Add(ordered[0].CharacterId);
        }

        return ids;
    }

    private static string[] ResolveRunEnemyIds()
    {
        if (!RunSession.HasActive) return null;
        if (!WorldCatalog.TryGet(RunSession.Current.worldId, out WorldDefinition world) &&
            (world = WorldCatalog.Default) == null) return null;
        if (!WorldCatalog.TryGetStage(world, RunSession.Current.currentStageId, out StageDefinition stage))
            return null;
        if (stage.EnemyCharacterIds == null || stage.EnemyCharacterIds.Count == 0) return null;
        return stage.EnemyCharacterIds.ToArray();
    }

    private static bool HasLiving(List<Unit> units)
    {
        for (int i = 0; i < units.Count; i++)
        {
            if (units[i] != null && units[i].IsAlive) return true;
        }

        return false;
    }

    private static void ClearScenePlaceholders()
    {
        Unit[] existing = FindObjectsByType<Unit>(FindObjectsInactive.Include);
        for (int i = 0; i < existing.Length; i++)
        {
            Unit unit = existing[i];
            if (unit == null) continue;
            Object.DestroyImmediate(unit.gameObject);
        }
    }
}
