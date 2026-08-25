using System;
using LegendsOfFurry.Content.Contracts;

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>
/// 提供不依赖场景对象的卡牌费用和主目标规则计算，供 Unity 输入层与自动测试共同使用。
/// </summary>
public static class ContentCardPlayRules
{
    /// <summary>
    /// 根据结构化费用和当前资源计算本次实际消耗，并同时判断玩家是否能够支付。
    /// </summary>
    /// <param name="card">数据库发布的权威卡牌定义。</param>
    /// <param name="currentActionPoints">玩家当前行动点。</param>
    /// <param name="currentMana">玩家当前法力。</param>
    /// <param name="freePlay">本次卡牌实例是否免除全部费用。</param>
    /// <param name="actionCost">计算出的实际行动点消耗。</param>
    /// <param name="manaCost">计算出的实际法力消耗。</param>
    /// <returns>费用定义有效且当前资源足够时返回 true。</returns>
    public static bool TryCalculateCost(
        CardDefinition card,
        int currentActionPoints,
        int currentMana,
        bool freePlay,
        out int actionCost,
        out int manaCost)
    {
        actionCost = 0;
        manaCost = 0;
        if (card == null || card.Cost == null || currentActionPoints < 0 || currentMana < 0 || card.Unplayable)
        {
            return false;
        }
        if (freePlay)
        {
            return true;
        }
        if (card.Cost.ActionCost < 0 || card.Cost.ManaCost < 0)
        {
            return false;
        }

        actionCost = card.Cost.SpendAllAction ? currentActionPoints : card.Cost.ActionCost;
        manaCost = card.Cost.SpendAllMana ? currentMana : card.Cost.ManaCost;
        if (card.Cost.SpendAllAction && actionCost == 0)
        {
            return false;
        }
        return actionCost <= currentActionPoints && manaCost <= currentMana;
    }

    /// <summary>
    /// 使用场景层收集的客观信息验证结构化目标规则，不直接查询棋盘或 Unity 对象。
    /// </summary>
    /// <param name="card">数据库发布的权威卡牌定义。</param>
    /// <param name="selection">场景层对本次选择生成的规则输入。</param>
    /// <returns>目标选择满足模式、距离、阵营、存活、自身和视线约束时返回 true。</returns>
    public static bool ValidateTarget(CardDefinition card, ContentCardTargetSelection selection)
    {
        if (card == null || card.Target == null || selection == null)
        {
            return false;
        }

        CardTargetRule rule = card.Target;
        if (rule.SelectionMode == "none" || rule.SelectionMode == "self")
        {
            return true;
        }
        if (rule.SelectionMode == "direction")
        {
            return selection.HasDirection && Math.Abs(selection.DirectionX) + Math.Abs(selection.DirectionY) == 1;
        }
        if (selection.Distance < 0 || selection.Distance > Math.Max(0, rule.Range))
        {
            return false;
        }
        if (rule.SelectionMode == "cell")
        {
            return selection.HasCell && (!rule.RequiresLineOfSight || selection.HasLineOfSight);
        }
        if (rule.SelectionMode != "unit" || !selection.HasUnit)
        {
            return false;
        }
        if (!rule.AllowSelf && selection.TargetIsSelf)
        {
            return false;
        }
        if ((rule.LifeStateFilter == "alive" && !selection.TargetIsAlive) ||
            (rule.LifeStateFilter == "dead" && selection.TargetIsAlive))
        {
            return false;
        }
        if ((rule.TeamFilter == "ally" && !selection.TargetHasSameFaction) ||
            (rule.TeamFilter == "enemy" && selection.TargetHasSameFaction))
        {
            return false;
        }
        return !rule.RequiresLineOfSight || selection.HasLineOfSight;
    }
}

/// <summary>
/// 保存 Unity 场景层已经测得的目标信息，使通用目标规则可以脱离场景独立验证。
/// </summary>
public sealed class ContentCardTargetSelection
{
    public bool HasUnit { get; set; }
    public bool HasCell { get; set; }
    public bool HasDirection { get; set; }
    public int DirectionX { get; set; }
    public int DirectionY { get; set; }
    public int Distance { get; set; } = -1;
    public bool TargetIsSelf { get; set; }
    public bool TargetHasSameFaction { get; set; }
    public bool TargetIsAlive { get; set; }
    public bool HasLineOfSight { get; set; }
}
}
