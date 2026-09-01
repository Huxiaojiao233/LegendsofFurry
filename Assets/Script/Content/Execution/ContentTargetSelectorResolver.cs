using System;
using System.Collections.Generic;
using System.Linq;
using LegendsOfFurry.Content.Contracts;
using UnityEngine;

#pragma warning disable 0649 // Unity JsonUtility 会通过反射填充目标选择参数 DTO 字段。

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>
/// 向目标选择器提供场景单位和牌区快照，避免注册表直接搜索全局对象或读取手牌私有字段。
/// </summary>
public interface IContentTargetQueryService
{
    /// <summary>
    /// 返回本次选择允许查询的全部战斗单位快照。
    /// </summary>
    /// <returns>单位集合；选择器会再次稳定排序。</returns>
    IReadOnlyList<Unit> GetUnits();

    /// <summary>
    /// 返回指定牌区的卡牌实例快照。
    /// </summary>
    /// <param name="zoneKey">hand、draw、discard 或 exhaust。</param>
    /// <returns>牌区卡牌集合；未知牌区返回空集合。</returns>
    IReadOnlyList<CardInstance> GetCards(string zoneKey);
}

/// <summary>
/// 保存范围目标选择器的结构化参数。
/// </summary>
[Serializable]
public sealed class ContentTargetSelectorParameters
{
    public int range = 1;
    public int maximum;
    public bool includeCenter;
}

/// <summary>
/// 解析一个目标选择器 key，并显式区分空结果与解析失败。
/// </summary>
public interface IContentTargetSelectorHandler
{
    /// <summary>
    /// 解析当前目标选择器。
    /// </summary>
    /// <param name="parameters">结构化选择参数。</param>
    /// <param name="context">本次卡牌执行上下文。</param>
    /// <param name="targets">按稳定顺序返回的对象集合。</param>
    /// <returns>选择器可用且参数有效时返回 true；合法空集合仍返回 true。</returns>
    bool TryResolve(
        ContentTargetSelectorParameters parameters,
        ContentCardExecutionContext context,
        out IReadOnlyList<object> targets);
}

/// <summary>
/// 通过白名单注册表解析单位、地块、卡牌实例和牌区集合，并对集合实施确定性排序。
/// </summary>
public sealed class ContentTargetSelectorResolver
{
    private readonly ContentOperationRegistry<IContentTargetSelectorHandler> registry =
        new ContentOperationRegistry<IContentTargetSelectorHandler>();

    /// <summary>
    /// 创建并注册首期全部目标选择器。
    /// </summary>
    public ContentTargetSelectorResolver()
    {
        Register("self", ResolveSelf);
        Register("selected_unit", ResolveSelectedUnit);
        Register("selected_cell", ResolveSelectedCell);
        Register("units_around_selected", ResolveUnitsAroundSelected);
        Register("units_in_manhattan_range", ResolveUnitsInManhattanRange);
        Register("units_in_direction", ResolveUnitsInDirection);
        Register("units_in_front_area", ResolveUnitsInFrontArea);
        Register("all_allies", ResolveAllAllies);
        Register("all_enemies", ResolveAllEnemies);
        Register("all_units", ResolveAllUnits);
        Register("current_card", ResolveCurrentCard);
        Register("cards_in_hand", (ContentTargetSelectorParameters parameters, ContentCardExecutionContext context, out IReadOnlyList<object> targets) => ResolveCardsInZone(ContentCardZoneKeys.Hand, context, out targets));
        Register("cards_in_draw_pile", (ContentTargetSelectorParameters parameters, ContentCardExecutionContext context, out IReadOnlyList<object> targets) => ResolveCardsInZone(ContentCardZoneKeys.Draw, context, out targets));
        Register("cards_in_discard_pile", (ContentTargetSelectorParameters parameters, ContentCardExecutionContext context, out IReadOnlyList<object> targets) => ResolveCardsInZone(ContentCardZoneKeys.Discard, context, out targets));
        Register("cards_in_exhaust_pile", (ContentTargetSelectorParameters parameters, ContentCardExecutionContext context, out IReadOnlyList<object> targets) => ResolveCardsInZone(ContentCardZoneKeys.Exhaust, context, out targets));
    }

    /// <summary>
    /// 解析 foreach 行为节点，并把参数 JSON 映射为受控范围参数。
    /// </summary>
    /// <param name="node">NodeKind=foreach 的行为节点。</param>
    /// <param name="context">本次卡牌执行上下文。</param>
    /// <param name="targets">稳定排序后的目标对象。</param>
    /// <returns>节点类型、key、JSON 和运行时查询服务均有效时返回 true。</returns>
    public bool TryResolve(
        BehaviorNodeDefinition node,
        ContentCardExecutionContext context,
        out IReadOnlyList<object> targets)
    {
        targets = Array.Empty<object>();
        if (node == null || node.NodeKind != "foreach" || context == null ||
            !registry.TryGet(node.OperationKey, out IContentTargetSelectorHandler handler))
        {
            return false;
        }
        try
        {
            ContentTargetSelectorParameters parameters =
                JsonUtility.FromJson<ContentTargetSelectorParameters>(node.ParametersJson) ??
                new ContentTargetSelectorParameters();
            return handler.TryResolve(parameters, context, out targets);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// 判断指定目标选择器 key 是否已经注册。
    /// </summary>
    /// <param name="key">目标选择器 key。</param>
    /// <returns>存在处理器时返回 true。</returns>
    public bool Contains(string key)
    {
        return registry.Contains(key);
    }

    /// <summary>
    /// 在扣费前验证 foreach 选择器 key、JSON 和范围参数，不访问场景对象。
    /// </summary>
    /// <param name="node">需要预检的 foreach 节点。</param>
    /// <returns>选择器已注册且参数非负时返回 true。</returns>
    public bool CanResolveNode(BehaviorNodeDefinition node)
    {
        if (node == null || node.NodeKind != "foreach" || !registry.Contains(node.OperationKey))
        {
            return false;
        }
        try
        {
            ContentTargetSelectorParameters parameters =
                JsonUtility.FromJson<ContentTargetSelectorParameters>(node.ParametersJson) ??
                new ContentTargetSelectorParameters();
            return parameters.range >= 0 && parameters.maximum >= 0;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// 返回全部已实现目标选择器 key 的只读有序快照。
    /// </summary>
    /// <returns>注册 key 列表。</returns>
    public IReadOnlyList<string> GetRegisteredKeys()
    {
        return registry.GetRegisteredKeys();
    }

    /// <summary>注册一个强类型目标选择委托。</summary>
    private void Register(string key, TargetSelectorDelegate handler)
    {
        registry.Register(key, new DelegateTargetSelectorHandler(handler));
    }

    /// <summary>返回施放者；缺失施放者属于运行时失败而非合法空集合。</summary>
    private static bool ResolveSelf(ContentTargetSelectorParameters parameters, ContentCardExecutionContext context, out IReadOnlyList<object> targets)
    {
        targets = context.Source == null ? Array.Empty<object>() : new object[] { context.Source };
        return context.Source != null;
    }

    /// <summary>返回玩家已选单位；未选择单位时报告失败。</summary>
    private static bool ResolveSelectedUnit(ContentTargetSelectorParameters parameters, ContentCardExecutionContext context, out IReadOnlyList<object> targets)
    {
        targets = context.SelectedUnit == null ? Array.Empty<object>() : new object[] { context.SelectedUnit };
        return context.SelectedUnit != null;
    }

    /// <summary>返回玩家已选地块；未选择地块时报告失败。</summary>
    private static bool ResolveSelectedCell(ContentTargetSelectorParameters parameters, ContentCardExecutionContext context, out IReadOnlyList<object> targets)
    {
        targets = context.SelectedCell == null ? Array.Empty<object>() : new object[] { context.SelectedCell };
        return context.SelectedCell != null;
    }

    /// <summary>返回已选单位周围切比雪夫距离内的单位。</summary>
    private static bool ResolveUnitsAroundSelected(ContentTargetSelectorParameters parameters, ContentCardExecutionContext context, out IReadOnlyList<object> targets)
    {
        targets = Array.Empty<object>();
        if (context.SelectedUnit == null || !TryGetUnits(context, out IReadOnlyList<Unit> units) || parameters.range < 0)
        {
            return false;
        }
        Vector2Int center = context.SelectedUnit.Position;
        targets = LimitAndBox(units.Where(unit =>
            (parameters.includeCenter || unit != context.SelectedUnit) &&
            Mathf.Max(Mathf.Abs(unit.Position.x - center.x), Mathf.Abs(unit.Position.y - center.y)) <= parameters.range),
            parameters.maximum);
        return true;
    }

    /// <summary>返回以当前图目标、已选单位或施放者为中心的曼哈顿范围单位。</summary>
    private static bool ResolveUnitsInManhattanRange(ContentTargetSelectorParameters parameters, ContentCardExecutionContext context, out IReadOnlyList<object> targets)
    {
        targets = Array.Empty<object>();
        Unit centerUnit = context.CurrentGraphTarget as Unit ?? context.SelectedUnit ?? context.Source;
        if (centerUnit == null || !TryGetUnits(context, out IReadOnlyList<Unit> units) || parameters.range < 0)
        {
            return false;
        }
        Vector2Int center = centerUnit.Position;
        targets = LimitAndBox(units.Where(unit =>
            (parameters.includeCenter || unit != centerUnit) &&
            Mathf.Abs(unit.Position.x - center.x) + Mathf.Abs(unit.Position.y - center.y) <= parameters.range),
            parameters.maximum);
        return true;
    }

    /// <summary>返回施放者指定正交方向直线上的单位，并按距离和坐标稳定排序。</summary>
    private static bool ResolveUnitsInDirection(ContentTargetSelectorParameters parameters, ContentCardExecutionContext context, out IReadOnlyList<object> targets)
    {
        targets = Array.Empty<object>();
        if (context.Source == null || !context.Direction.HasValue ||
            Mathf.Abs(context.Direction.Value.x) + Mathf.Abs(context.Direction.Value.y) != 1 ||
            !TryGetUnits(context, out IReadOnlyList<Unit> units))
        {
            return false;
        }
        Vector2Int origin = context.Source.Position;
        Vector2Int direction = context.Direction.Value;
        IEnumerable<Unit> selected = units.Where(unit =>
        {
            Vector2Int delta = unit.Position - origin;
            int forward = delta.x * direction.x + delta.y * direction.y;
            int cross = delta.x * direction.y - delta.y * direction.x;
            return forward > 0 && cross == 0 && (parameters.range <= 0 || forward <= parameters.range);
        });
        targets = LimitAndBox(selected.OrderBy(unit => Mathf.Abs(unit.Position.x - origin.x) + Mathf.Abs(unit.Position.y - origin.y)), parameters.maximum);
        return true;
    }

    /// <summary>返回施放者面前一行三格中的单位。</summary>
    private static bool ResolveUnitsInFrontArea(ContentTargetSelectorParameters parameters, ContentCardExecutionContext context, out IReadOnlyList<object> targets)
    {
        targets = Array.Empty<object>();
        if (context.Source == null || !context.Direction.HasValue ||
            Mathf.Abs(context.Direction.Value.x) + Mathf.Abs(context.Direction.Value.y) != 1 ||
            !TryGetUnits(context, out IReadOnlyList<Unit> units))
        {
            return false;
        }
        Vector2Int direction = context.Direction.Value;
        Vector2Int perpendicular = new Vector2Int(-direction.y, direction.x);
        Vector2Int front = context.Source.Position + direction;
        targets = LimitAndBox(units.Where(unit =>
            unit.Position == front || unit.Position == front + perpendicular || unit.Position == front - perpendicular),
            parameters.maximum);
        return true;
    }

    /// <summary>返回所有与施放者同阵营的单位。</summary>
    private static bool ResolveAllAllies(ContentTargetSelectorParameters parameters, ContentCardExecutionContext context, out IReadOnlyList<object> targets)
    {
        targets = Array.Empty<object>();
        if (context.Source == null || !TryGetUnits(context, out IReadOnlyList<Unit> units)) return false;
        targets = LimitAndBox(units.Where(unit => unit.Faction == context.Source.Faction), parameters.maximum);
        return true;
    }

    /// <summary>返回所有与施放者不同阵营的单位。</summary>
    private static bool ResolveAllEnemies(ContentTargetSelectorParameters parameters, ContentCardExecutionContext context, out IReadOnlyList<object> targets)
    {
        targets = Array.Empty<object>();
        if (context.Source == null || !TryGetUnits(context, out IReadOnlyList<Unit> units)) return false;
        targets = LimitAndBox(units.Where(unit => unit.Faction != context.Source.Faction), parameters.maximum);
        return true;
    }

    /// <summary>返回查询服务中的全部单位。</summary>
    private static bool ResolveAllUnits(ContentTargetSelectorParameters parameters, ContentCardExecutionContext context, out IReadOnlyList<object> targets)
    {
        targets = Array.Empty<object>();
        if (!TryGetUnits(context, out IReadOnlyList<Unit> units)) return false;
        targets = LimitAndBox(units, parameters.maximum);
        return true;
    }

    /// <summary>返回当前卡牌实例。</summary>
    private static bool ResolveCurrentCard(ContentTargetSelectorParameters parameters, ContentCardExecutionContext context, out IReadOnlyList<object> targets)
    {
        targets = context.Card == null ? Array.Empty<object>() : new object[] { context.Card };
        return context.Card != null;
    }

    /// <summary>从注入牌区服务读取并按卡牌 ID 稳定排序指定牌区。</summary>
    private static bool ResolveCardsInZone(string zoneKey, ContentCardExecutionContext context, out IReadOnlyList<object> targets)
    {
        targets = Array.Empty<object>();
        if (context.TargetQueryService == null) return false;
        IReadOnlyList<CardInstance> cards = context.TargetQueryService.GetCards(zoneKey);
        if (cards == null) return false;
        targets = cards.Where(card => card != null)
            .OrderBy(card => card.Definition?.CardId ?? card.Data?.cardId, StringComparer.Ordinal)
            .Cast<object>()
            .ToArray();
        return true;
    }

    /// <summary>从注入查询服务读取非空单位快照。</summary>
    private static bool TryGetUnits(ContentCardExecutionContext context, out IReadOnlyList<Unit> units)
    {
        units = context.TargetQueryService?.GetUnits();
        return units != null;
    }

    /// <summary>按坐标和显示名稳定排序单位、实施可选数量上限并装箱。</summary>
    private static IReadOnlyList<object> LimitAndBox(IEnumerable<Unit> units, int maximum)
    {
        IEnumerable<Unit> ordered = units.Where(unit => unit != null)
            .OrderBy(unit => unit.Position.x)
            .ThenBy(unit => unit.Position.y)
            .ThenBy(unit => unit.DisplayName, StringComparer.Ordinal);
        if (maximum > 0) ordered = ordered.Take(maximum);
        return ordered.Cast<object>().ToArray();
    }

    private delegate bool TargetSelectorDelegate(
        ContentTargetSelectorParameters parameters,
        ContentCardExecutionContext context,
        out IReadOnlyList<object> targets);

    /// <summary>把内部目标选择委托适配为公开处理器接口。</summary>
    private sealed class DelegateTargetSelectorHandler : IContentTargetSelectorHandler
    {
        private readonly TargetSelectorDelegate handler;

        /// <summary>保存需要调用的目标选择委托。</summary>
        public DelegateTargetSelectorHandler(TargetSelectorDelegate value)
        {
            handler = value;
        }

        /// <summary>转发目标解析并保留合法空集合与失败的区别。</summary>
        public bool TryResolve(ContentTargetSelectorParameters parameters, ContentCardExecutionContext context, out IReadOnlyList<object> targets)
        {
            return handler(parameters, context, out targets);
        }
    }
}
}

#pragma warning restore 0649
