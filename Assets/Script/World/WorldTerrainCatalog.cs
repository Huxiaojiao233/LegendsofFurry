using System.Linq;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;
using UnityEngine;

/// <summary>手写地图用地形与少量装饰的稳定 ID、图例字母。</summary>
public static class WorldTerrainCatalog
{
    public const string Grass = "base.grass";
    public const string Stone = "base.stone";
    public const string Water = "base.water";
    public const string Dirt = "base.dirt";
    public const string Sand = "base.sand";
    public const string Road = "base.road";
    public const string Forest = "base.forest";
    public const string Void = "base.void";

    public const string Tree = "base.tree";
    public const string Rock = "base.rock";
    public const string Camp = "base.camp";

    public static readonly TerrainBrush[] Terrains =
    {
        new TerrainBrush("G", Grass, "草"),
        new TerrainBrush("S", Stone, "石"),
        new TerrainBrush("W", Water, "水"),
        new TerrainBrush("D", Dirt, "土"),
        new TerrainBrush("A", Sand, "沙"),
        new TerrainBrush("R", Road, "路"),
        new TerrainBrush("F", Forest, "林"),
        new TerrainBrush("V", Void, "空")
    };

    public static readonly DecorationBrush[] Decorations =
    {
        new DecorationBrush(Tree, "树"),
        new DecorationBrush(Rock, "石"),
        new DecorationBrush(Camp, "营")
    };

    public static string GlyphOf(string terrainId)
    {
        for (int i = 0; i < Terrains.Length; i++)
            if (Terrains[i].Id == terrainId) return Terrains[i].Glyph;
        return "G";
    }

    public static string IdOfGlyph(char glyph)
    {
        for (int i = 0; i < Terrains.Length; i++)
            if (Terrains[i].Glyph[0] == glyph) return Terrains[i].Id;
        return Grass;
    }

    public static string FromHeight(int height)
    {
        if (height < 0) return Water;
        if (height <= 2) return Grass;
        if (height <= 5) return Dirt;
        return Stone;
    }

    public static bool IsWalkable(string terrainId) => terrainId != Void;

    /// <summary>地形进入能力检查集中在这里，新增飞行、熔岩等能力时不改寻路器。</summary>
    public static bool CanEnter(string terrainId, Unit unit)
    {
        if (!IsWalkable(terrainId)) return false;
        if (terrainId != Water) return true;
        return unit?.Definition?.Capabilities?.Contains("wading") == true;
    }

    public static bool EndsActionOnEnter(string terrainId) => terrainId == Water;

    public static bool TryGetDecoration(string definition, out DecorationBrush brush)
    {
        for (int i = 0; i < Decorations.Length; i++)
        {
            if (Decorations[i].Id == definition)
            {
                brush = Decorations[i];
                return true;
            }
        }

        brush = default;
        return false;
    }

    public readonly struct TerrainBrush
    {
        public readonly string Glyph;
        public readonly string Id;
        public readonly string Label;

        public TerrainBrush(string glyph, string id, string label)
        {
            Glyph = glyph;
            Id = id;
            Label = label;
        }
    }

    public readonly struct DecorationBrush
    {
        public readonly string Id;
        public readonly string Label;

        public DecorationBrush(string id, string label)
        {
            Id = id;
            Label = label;
        }
    }
}

/// <summary>把“完成移动/进入地形/获得状态”串成通用触发流程。</summary>
public static class TerrainMovementRuntime
{
    public static void CompleteMove(Unit unit, Vector2Int from, Vector2Int to, IActionPointPool actionPoints)
    {
        if (unit?.Board == null) return;
        string terrainId = unit.Board.GetTerrain(to.x, to.y);
        CombatEventBus.Shared.Publish(new UnitMoveCompletedEvent(unit, from, to));
        CombatEventBus.Shared.Publish(new TerrainEnteredEvent(unit, terrainId, from, to));
        ContentActorBehaviorRuntime.Execute(unit, ContentTriggerKeys.OnMoveCompleted, actionPoints, terrainId);
        ContentActorBehaviorRuntime.Execute(unit, ContentTriggerKeys.OnEnteredTerrain, actionPoints, terrainId);
        if (WorldTerrainCatalog.EndsActionOnEnter(terrainId))
            actionPoints?.TrySpendActionPoints(actionPoints.CurrentActionPoints);
        ApplyStatusesForTerrain(unit, terrainId, ContentRuntime.IsLoaded
            ? ContentRuntime.Registry.Statuses : System.Array.Empty<StatusDefinition>());
    }

    /// <summary>按状态定义上的地形列表施加状态，扩展包可给熔岩、毒沼等复用同一入口。</summary>
    public static void ApplyStatusesForTerrain(Unit unit, string terrainId,
        System.Collections.Generic.IEnumerable<StatusDefinition> statuses)
    {
        if (unit == null || string.IsNullOrWhiteSpace(terrainId) || statuses == null) return;
        foreach (StatusDefinition status in statuses)
        {
            if (status == null || !status.Enabled || string.IsNullOrWhiteSpace(status.StatusId)) continue;
            if (status.ApplyOnTerrainIds == null ||
                !status.ApplyOnTerrainIds.Contains(terrainId, System.StringComparer.Ordinal)) continue;
            unit.State.Add(status.StatusId, 1, 0, unit.InstanceId);
        }
    }
}
