using System;
using System.Collections.Generic;
using System.Linq;
using LegendsOfFurry.Content.Contracts;
using UnityEngine;

#pragma warning disable 0649 // Unity JsonUtility 会通过反射填充效果参数 DTO 字段。

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>
/// 保存一次数据库卡牌结算需要的战斗对象；首期仅承载同步 OnPlay 基础效果。
/// </summary>
public sealed class ContentCardExecutionContext
{
    /// <summary>
    /// 创建一次卡牌执行上下文，确保所有效果共享同一目标和结果对象。
    /// </summary>
    /// <param name="card">当前卡牌实例。</param>
    /// <param name="source">打出卡牌的单位。</param>
    /// <param name="selectedUnit">玩家选择的可选单位。</param>
    /// <param name="selectedCell">玩家选择的可选地块。</param>
    /// <param name="direction">玩家选择的可选方向。</param>
    /// <param name="hand">手牌和牌区服务。</param>
    /// <param name="actionPoints">行动点与移动输入服务。</param>
    /// <param name="result">需要写入后续控制结果的对象。</param>
    /// <param name="enablePresentation">是否播放伤害表现；自动规则测试可关闭。</param>
    public ContentCardExecutionContext(
        CardInstance card,
        Unit source,
        Unit selectedUnit,
        BoardCell selectedCell,
        Vector2Int? direction,
        ICardDrawService hand,
        BoardClickController actionPoints,
        CardPlayResult result,
        bool enablePresentation = true,
        int actionPointsBefore = -1,
        int manaBefore = -1,
        int spentActionPoints = 0,
        int spentMana = 0,
        IContentRandomSource randomSource = null,
        IContentTargetQueryService targetQueryService = null,
        ContentRuleQuery ruleQuery = null)
        : this(
            ContentBehaviorOwner.FromCard(card),
            source,
            selectedUnit,
            selectedCell,
            direction,
            hand,
            actionPoints,
            result,
            enablePresentation,
            actionPointsBefore,
            manaBefore,
            spentActionPoints,
            spentMana,
            randomSource,
            targetQueryService,
            ruleQuery)
    {
    }

    /// <summary>
    /// Creates an execution context for any real behavior owner, including statuses and class profiles.
    /// Card-only selectors remain unavailable when the owner is not a card.
    /// </summary>
    /// <param name="owner">The definition and optional instance that own the graph.</param>
    /// <param name="source">The unit causing the behavior.</param>
    /// <param name="selectedUnit">The optional selected unit.</param>
    /// <param name="selectedCell">The optional selected board cell.</param>
    /// <param name="direction">The optional selected direction.</param>
    /// <param name="hand">The optional card draw and zone service.</param>
    /// <param name="actionPoints">The optional action point service.</param>
    /// <param name="result">The shared execution result.</param>
    /// <param name="enablePresentation">Whether presentation-only effects may run.</param>
    /// <param name="actionPointsBefore">Action points before this execution, or -1 when unknown.</param>
    /// <param name="manaBefore">Mana before this execution, or -1 when unknown.</param>
    /// <param name="spentActionPoints">Action points already committed by the caller.</param>
    /// <param name="spentMana">Mana already committed by the caller.</param>
    /// <param name="randomSource">The optional deterministic random source.</param>
    /// <param name="targetQueryService">The optional unit and card target query service.</param>
    public ContentCardExecutionContext(
        ContentBehaviorOwner owner,
        Unit source,
        Unit selectedUnit,
        BoardCell selectedCell,
        Vector2Int? direction,
        ICardDrawService hand,
        BoardClickController actionPoints,
        CardPlayResult result,
        bool enablePresentation = true,
        int actionPointsBefore = -1,
        int manaBefore = -1,
        int spentActionPoints = 0,
        int spentMana = 0,
        IContentRandomSource randomSource = null,
        IContentTargetQueryService targetQueryService = null,
        ContentRuleQuery ruleQuery = null)
    {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Card = owner.RuntimeInstance as CardInstance;
        Source = source;
        SelectedUnit = selectedUnit;
        SelectedCell = selectedCell;
        Direction = direction;
        Hand = hand;
        CardZones = hand as IContentCardZoneService;
        ActionPoints = actionPoints;
        Result = result;
        EnablePresentation = enablePresentation;
        Resources = new CardPlayResourceSnapshot(
            actionPointsBefore,
            manaBefore,
            spentActionPoints,
            spentMana);
        RandomSource = randomSource ?? UnityContentRandomSource.Instance;
        TargetQueryService = targetQueryService;
        RuleQuery = ruleQuery;
        RuleQuerySession = ruleQuery?.Session ?? new ContentRuleQuerySession();
    }

    public ContentBehaviorOwner Owner { get; }
    public CardInstance Card { get; }
    public Unit Source { get; }
    public Unit SelectedUnit { get; }
    public BoardCell SelectedCell { get; }
    public Vector2Int? Direction { get; }
    public ICardDrawService Hand { get; }
    public IContentCardZoneService CardZones { get; }
    public BoardClickController ActionPoints { get; }
    public CardPlayResult Result { get; }
    public bool EnablePresentation { get; }
    public CardPlayResourceSnapshot Resources { get; }
    public IReadOnlyList<CardDamageRecord> DamageRecords => damageRecords;
    public object CurrentGraphTarget { get; private set; }
    public IContentRandomSource RandomSource { get; }
    public IContentTargetQueryService TargetQueryService { get; }
    public ContentRuleQuery RuleQuery { get; }
    public ContentRuleQuerySession RuleQuerySession { get; }
    public bool IsAwaitingInteraction { get; private set; }
    public string PendingInteractionNodeId { get; private set; }
    public string PendingInteractionKind { get; private set; }
    public string CurrentBehaviorId { get; private set; }
    public string CurrentTriggerKey { get; private set; }
    public IReadOnlyList<ContentCombatLogEntry> CombatLog => combatLog;

    private readonly List<CardDamageRecord> damageRecords = new List<CardDamageRecord>();
    private readonly List<ContentCombatLogEntry> combatLog = new List<ContentCombatLogEntry>();
    private readonly HashSet<Unit> quickTargets = new HashSet<Unit>();

    /// <summary>设置当前解释中的行为和触发器，供每个节点生成可定位日志。</summary>
    internal void BeginBehavior(BehaviorDefinition behavior)
    {
        CurrentBehaviorId = behavior?.BehaviorId ?? string.Empty;
        CurrentTriggerKey = behavior?.TriggerKey ?? string.Empty;
    }

    /// <summary>记录一个行为节点的目标、输入参数和执行结果。</summary>
    internal void RecordCombatLog(BehaviorNodeDefinition node, bool succeeded)
    {
        object target = CurrentGraphTarget ?? (object)SelectedUnit ?? Source;
        combatLog.Add(new ContentCombatLogEntry(
            Owner.OwnerKind,
            Owner.OwnerId,
            CurrentTriggerKey,
            CurrentBehaviorId,
            node?.NodeId ?? string.Empty,
            node?.OperationKey ?? string.Empty,
            target is Unit unit ? unit.DisplayName : target?.ToString() ?? string.Empty,
            node?.ParametersJson ?? "{}",
            succeeded ? "succeeded" : "failed"));
    }

    /// <summary>
    /// 设置行为图 foreach 当前迭代目标；解释器会在退出嵌套遍历时恢复上一层值。
    /// </summary>
    /// <param name="target">当前迭代对象，可为单位、卡牌实例或牌区对象。</param>
    internal void SetCurrentGraphTarget(object target)
    {
        CurrentGraphTarget = target;
    }

    /// <summary>记录需要玩家继续操作的节点，使出牌结果可以暴露暂停原因。</summary>
    internal void MarkAwaitingInteraction(string nodeId, string kind)
    {
        IsAwaitingInteraction = true;
        PendingInteractionNodeId = nodeId ?? string.Empty;
        PendingInteractionKind = kind ?? string.Empty;
    }

    /// <summary>由界面或移动控制器在玩家完成选择后解除本次交互暂停。</summary>
    public void CompletePendingInteraction()
    {
        IsAwaitingInteraction = false;
        PendingInteractionNodeId = string.Empty;
        PendingInteractionKind = string.Empty;
    }

    /// <summary>
    /// 记录一个伤害节点的请求值、护甲吸收、生命伤害和击杀结果，供后续条件与日志查询。
    /// </summary>
    /// <param name="nodeId">产生伤害的行为节点 ID。</param>
    /// <param name="target">本次受伤单位。</param>
    /// <param name="requestedDamage">进入单位伤害管线前的请求值。</param>
    /// <param name="armorBefore">结算前护甲。</param>
    /// <param name="armorAfter">结算后护甲。</param>
    /// <param name="healthBefore">结算前生命。</param>
    /// <param name="healthAfter">结算后生命。</param>
    /// <param name="actualHealthDamage">伤害管线报告的实际生命伤害，复活时也保留本次损失。</param>
    public void RecordDamage(
        string nodeId,
        Unit target,
        int requestedDamage,
        int armorBefore,
        int armorAfter,
        int healthBefore,
        int healthAfter,
        int actualHealthDamage)
    {
        damageRecords.Add(new CardDamageRecord(
            nodeId,
            target,
            requestedDamage,
            Math.Max(0, armorBefore - armorAfter),
            Math.Max(0, actualHealthDamage),
            healthBefore > 0 && healthAfter <= 0));
    }

    /// <summary>
    /// 判断指定单位是否在当前卡牌上下文中由任一已记录伤害节点击杀。
    /// </summary>
    /// <param name="target">需要查询的单位。</param>
    /// <returns>存在命中该单位且标记为击杀的伤害记录时返回 true。</returns>
    public bool WasKilledByThisCard(Unit target)
    {
        return target != null && damageRecords.Any(record => record.Target == target && record.KilledTarget);
    }

    /// <summary>Applies authored outgoing-damage and hit-count rule queries.</summary>
    /// <param name="target">当前伤害目标。</param>
    /// <param name="baseAmount">行为图计算出的基础伤害。</param>
    /// <param name="amount">应用锋利、恍惚、心火和剑蓄力后的每段伤害。</param>
    /// <returns>普通为一段；具有速攻且该目标尚未获得额外段时为两段。</returns>
    internal int PrepareAttackDamage(Unit target, int baseAmount, out int amount)
    {
        amount = Math.Max(0, baseAmount);
        if (Source == null || Card?.Definition?.IsAttack != true || target == null) return 1;
        ContentRuleQuery damage = ContentRuleQueryRuntime.Evaluate(new ContentRuleQuery(
            ContentRuleQueryKeys.OutgoingAttackDamage, Source, target, amount, Card.Definition,
            session: RuleQuerySession));
        amount = Math.Max(0, damage.Value);
        if (!quickTargets.Add(target)) return 1;
        ContentRuleQuery hits = ContentRuleQueryRuntime.Evaluate(new ContentRuleQuery(
            ContentRuleQueryKeys.AttackHitCount, Source, target, 1, Card.Definition,
            session: RuleQuerySession));
        return Math.Max(0, hits.Value);
    }

    /// <summary>Dispatches the generic post-attack lifecycle query once per card execution.</summary>
    internal void FinishAttackModifiers()
    {
        if (Source == null || Card?.Definition?.IsAttack != true) return;
        ContentRuleQueryRuntime.Evaluate(new ContentRuleQuery(
            ContentRuleQueryKeys.AfterAttack, Source, SelectedUnit, 0, Card.Definition,
            session: RuleQuerySession));
    }
}

/// <summary>
/// 保存一次出牌提交前后的资源快照，X 费用表达式只读取这里的稳定值。
/// </summary>
public sealed class CardPlayResourceSnapshot
{
    /// <summary>
    /// 创建不可变资源快照；未知的提交前资源可传入 -1。
    /// </summary>
    /// <param name="actionPointsBefore">提交费用前行动点。</param>
    /// <param name="manaBefore">提交费用前法力。</param>
    /// <param name="spentActionPoints">本次实际消耗行动点。</param>
    /// <param name="spentMana">本次实际消耗法力。</param>
    public CardPlayResourceSnapshot(int actionPointsBefore, int manaBefore, int spentActionPoints, int spentMana)
    {
        ActionPointsBefore = actionPointsBefore;
        ManaBefore = manaBefore;
        SpentActionPoints = Math.Max(0, spentActionPoints);
        SpentMana = Math.Max(0, spentMana);
        ActionPointsAfter = actionPointsBefore < 0 ? -1 : Math.Max(0, actionPointsBefore - SpentActionPoints);
        ManaAfter = manaBefore < 0 ? -1 : Math.Max(0, manaBefore - SpentMana);
    }

    public int ActionPointsBefore { get; }
    public int ManaBefore { get; }
    public int SpentActionPoints { get; }
    public int SpentMana { get; }
    public int ActionPointsAfter { get; }
    public int ManaAfter { get; }
}

/// <summary>
/// 保存一次伤害效果的确定性结算摘要，供击杀条件、战斗日志和回归测试使用。
/// </summary>
public sealed class CardDamageRecord
{
    /// <summary>
    /// 创建一次伤害结算记录。
    /// </summary>
    /// <param name="nodeId">产生伤害的行为节点 ID。</param>
    /// <param name="target">受伤单位。</param>
    /// <param name="requestedDamage">请求伤害。</param>
    /// <param name="absorbedByArmor">被护甲吸收的数值。</param>
    /// <param name="healthDamage">实际生命伤害。</param>
    /// <param name="killedTarget">本次结算是否使目标死亡。</param>
    public CardDamageRecord(
        string nodeId,
        Unit target,
        int requestedDamage,
        int absorbedByArmor,
        int healthDamage,
        bool killedTarget)
    {
        NodeId = nodeId ?? string.Empty;
        Target = target;
        RequestedDamage = requestedDamage;
        AbsorbedByArmor = absorbedByArmor;
        HealthDamage = healthDamage;
        KilledTarget = killedTarget;
    }

    public string NodeId { get; }
    public Unit Target { get; }
    public int RequestedDamage { get; }
    public int AbsorbedByArmor { get; }
    public int HealthDamage { get; }
    public bool KilledTarget { get; }
}

/// <summary>
/// 解释首期 WPF 可创建的同步 OnPlay 行为，并拒绝尚未实现的节点以防扣费后静默跳过。
/// </summary>
public static class ContentCardEffectExecutor
{
    private static readonly ContentBehaviorRuntime BehaviorRuntime = new ContentBehaviorRuntime();
    private static readonly ContentValueExpressionResolver ValueResolver = new ContentValueExpressionResolver();

    /// <summary>
    /// 在扣费前确认所有启用的 OnPlay 行为均为当前垂直切片支持的顺序基础效果。
    /// </summary>
    /// <param name="definition">即将执行的数据库卡牌定义。</param>
    /// <returns>行为结构和效果 key 均可执行时返回 true。</returns>
    public static bool CanExecuteOnPlay(CardDefinition definition)
    {
        return CanExecuteTrigger(definition, "on_play");
    }

    /// <summary>在触发前确认指定卡牌生命周期行为的全部节点均可由当前运行时执行。</summary>
    /// <param name="definition">需要检查的卡牌定义。</param>
    /// <param name="triggerKey">稳定触发器 key。</param>
    /// <returns>至少存在一个行为且全部行为可执行时返回 true。</returns>
    public static bool CanExecuteTrigger(CardDefinition definition, string triggerKey)
    {
        if (definition == null || string.IsNullOrWhiteSpace(triggerKey)) return false;
        return CanExecuteOwnedTrigger(
            ContentDefinitionKinds.Card,
            definition.CardId,
            definition.Behaviors,
            triggerKey);
    }

    /// <summary>
    /// Verifies one real definition owner's trigger without converting it into a synthetic card.
    /// </summary>
    /// <param name="ownerKind">The definition kind expected on every behavior.</param>
    /// <param name="ownerId">The stable definition ID expected on every behavior.</param>
    /// <param name="sourceBehaviors">All behavior graphs owned by the definition.</param>
    /// <param name="triggerKey">The lifecycle trigger to preflight.</param>
    /// <returns>True when at least one matching graph exists and every graph is executable.</returns>
    public static bool CanExecuteOwnedTrigger(
        string ownerKind,
        string ownerId,
        IEnumerable<BehaviorDefinition> sourceBehaviors,
        string triggerKey)
    {
        if (!ContentDefinitionKinds.CanOwnBehavior(ownerKind) || !ContentId.IsValid(ownerId) ||
            sourceBehaviors == null || string.IsNullOrWhiteSpace(triggerKey))
        {
            return false;
        }

        BehaviorDefinition[] behaviors = sourceBehaviors
            .Where(behavior => behavior.Enabled && behavior.TriggerKey == triggerKey)
            .OrderBy(behavior => behavior.Priority)
            .ThenBy(behavior => behavior.BehaviorId, StringComparer.Ordinal)
            .ToArray();
        if (behaviors.Length == 0)
        {
            return false;
        }

        return behaviors.All(behavior =>
                   behavior.OwnerKind == ownerKind && behavior.OwnerId == ownerId &&
                   BehaviorRuntime.CanExecuteBehavior(behavior)) &&
               HasValidInteractionPlacement(behaviors);
    }

    /// <summary>确保需要玩家输入的效果位于最终行为的最终叶子，完成回调后不会遗漏后续节点。</summary>
    private static bool HasValidInteractionPlacement(IReadOnlyList<BehaviorDefinition> behaviors)
    {
        HashSet<string> interactionKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "reveal_top_cards_and_choose_discard", "play_top_cards_for_free"
        };
        for (int behaviorIndex = 0; behaviorIndex < behaviors.Count; behaviorIndex++)
        {
            BehaviorDefinition behavior = behaviors[behaviorIndex];
            Dictionary<string, BehaviorNodeDefinition[]> siblings = behavior.Nodes
                .GroupBy(node => (node.ParentNodeId ?? string.Empty) + "\u001f" + node.BranchKey, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.OrderBy(node => node.SortOrder)
                    .ThenBy(node => node.NodeId, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
            foreach (BehaviorNodeDefinition interaction in behavior.Nodes.Where(node => interactionKeys.Contains(node.OperationKey)))
            {
                if (behaviorIndex != behaviors.Count - 1 || behavior.Nodes.Any(node => node.ParentNodeId == interaction.NodeId)) return false;
                BehaviorNodeDefinition current = interaction;
                while (current != null)
                {
                    string key = (current.ParentNodeId ?? string.Empty) + "\u001f" + current.BranchKey;
                    if (siblings.TryGetValue(key, out BehaviorNodeDefinition[] group) && group.Last() != current) return false;
                    current = string.IsNullOrEmpty(current.ParentNodeId)
                        ? null : behavior.Nodes.FirstOrDefault(node => node.NodeId == current.ParentNodeId);
                }
            }
        }
        return true;
    }

    /// <summary>
    /// 在扣费前验证基础效果所需的目标、常量数值和伤害类型，拒绝尚未实现的动态表达式。
    /// </summary>
    /// <param name="node">需要检查的效果节点。</param>
    /// <returns>节点参数可被当前同步执行器准确解释时返回 true。</returns>
    internal static bool CanExecuteEffectParameters(BehaviorNodeDefinition node)
    {
        EffectParametersDto parameters;
        try
        {
            parameters = JsonUtility.FromJson<EffectParametersDto>(node.ParametersJson) ?? new EffectParametersDto();
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (node.OperationKey is "end_turn" or "no_op" or "play_vfx" or "play_sfx" or "cancel_query" or
            "transform_owner_status" or "appraise_equipment") return true;

        bool requiresAmount = node.OperationKey is not "remove_status" and not "clear_statuses" and
            not "remove_cards_by_query" and not "move_cards";
        if (requiresAmount && (!TryGetParsedAmount(node.ParametersJson, out object parsedAmount) ||
                               !ValueResolver.CanResolveParsedStructure(parsedAmount))) return false;

        if (node.OperationKey is "damage" or "heal" or "gain_armor" or "revive" or "knock_back" or
            "add_status" or "set_status" or "reduce_status" or "remove_status" or "clear_statuses" or "modify_mana")
        {
            if (!ContentCapabilityCatalog.Phase3ExecutableEffectTargetKeys.Contains(parameters.target)) return false;
        }
        if (node.OperationKey is "add_status" or "set_status" or "reduce_status" or "remove_status")
        {
            if (string.IsNullOrWhiteSpace(parameters.statusId)) return false;
        }
        if (node.OperationKey == "spend_resource" && parameters.resource is not "action_points" and not "mana")
            return false;
        if (node.OperationKey is "generate_card" or "move_cards" or "remove_cards_by_query")
        {
            if (parameters.query == null) return false;
        }
        if (node.OperationKey == "generate_card" &&
            !ContentCardZoneKeys.IsConcrete(parameters.destinationZone ?? ContentCardZoneKeys.Hand))
            return false;
        if (node.OperationKey == "move_cards" &&
            (!ContentCardZoneKeys.IsConcrete(parameters.sourceZone) ||
             !ContentCardZoneKeys.IsConcrete(parameters.destinationZone) ||
             parameters.sourceZone == parameters.destinationZone))
            return false;
        if (node.OperationKey == "remove_cards_by_query" &&
            !ContentCardZoneKeys.IsConcreteOrAll(parameters.zone ?? ContentCardZoneKeys.All))
            return false;
        if (node.OperationKey == "modify_query_value" &&
            parameters.mode is not "add" and not "set" and not "min" and not "max" and not "multiply_percent")
            return false;
        return node.OperationKey != "damage" || Enum.TryParse(parameters.damageType, true, out DamageType _);
    }

    /// <summary>
    /// 按行为优先级和节点顺序执行同步 OnPlay 效果，并在任一参数或目标无效时返回 false。
    /// </summary>
    /// <param name="context">本次卡牌结算共享上下文。</param>
    /// <returns>全部基础效果成功执行时返回 true。</returns>
    public static bool TryExecuteOnPlay(ContentCardExecutionContext context)
    {
        return TryExecuteTrigger(context, "on_play");
    }

    /// <summary>按优先级执行指定卡牌生命周期触发器，并在任一节点失败时停止。</summary>
    /// <param name="context">共享卡牌执行上下文。</param>
    /// <param name="triggerKey">需要执行的稳定触发器 key。</param>
    /// <returns>全部行为执行成功或等待交互时返回 true。</returns>
    public static bool TryExecuteTrigger(ContentCardExecutionContext context, string triggerKey)
    {
        if (context == null || context.Card?.Definition == null ||
            !CanExecuteTrigger(context.Card.Definition, triggerKey))
        {
            return false;
        }

        return TryExecuteOwnedTrigger(context, context.Card.Definition.Behaviors, triggerKey);
    }

    /// <summary>
    /// Executes behavior graphs belonging to the context's real definition owner.
    /// </summary>
    /// <param name="context">The shared execution context carrying the validated owner.</param>
    /// <param name="sourceBehaviors">All graphs authored on that owner definition.</param>
    /// <param name="triggerKey">The lifecycle trigger being dispatched.</param>
    /// <returns>True when all matching graphs succeed or pause for supported interaction.</returns>
    public static bool TryExecuteOwnedTrigger(
        ContentCardExecutionContext context,
        IEnumerable<BehaviorDefinition> sourceBehaviors,
        string triggerKey)
    {
        if (context?.Owner == null || !CanExecuteOwnedTrigger(
                context.Owner.OwnerKind,
                context.Owner.OwnerId,
                sourceBehaviors,
                triggerKey))
        {
            return false;
        }

        foreach (BehaviorDefinition behavior in sourceBehaviors
                     .Where(item => item.Enabled && item.TriggerKey == triggerKey)
                     .OrderBy(item => item.Priority)
                     .ThenBy(item => item.BehaviorId, StringComparer.Ordinal))
        {
            context.BeginBehavior(behavior);
            ContentBehaviorExecutionStatus status = ContentBehaviorGraphInterpreter.Execute(
                behavior,
                context,
                BehaviorRuntime);
            if (status == ContentBehaviorExecutionStatus.AwaitingInput)
            {
                return true;
            }
            if (status != ContentBehaviorExecutionStatus.Succeeded)
            {
                Debug.LogError(
                    $"内容 {context.Owner.OwnerKind}:{context.Owner.OwnerId} 的行为 {behavior.BehaviorId} 执行结果为 {status}。",
                    context.Source);
                return false;
            }
        }
        if (context.Card != null && triggerKey == "on_play") context.FinishAttackModifiers();
        return true;
    }

    /// <summary>
    /// 执行一个白名单基础效果节点，并把需要延后处理的移动或结束回合写入结果。
    /// </summary>
    /// <param name="node">经过结构校验的效果节点。</param>
    /// <param name="context">本次卡牌结算共享上下文。</param>
    /// <returns>参数和目标有效且效果完成时返回 true。</returns>
    internal static bool TryExecuteEffect(BehaviorNodeDefinition node, ContentCardExecutionContext context)
    {
        EffectParametersDto parameters;
        try
        {
            parameters = JsonUtility.FromJson<EffectParametersDto>(node.ParametersJson) ?? new EffectParametersDto();
        }
        catch (ArgumentException)
        {
            return false;
        }

        Unit target = parameters.target switch
        {
            "self" => context.Source,
            "current_graph_target" => context.CurrentGraphTarget as Unit,
            _ => context.SelectedUnit
        };
        int amount = 0;
        bool requiresAmount = node.OperationKey is not "end_turn" and not "no_op" and not "play_vfx" and not "play_sfx" and
            not "remove_status" and not "clear_statuses" and not "remove_cards_by_query" and not "move_cards" and
            not "cancel_query" and not "transform_owner_status" and not "appraise_equipment";
        if (requiresAmount && !TryResolveAmount(node.ParametersJson, context, target, out amount)) return false;
        switch (node.OperationKey)
        {
            case "modify_query_value":
                if (context.RuleQuery == null || !MatchesQuery(parameters, context.RuleQuery)) return false;
                if (!TryApplyQueryEffect(context, node, parameters)) return true;
                context.RuleQuery.Touched = true;
                context.RuleQuery.Value = parameters.mode switch
                {
                    "set" => amount,
                    "min" => Math.Min(context.RuleQuery.Value, amount),
                    "max" => Math.Max(context.RuleQuery.Value, amount),
                    "multiply_percent" => Mathf.FloorToInt(context.RuleQuery.Value * amount / 100f),
                    _ => context.RuleQuery.Value + amount
                };
                return true;
            case "cancel_query":
                if (context.RuleQuery == null || !MatchesQuery(parameters, context.RuleQuery)) return false;
                if (!TryApplyQueryEffect(context, node, parameters)) return true;
                context.RuleQuery.Touched = true;
                context.RuleQuery.Cancelled = true;
                return true;
            case "consume_owner_status":
                if (context.Owner.RuntimeInstance is not RuntimeStatusInstance ownerStatus ||
                    (context.RuleQuery != null && !MatchesQuery(parameters, context.RuleQuery))) return false;
                if (context.RuleQuery != null) context.RuleQuery.Touched = true;
                context.Source?.State.Reduce(ownerStatus.StatusId, Mathf.Max(1, amount));
                return true;
            case "transform_owner_status":
                if (context.Owner.RuntimeInstance is not RuntimeStatusInstance transformed || context.Source == null ||
                    parameters.threshold <= 0 || string.IsNullOrWhiteSpace(parameters.resultStatusId)) return false;
                if (transformed.Stacks < parameters.threshold ||
                    (!string.IsNullOrWhiteSpace(parameters.blockedByStatusId) &&
                     context.Source.State.Has(parameters.blockedByStatusId))) return true;
                context.Source.State.Remove(transformed.StatusId);
                context.Source.State.Add(parameters.resultStatusId, 1, parameters.durationTurns, transformed.SourceId);
                return true;
            case "damage":
                if (target == null) return false;
                int hitCount = context.PrepareAttackDamage(target, amount, out int modifiedAmount);
                for (int hit = 0; hit < hitCount; hit++)
                {
                    int healthBefore = target.CurrentHealth;
                    int armorBefore = target.Armor;
                    DamageResolution damage = target.ResolveDamage(new DamageRequest(
                        context.Source, target, modifiedAmount,
                        ParseDamageType(parameters.damageType), context.EnablePresentation));
                    context.RecordDamage(node.NodeId, target, modifiedAmount, armorBefore, target.Armor,
                        healthBefore, target.CurrentHealth, damage.HealthDamage);
                }
                return true;
            case "heal":
                if (target == null) return false;
                target.Heal(Mathf.Max(0, amount));
                return true;
            case "gain_armor":
                if (target == null) return false;
                target.AddArmor(Mathf.Max(0, amount));
                return true;
            case "revive":
                if (target == null || target.IsAlive || amount <= 0) return false;
                target.Revive(amount);
                return true;
            case "knock_back":
                return target != null && TryKnockBack(context.Source, target, Mathf.Max(0, amount));
            case "add_status":
                if (target == null || string.IsNullOrWhiteSpace(parameters.statusId)) return false;
                target.State.Add(parameters.statusId, Mathf.Max(0, amount), Mathf.Max(0, parameters.durationTurns), context.Source?.DisplayName);
                return true;
            case "set_status":
                if (target == null || string.IsNullOrWhiteSpace(parameters.statusId)) return false;
                target.State.Set(parameters.statusId, Mathf.Max(0, amount), Mathf.Max(0, parameters.durationTurns), context.Source?.DisplayName);
                return true;
            case "reduce_status":
                if (target == null || string.IsNullOrWhiteSpace(parameters.statusId)) return false;
                target.State.Reduce(parameters.statusId, Mathf.Max(1, amount));
                return true;
            case "remove_status":
                if (target == null || string.IsNullOrWhiteSpace(parameters.statusId)) return false;
                target.State.Remove(parameters.statusId);
                return true;
            case "clear_statuses":
                if (target == null) return false;
                if (parameters.mode == "negative") target.State.ClearNegative();
                else if (parameters.mode == "all") target.State.ClearAll();
                else return false;
                return true;
            case "modify_action_points":
                if (context.ActionPoints == null) return false;
                if (amount >= 0) context.ActionPoints.GainActionPoints(amount);
                else if (!context.ActionPoints.TrySpendActionPoints(-amount)) return false;
                return true;
            case "modify_max_action_points":
                if (context.ActionPoints == null || amount < 0) return false;
                context.ActionPoints.IncreaseMaximumActionPoints(amount);
                return true;
            case "modify_mana":
                if (target == null) return false;
                return amount >= 0 ? target.State.TryGainMana(amount) : target.State.TrySpendMana(-amount);
            case "spend_resource":
                if (parameters.resource == "action_points")
                    return context.ActionPoints != null && context.ActionPoints.TrySpendActionPoints(Mathf.Max(0, amount));
                if (parameters.resource == "mana")
                    return context.Source != null && context.Source.State.TrySpendMana(Mathf.Max(0, amount));
                return false;
            case "draw_cards":
                if (context.Hand == null) return false;
                context.Hand.DrawCards(Mathf.Max(0, amount), true);
                return true;
            case "generate_card":
                return context.CardZones != null &&
                       context.CardZones.GenerateCards(parameters.query, Mathf.Max(0, amount),
                           parameters.destinationZone ?? ContentCardZoneKeys.Hand);
            case "move_cards":
                return context.CardZones != null &&
                       context.CardZones.MoveCards(parameters.query, Mathf.Max(0, amount), parameters.sourceZone, parameters.destinationZone) >= 0;
            case "remove_cards_by_query":
                return context.CardZones != null && context.CardZones.RemoveCards(
                    parameters.query, parameters.zone ?? ContentCardZoneKeys.All) >= 0;
            case "modify_card_runtime_value":
                if (context.Card == null || string.IsNullOrWhiteSpace(parameters.runtimeKey)) return false;
                context.Card.ModifyRuntimeValue(parameters.runtimeKey, amount);
                return true;
            case "reveal_top_cards_and_choose_discard":
                if (context.CardZones == null) return false;
                context.MarkAwaitingInteraction(node.NodeId, "reveal_top_cards_and_choose_discard");
                context.CardZones.RevealTopCardsAndChooseDiscard(
                    Mathf.Max(0, amount), context.CompletePendingInteraction);
                return true;
            case "play_top_cards_for_free":
                if (context.CardZones == null) return false;
                context.MarkAwaitingInteraction(node.NodeId, "play_top_cards_for_free");
                context.CardZones.PlayTopCardsForFree(
                    Mathf.Max(0, amount), context.CompletePendingInteraction);
                return true;
            case "begin_free_move":
                context.Result.FreeMoveSteps += Mathf.Max(0, amount);
                return true;
            case "end_turn":
                context.Result.EndTurn = true;
                return true;
            case "no_op":
                return true;
            case "play_vfx":
            case "play_sfx":
                if (context.EnablePresentation)
                    Debug.Log($"内容表现请求：{node.OperationKey} / {parameters.assetKey}", context.Source);
                return true;
            case "appraise_equipment":
                return TryAppraiseEquipment(context, parameters);
            default:
                return false;
        }
    }

    /// <summary>鉴定当前绑定装备：先诅咒判定，再按品质套表抽一张临时消耗牌入手，满手则进弃牌。</summary>
    private static bool TryAppraiseEquipment(ContentCardExecutionContext context, EffectParametersDto parameters)
    {
        if (context?.CardZones == null || !ContentRuntime.IsLoaded) return false;
        string equipmentId = parameters.equipmentId;
        if (string.IsNullOrWhiteSpace(equipmentId))
            EquipmentAppraisal.TryGetEquipmentIdFromCard(context.Card?.Definition.CardId, out equipmentId);
        if (!ContentRuntime.Registry.TryGetEquipment(equipmentId, out EquipmentDefinition equipment))
            return false;
        CardDefinition reward = EquipmentAppraisal.Roll(
            ContentRuntime.Registry.GetCardsInPool(equipment.CardPoolId),
            context.RandomSource.NextUnit(),
            context.RandomSource.NextUnit(),
            context.RandomSource.NextUnit());
        if (reward == null) return true;
        CardInstance instance = new CardInstance(reward);
        instance.ApplyAppraisalRewardFlags();
        return context.CardZones.AddCardToHandOrDiscard(instance);
    }

    private static bool MatchesQuery(EffectParametersDto parameters, ContentRuleQuery query)
    {
        if (!string.IsNullOrWhiteSpace(parameters.damageType) &&
            !string.Equals(parameters.damageType, query.DamageTypeId, StringComparison.Ordinal)) return false;
        if (!string.IsNullOrWhiteSpace(parameters.familyId) &&
            !string.Equals(parameters.familyId, query.Card?.FamilyId, StringComparison.Ordinal)) return false;
        if (!string.IsNullOrWhiteSpace(parameters.tag) &&
            (query.Card?.Tags == null || !query.Card.Tags.Contains(parameters.tag, StringComparer.Ordinal))) return false;
        if (parameters.requiresAttack && query.Card?.IsAttack != true) return false;
        return true;
    }

    private static bool TryApplyQueryEffect(
        ContentCardExecutionContext context,
        BehaviorNodeDefinition node,
        EffectParametersDto parameters)
    {
        string key = context.Owner.OwnerKind + ":" + context.Owner.OwnerId + ":" + node.NodeId;
        if (parameters.scope == "once_per_battle") return context.Source != null && context.Source.State.TryMarkRuleUsed(key);
        return context.RuleQuery.Session.TryApply(key, parameters.scope, context.RuleQuery.OtherUnit);
    }

    /// <summary>从效果 JSON 中提取 amount 表达式的安全解析对象。</summary>
    private static bool TryGetParsedAmount(string json, out object amount)
    {
        amount = null;
        return ContentSafeJsonParser.TryParse(json, out object parsed) &&
               parsed is Dictionary<string, object> values && values.TryGetValue("amount", out amount);
    }

    /// <summary>计算效果 amount 表达式并保留负数资源修改值。</summary>
    private static bool TryResolveAmount(string json, ContentCardExecutionContext context, Unit target, out int amount)
    {
        amount = 0;
        return TryGetParsedAmount(json, out object parsed) && ValueResolver.TryResolveParsed(parsed, context, target, out amount);
    }

    /// <summary>尝试把目标沿远离来源的方向击退指定格数，遇到边界或占用时停止。</summary>
    private static bool TryKnockBack(Unit source, Unit target, int steps)
    {
        if (source == null || target == null || target.Board == null || steps < 0) return false;
        Vector2Int delta = target.Position - source.Position;
        Vector2Int direction = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)
            ? new Vector2Int(delta.x == 0 ? 0 : (int)Mathf.Sign(delta.x), 0)
            : new Vector2Int(0, delta.y == 0 ? 0 : (int)Mathf.Sign(delta.y));
        if (direction == Vector2Int.zero) return false;
        Vector2Int destination = target.Position;
        for (int index = 0; index < steps; index++)
        {
            Vector2Int next = destination + direction;
            if (!target.Board.TryGetCell(next.x, next.y, out _) || target.Board.IsOccupied(next.x, next.y, target)) break;
            destination = next;
        }
        return destination == target.Position || target.MoveToAnimated(destination.x, destination.y);
    }

    /// <summary>
    /// 将内容中的稳定伤害类型字符串映射到现有伤害管线枚举。
    /// </summary>
    /// <param name="damageType">例如 normal、fire 或 true。</param>
    /// <returns>现有 Unit.TakeTypedDamage 使用的伤害类型。</returns>
    private static DamageType ParseDamageType(string damageType)
    {
        return Enum.TryParse(damageType, true, out DamageType parsed) ? parsed : DamageType.Normal;
    }

    /// <summary>表示首期效果节点中的目标、常量数值和伤害类型字段。</summary>
    [Serializable]
    private sealed class EffectParametersDto
    {
        public string target;
        public ConstantValueDto amount;
        public string damageType;
        public string statusId;
        public int durationTurns;
        public string mode;
        public string resource;
        public ContentCardQuery query;
        public string sourceZone;
        public string destinationZone;
        public string zone;
        public string runtimeKey;
        public string assetKey;
        public string familyId;
        public string tag;
        public string scope;
        public bool requiresAttack;
        public int threshold;
        public string resultStatusId;
        public string blockedByStatusId;
        public string equipmentId;
    }

    /// <summary>表示 kind=constant 的整数数值表达式。</summary>
    [Serializable]
    private sealed class ConstantValueDto
    {
        public string kind;
        public int value;
    }
}

/// <summary>表示一条可按卡牌、触发器、行为和节点定位的结构化战斗日志。</summary>
public sealed class ContentCombatLogEntry
{
    /// <summary>创建一条不可变节点执行日志。</summary>
    public ContentCombatLogEntry(string ownerKind, string ownerId, string triggerKey, string behaviorId, string nodeId,
        string operationKey, string target, string inputJson, string result)
    {
        OwnerKind = ownerKind;
        OwnerId = ownerId;
        TriggerKey = triggerKey;
        BehaviorId = behaviorId;
        NodeId = nodeId;
        OperationKey = operationKey;
        Target = target;
        InputJson = inputJson;
        Result = result;
    }

    public string OwnerKind { get; }
    public string OwnerId { get; }
    public string CardId => OwnerKind == ContentDefinitionKinds.Card ? OwnerId : string.Empty;
    public string TriggerKey { get; }
    public string BehaviorId { get; }
    public string NodeId { get; }
    public string OperationKey { get; }
    public string Target { get; }
    public string InputJson { get; }
    public string Result { get; }
}
}

#pragma warning restore 0649
