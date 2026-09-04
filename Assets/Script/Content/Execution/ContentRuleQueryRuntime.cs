using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using LegendsOfFurry.Content.Contracts;

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>状态、职业、角色和装备行为图可以修正的稳定引擎查询 key。</summary>
public static class ContentRuleQueryKeys
{
    public const string OutgoingAttackDamage = "query_outgoing_attack_damage";
    public const string AttackHitCount = "query_attack_hit_count";
    public const string IncomingDamage = "query_incoming_damage";
    public const string ArmorGain = "query_armor_gain";
    public const string ArmorRetention = "query_armor_retention";
    public const string MaximumActionPoints = "query_maximum_action_points";
    public const string MoveSteps = "query_move_steps";
    public const string CanPlayCard = "query_can_play_card";
    public const string ManaGain = "query_mana_gain";
    public const string TargetRange = "query_target_range";
    public const string CanTakeTurn = "query_can_take_turn";
    public const string AfterAttack = "query_after_attack";
    public const string LethalRecovery = "query_lethal_recovery";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        OutgoingAttackDamage, AttackHitCount, IncomingDamage, ArmorGain, ArmorRetention, MaximumActionPoints,
        MoveSteps, CanPlayCard, ManaGain, TargetRange, CanTakeTurn, AfterAttack, LethalRecovery
    });
}

/// <summary>经过策划修正图和拦截图传递的可变数值/取消请求。</summary>
public sealed class ContentRuleQuery
{
    public ContentRuleQuery(string queryKey, Unit owner, Unit otherUnit, int value,
        CardDefinition card = null, string damageTypeId = "", ContentRuleQuerySession session = null)
    {
        QueryKey = queryKey ?? string.Empty;
        Owner = owner;
        OtherUnit = otherUnit;
        Value = value;
        Card = card;
        DamageTypeId = damageTypeId ?? string.Empty;
        Session = session ?? new ContentRuleQuerySession();
    }

    public string QueryKey { get; }
    public Unit Owner { get; }
    public Unit OtherUnit { get; }
    public CardDefinition Card { get; }
    public string DamageTypeId { get; }
    public int Value { get; set; }
    public bool Cancelled { get; set; }
    public bool Touched { get; set; }
    public ContentRuleQuerySession Session { get; }
}

/// <summary>跟踪每次执行一次、每个目标一次的修正应用，不保存内容特有状态。</summary>
public sealed class ContentRuleQuerySession
{
    private readonly HashSet<string> applied = new HashSet<string>(StringComparer.Ordinal);

    public bool TryApply(string modifierId, string scope, Unit target)
    {
        if (string.IsNullOrWhiteSpace(scope) || scope == "always") return true;
        string key = modifierId;
        if (scope == "once_per_target") key += ":" + (target == null ? "none" : RuntimeHelpers.GetHashCode(target).ToString());
        else if (scope != "once_per_execution") return false;
        return applied.Add(key);
    }
}

/// <summary>按确定性优先级，把规则查询分发给当前全部有效的策划归属。</summary>
public static class ContentRuleQueryRuntime
{
    public static ContentRuleQuery Evaluate(ContentRuleQuery query)
    {
        if (query?.Owner == null || !ContentRuntime.IsLoaded ||
            !ContentRuleQueryKeys.All.Contains(query.QueryKey)) return query;

        List<(ContentBehaviorOwner Owner, IEnumerable<BehaviorDefinition> Behaviors)> sources =
            new List<(ContentBehaviorOwner, IEnumerable<BehaviorDefinition>)>();
        foreach (RuntimeStatusInstance instance in query.Owner.State.GetStatusSnapshot())
        {
            if (instance.Definition == null || instance.Stacks <= 0) continue;
            sources.Add((ContentBehaviorOwner.FromStatus(instance.Definition, instance), instance.Definition.Behaviors));
        }

        if (query.Owner.Faction == UnitFaction.Player &&
            ContentClassPassiveRuntime.TryGetSelectedProfile(out ClassProfileDefinition profile))
            sources.Add((ContentBehaviorOwner.FromClass(profile), profile.Behaviors));

        if (query.Owner.Definition != null)
            sources.Add((ContentBehaviorOwner.FromUnit(query.Owner.Definition, query.Owner),
                query.Owner.Definition.Behaviors));

        RuntimeEquipmentLoadout loadout = query.Owner.GetComponent<RuntimeEquipmentLoadout>();
        if (loadout != null)
            foreach (EquipmentInstance item in loadout.GetSnapshot())
                sources.Add((ContentBehaviorOwner.FromEquipment(item), item.Definition.Behaviors));
        foreach ((ContentBehaviorOwner owner, BehaviorDefinition behavior) in sources
                     .SelectMany(source => source.Behaviors
                         .Where(item => item.Enabled && item.TriggerKey == query.QueryKey)
                         .Select(item => (source.Owner, item)))
                     .OrderBy(item => item.item.Priority)
                     .ThenBy(item => item.Owner.OwnerKind, StringComparer.Ordinal)
                     .ThenBy(item => item.Owner.OwnerId, StringComparer.Ordinal)
                     .ThenBy(item => item.item.BehaviorId, StringComparer.Ordinal)
                     .Select(item => (item.Owner, item.item)))
        {
            Execute(owner, new[] { behavior }, query);
            if (query.Cancelled) break;
        }
        return query;
    }

    private static void Execute(
        ContentBehaviorOwner owner,
        IEnumerable<BehaviorDefinition> behaviors,
        ContentRuleQuery query)
    {
        if (!behaviors.Any(item => item.Enabled && item.TriggerKey == query.QueryKey)) return;
        CardPlayResult result = new CardPlayResult { Success = true };
        ContentCardExecutionContext context = new ContentCardExecutionContext(
            owner, query.Owner, query.OtherUnit, null, null, null, null, result, false,
            ruleQuery: query);
        ContentCardEffectExecutor.TryExecuteOwnedTrigger(context, behaviors, query.QueryKey);
    }
}
}
