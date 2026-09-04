using System.Collections.Generic;
using UnityEngine;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;

/// <summary>
/// 本场战斗的己方与敌人编制。敌人只从关卡的显式单位部署记录加载。
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

    /// <summary>无大地图的调试场景只生成己方；敌对单位必须由世界关卡部署。</summary>
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
        GameSettingsDefinition settings = ContentRuntime.Registry.GameSettings;
        BattleEncounterConfig config = FindAnyObjectByType<BattleEncounterConfig>();
        SpawnSide(true, ResolveIds(config != null ? config.AllyUnitIds : null, settings.PlayerUnitId, MaximumAllies),
            config != null ? config.AllySpawnCells : null, new Vector2Int(3, 2), board);
    }

    /// <summary>大地图冒险：己方放在当前关卡中心，敌人按当前关卡部署坐标加载。</summary>
    public void SpawnRunParty(Vector2Int allyCell, bool spawnEnemies)
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
        SpawnSide(true, ResolveIds(null, settings.PlayerUnitId, MaximumAllies),
            null, allyCell, board);
        if (spawnEnemies)
            SpawnDeployedEnemies(ResolveCurrentStage());
    }

    /// <summary>按世界编辑器保存的实例坐标加载当前关卡全部敌对单位。</summary>
    public void SpawnDeployedEnemies(StageDefinition stage)
    {
        BoardGenerator board = FindAnyObjectByType<BoardGenerator>();
        if (board == null || !ContentRuntime.IsLoaded || stage == null) return;
        ClearEnemies();
        WorldDefinition world = ResolveCurrentWorld();
        if (world == null) return;
        foreach (WorldUnitPlacementDefinition placement in stage.UnitPlacements)
        {
            if (placement == null || !placement.Enabled) continue;
            SpawnPlacement(placement, stage, world, board);
        }
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
            if (!ContentRuntime.Registry.TryGetUnit(ids[i], out UnitDefinition definition))
            {
                Debug.LogWarning($"内容包没有单位 {ids[i]}，跳过加载。", this);
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

    private void SpawnPlacement(WorldUnitPlacementDefinition placement, StageDefinition stage,
        WorldDefinition world, BoardGenerator board)
    {
        if (!ContentRuntime.Registry.TryGetUnit(placement.UnitId, out UnitDefinition definition))
        {
            Debug.LogWarning($"关卡 {stage.StageId} 部署了不存在的单位 {placement.UnitId}。", this);
            return;
        }
        string factionKey = string.IsNullOrWhiteSpace(placement.FactionOverride)
            ? definition.DefaultFaction : placement.FactionOverride;
        if (factionKey != "enemy") return;
        Unit unit = CombatantTokenFactory.Spawn(definition, UnitFaction.Enemy,
            string.IsNullOrWhiteSpace(placement.InstanceId) ? definition.UnitId : placement.InstanceId);
        unit.SetBoard(board);
        Vector2Int preferred = WorldLayout.ToBoard(stage, placement.LocalX, placement.LocalY, world);
        if (!board.TryFindNearestFreeCell(preferred, unit, out Vector2Int cell))
        {
            Debug.LogWarning($"部署实例 {placement.InstanceId} 找不到可放置格子。", unit);
            Destroy(unit.gameObject);
            return;
        }
        unit.MoveTo(cell.x, cell.y);
        enemies.Add(unit);
        if ((string.IsNullOrWhiteSpace(placement.ControllerOverride) ? definition.Controller : placement.ControllerOverride) == "ai")
            (unit.GetComponent<UtilityAiController>() ?? unit.gameObject.AddComponent<UtilityAiController>())
                .Configure(string.IsNullOrWhiteSpace(placement.DeckIdOverride) ? definition.DeckId : placement.DeckIdOverride);
    }

    private static List<string> ResolveIds(string[] authored, string fallbackId, int cap)
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
            List<UnitDefinition> ordered = new List<UnitDefinition>(ContentRuntime.Registry.Units);
            ordered.RemoveAll(item => !item.CanJoinParty && item.DefaultFaction != "player");
            ordered.Sort((left, right) =>
            {
                int byOrder = left.SortOrder.CompareTo(right.SortOrder);
                return byOrder != 0 ? byOrder : string.CompareOrdinal(left.UnitId, right.UnitId);
            });
            if (ordered.Count > 0) ids.Add(ordered[0].UnitId);
        }

        return ids;
    }

    private static WorldDefinition ResolveCurrentWorld()
    {
        if (!RunSession.HasActive) return null;
        if (!WorldCatalog.TryGet(RunSession.Current.worldId, out WorldDefinition world) &&
            (world = WorldCatalog.Default) == null) return null;
        return world;
    }

    private static StageDefinition ResolveCurrentStage()
    {
        WorldDefinition world = ResolveCurrentWorld();
        if (world == null || !WorldCatalog.TryGetStage(world, RunSession.Current.currentStageId, out StageDefinition stage))
            return null;
        return stage;
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
