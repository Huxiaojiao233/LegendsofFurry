using UnityEngine;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;

/// <summary>手写地图用地形与少量装饰的稳定 ID、图例字母和编辑/运行时颜色。</summary>
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
        new TerrainBrush("G", Grass, "草", new Color(0.42f, 0.72f, 0.36f)),
        new TerrainBrush("S", Stone, "石", new Color(0.55f, 0.56f, 0.58f)),
        new TerrainBrush("W", Water, "水", new Color(0.28f, 0.52f, 0.78f)),
        new TerrainBrush("D", Dirt, "土", new Color(0.62f, 0.48f, 0.28f)),
        new TerrainBrush("A", Sand, "沙", new Color(0.84f, 0.76f, 0.48f)),
        new TerrainBrush("R", Road, "路", new Color(0.7f, 0.62f, 0.42f)),
        new TerrainBrush("F", Forest, "林", new Color(0.22f, 0.48f, 0.24f)),
        new TerrainBrush("V", Void, "空", new Color(0.12f, 0.13f, 0.15f))
    };

    public static readonly DecorationBrush[] Decorations =
    {
        new DecorationBrush(Tree, "树", new Color(0.2f, 0.45f, 0.18f)),
        new DecorationBrush(Rock, "石", new Color(0.45f, 0.46f, 0.48f)),
        new DecorationBrush(Camp, "营", new Color(0.72f, 0.5f, 0.28f))
    };

    public static Color ColorOf(string terrainId)
    {
        for (int i = 0; i < Terrains.Length; i++)
            if (Terrains[i].Id == terrainId) return Terrains[i].Color;
        return Terrains[0].Color;
    }

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
        if (height == 0) return Grass;
        if (height == 1) return Dirt;
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
        public readonly Color Color;

        public TerrainBrush(string glyph, string id, string label, Color color)
        {
            Glyph = glyph;
            Id = id;
            Label = label;
            Color = color;
        }
    }

    public readonly struct DecorationBrush
    {
        public readonly string Id;
        public readonly string Label;
        public readonly Color Color;

        public DecorationBrush(string id, string label, Color color)
        {
            Id = id;
            Label = label;
            Color = color;
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
        ContentActorBehaviorRuntime.Execute(unit, ContentTriggerKeys.OnMoveCompleted, actionPoints);
        ContentActorBehaviorRuntime.Execute(unit, ContentTriggerKeys.OnEnteredTerrain, actionPoints);
        if (WorldTerrainCatalog.EndsActionOnEnter(terrainId))
        {
            actionPoints?.TrySpendActionPoints(actionPoints.CurrentActionPoints);
            unit.State.Add("wet", 1, 0, unit.InstanceId);
        }
    }
}
