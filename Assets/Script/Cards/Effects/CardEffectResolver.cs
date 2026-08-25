using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;
using UnityEngine;

public sealed class CardPlayResult
{
    public bool Success;
    public bool EndTurn;
    public int FreeMoveSteps;
    public ContentCardExecutionContext ExecutionContext { get; internal set; }
}

public class CardEffectResolver : MonoBehaviour
{
    [SerializeField] private BoardClickController actionPointController;
    [SerializeField] private HandCardSystem handCardSystem;
    [SerializeField] private Unit player;
    [SerializeField] private Unit enemy;

    /// <summary>
    /// 绑定战斗场景中的行动点控制器和双方单位，并补齐可选手牌系统引用。
    /// </summary>
    /// <param name="actionPoints">行动点控制器。</param>
    /// <param name="playerUnit">出牌方单位。</param>
    /// <param name="enemyUnit">当前敌方单位。</param>
    public void Bind(BoardClickController actionPoints, Unit playerUnit, Unit enemyUnit)
    {
        actionPointController = actionPoints;
        player = playerUnit;
        enemy = enemyUnit;
        handCardSystem ??= FindAnyObjectByType<HandCardSystem>();
    }

    /// <summary>
    /// 在不改变资源的前提下检查卡牌禁用、攻击限制以及固定或 X 费用是否可支付。
    /// </summary>
    /// <param name="instance">需要预检的卡牌实例。</param>
    /// <returns>当前状态允许支付并打出时返回 true。</returns>
    public bool CanAfford(CardInstance instance)
    {
        ResolveReferences();
        if (instance?.Definition == null || instance.Data == null || actionPointController == null || player == null) return false;
        CardData card = instance.Data;
        if (card.unplayable) return false;
        if (card.isAttack && player.State.Has(CombatStatus.CannotAttack)) return false;
        return ContentCardPlayRules.TryCalculateCost(
            instance.Definition,
            actionPointController.CurrentActionPoints,
            player.State.Mana,
            instance.FreePlay,
            out _,
            out _);
    }

    /// <summary>
    /// 校验费用和目标、原子扣除资源，并优先使用数据库行为执行器结算已迁移卡牌。
    /// </summary>
    /// <param name="instance">即将打出的卡牌实例。</param>
    /// <param name="target">玩家选择的可选单位。</param>
    /// <param name="targetCell">玩家选择的可选地块。</param>
    /// <param name="direction">玩家选择的可选正交方向。</param>
    /// <returns>是否成功及后续移动、结束回合等控制结果。</returns>
    public CardPlayResult TryPlay(CardInstance instance, Unit target = null,
        BoardCell targetCell = null, Vector2Int? direction = null)
    {
        CardPlayResult result = new CardPlayResult();
        ResolveReferences();
        if (!CanAfford(instance)) return result;

        if (!ValidateTarget(instance.Definition, target, targetCell, direction)) return result;
        if (!ContentCardEffectExecutor.CanExecuteOnPlay(instance.Definition)) return result;

        int xAction = actionPointController.CurrentActionPoints;
        int xMana = player.State.Mana;
        int spentAction = 0;
        int spentMana = 0;
        if (!instance.FreePlay)
        {
            if (!ContentCardPlayRules.TryCalculateCost(
                    instance.Definition, xAction, xMana, false, out int actionCost, out int manaCost))
            {
                return result;
            }
            spentAction = actionCost;
            spentMana = manaCost;
            if (!actionPointController.TrySpendActionPoints(actionCost) || !player.State.TrySpendMana(manaCost))
                return result;
        }

        ContentCardExecutionContext context = new ContentCardExecutionContext(
            instance,
            player,
            target,
            targetCell,
            direction,
            handCardSystem,
            actionPointController,
            result,
            true,
            xAction,
            xMana,
            spentAction,
            spentMana,
            targetQueryService: handCardSystem);
        result.ExecutionContext = context;
        result.Success = ContentCardEffectExecutor.TryExecuteOnPlay(context);
        return result;
    }
    /// <summary>
    /// 按数据库目标规则验证选择模式、距离、阵营、存活状态、自身许可和视线要求。
    /// </summary>
    /// <param name="card">数据库发布的权威卡牌定义。</param>
    /// <param name="target">玩家选择的可选单位。</param>
    /// <param name="cell">玩家选择的可选地块。</param>
    /// <param name="direction">玩家选择的可选正交方向。</param>
    /// <returns>选择满足全部结构化目标约束时返回 true。</returns>
    private bool ValidateTarget(CardDefinition card, Unit target, BoardCell cell, Vector2Int? direction)
    {
        CardTargetRule rule = card.Target;
        if (rule.SelectionMode != "none" && rule.SelectionMode != "self" &&
            rule.SelectionMode != "direction" && (player == null || player.Board == null))
        {
            return false;
        }
        Vector2Int targetPosition = target != null ? target.Position : cell != null ? cell.Coordinate : default;
        int effectiveRange = rule.Range + (card.FamilyId == "bow"
            ? ContentClassPassiveRuntime.GetSelectedTraitInt("bow_range_bonus") : 0);
        bool hasBoardTarget = target != null || cell != null;
        ContentCardTargetSelection selection = new ContentCardTargetSelection
        {
            HasUnit = target != null,
            HasCell = cell != null,
            HasDirection = direction.HasValue,
            DirectionX = direction?.x ?? 0,
            DirectionY = direction?.y ?? 0,
            Distance = hasBoardTarget && player != null
                ? Mathf.Abs(player.Position.x - targetPosition.x) + Mathf.Abs(player.Position.y - targetPosition.y)
                : -1,
            TargetIsSelf = target == player,
            TargetHasSameFaction = target != null && player != null && target.Faction == player.Faction,
            TargetIsAlive = target != null && target.IsAlive,
            HasLineOfSight = !rule.RequiresLineOfSight ||
                             (target != null && HasLineOfSight(player, target)) ||
                             (cell != null && HasLineOfSightToCell(player, cell.Coordinate))
        };
        CardTargetRule effectiveRule = new CardTargetRule
        {
            SelectionMode = rule.SelectionMode,
            Range = effectiveRange,
            TeamFilter = rule.TeamFilter,
            LifeStateFilter = rule.LifeStateFilter,
            RequiresLineOfSight = rule.RequiresLineOfSight,
            AllowSelf = rule.AllowSelf
        };
        CardDefinition effectiveCard = new CardDefinition { Target = effectiveRule };
        return ContentCardPlayRules.ValidateTarget(effectiveCard, selection);
    }

    /// <summary>
    /// 检查两个单位之间的离散射线是否被棋盘边界或中间占用单位阻挡。
    /// </summary>
    /// <param name="from">视线来源单位。</param>
    /// <param name="to">视线目标单位。</param>
    /// <returns>中间采样格全部有效且未占用时返回 true。</returns>
    private bool HasLineOfSight(Unit from, Unit to)
    {
        Vector2Int delta = to.Position - from.Position;
        int steps = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
        for (int i = 1; i < steps; i++)
        {
            int x = Mathf.RoundToInt(Mathf.Lerp(from.Position.x, to.Position.x, (float)i / steps));
            int y = Mathf.RoundToInt(Mathf.Lerp(from.Position.y, to.Position.y, (float)i / steps));
            if (!from.Board.TryGetCell(x, y, out _) || from.Board.IsOccupied(x, y)) return false;
        }
        return true;
    }

    /// <summary>
    /// 检查单位到目标地块的离散射线是否被棋盘边界或中间占用单位阻挡。
    /// </summary>
    /// <param name="from">视线来源单位。</param>
    /// <param name="destination">目标地块坐标。</param>
    /// <returns>中间采样格全部有效且未占用时返回 true。</returns>
    private bool HasLineOfSightToCell(Unit from, Vector2Int destination)
    {
        Vector2Int delta = destination - from.Position;
        int steps = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
        for (int i = 1; i < steps; i++)
        {
            int x = Mathf.RoundToInt(Mathf.Lerp(from.Position.x, destination.x, (float)i / steps));
            int y = Mathf.RoundToInt(Mathf.Lerp(from.Position.y, destination.y, (float)i / steps));
            if (!from.Board.TryGetCell(x, y, out _) || from.Board.IsOccupied(x, y)) return false;
        }
        return true;
    }

    /// <summary>
    /// 在序列化引用缺失时按场景约定补齐行动点、手牌和双方单位对象。
    /// </summary>
    private void ResolveReferences()
    {
        actionPointController ??= FindAnyObjectByType<BoardClickController>();
        handCardSystem ??= FindAnyObjectByType<HandCardSystem>();
        if (player == null) player = GameObject.Find("Player")?.GetComponent<Unit>();
        if (enemy == null) enemy = GameObject.Find("Monster")?.GetComponent<Unit>();
    }
}
