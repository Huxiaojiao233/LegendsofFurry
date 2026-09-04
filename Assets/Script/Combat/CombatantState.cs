using System;
using System.Collections.Generic;
using System.Linq;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;
using UnityEngine;

/// <summary>保存一个字符串状态 ID 的层数、剩余回合、来源和实例扩展数据。</summary>
public sealed class RuntimeStatusInstance : IContentInstance<StatusDefinition>
{
    /// <summary>Creates a runtime status bound to its optional published definition.</summary>
    /// <param name="statusId">The normalized stable status ID.</param>
    /// <param name="definition">The enabled definition resolved by the registry, or null in legacy fallback mode.</param>
    internal RuntimeStatusInstance(string statusId, StatusDefinition definition)
    {
        InstanceId = Guid.NewGuid().ToString("N");
        StatusId = statusId;
        Definition = definition;
    }

    public string InstanceId { get; }
    public StatusDefinition Definition { get; internal set; }
    public string StatusId { get; internal set; }
    public int Stacks { get; internal set; }
    public int RemainingTurns { get; internal set; }
    public string SourceId { get; internal set; }
    public Dictionary<string, int> Values { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
}

[DisallowMultipleComponent]
public class CombatantState : MonoBehaviour
{
    private readonly Dictionary<string, RuntimeStatusInstance> statuses =
        new Dictionary<string, RuntimeStatusInstance>(StringComparer.Ordinal);
    private readonly HashSet<string> usedRuleKeys = new HashSet<string>(StringComparer.Ordinal);

    public int Mana { get; private set; }
    public int MaxMana { get; private set; } = 10;
    public bool ActivatedAbilityUsedThisTurn { get; set; }

    public event Action Changed;

    public bool TryMarkRuleUsed(string ruleKey) => !string.IsNullOrWhiteSpace(ruleKey) && usedRuleKeys.Add(ruleKey);

    /// <summary>取得字符串状态 ID 的当前层数。</summary>
    public int Get(string statusId) => statuses.TryGetValue(NormalizeStatusId(statusId), out RuntimeStatusInstance value) ? value.Stacks : 0;

    /// <summary>判断字符串状态 ID 是否存在有效层数。</summary>
    public bool Has(string statusId) => Get(statusId) > 0;

    /// <summary>返回状态实例的独立快照，供 UI、条件和存档读取。</summary>
    public IReadOnlyList<RuntimeStatusInstance> GetStatusSnapshot() => statuses.Values
        .OrderBy(item => item.StatusId, StringComparer.Ordinal).ToArray();

    public void ConfigureMana(int current, int maximum = 10)
    {
        MaxMana = Mathf.Max(0, maximum);
        Mana = Mathf.Clamp(current, 0, MaxMana);
        Changed?.Invoke();
    }

    public bool TryGainMana(int amount)
    {
        Unit owner = GetComponent<Unit>();
        ContentRuleQuery query = ContentRuleQueryRuntime.Evaluate(new ContentRuleQuery(
            ContentRuleQueryKeys.ManaGain, owner, null, amount));
        if (query.Touched) amount = query.Value;
        if (amount <= 0 || query.Cancelled) return false;
        int before = Mana;
        Mana = Mathf.Min(MaxMana, Mana + amount);
        if (Mana != before) Changed?.Invoke();
        return Mana != before;
    }

    public bool TrySpendMana(int amount)
    {
        if (amount < 0 || amount > Mana) return false;
        Mana -= amount;
        Changed?.Invoke();
        return true;
    }

    public int SpendAllMana()
    {
        int spent = Mana;
        Mana = 0;
        Changed?.Invoke();
        return spent;
    }

    public void Add(string statusId, int amount = 1, int durationTurns = 0, string sourceId = null)
    {
        if (amount <= 0) return;
        statusId = NormalizeStatusId(statusId);
        if (string.IsNullOrEmpty(statusId)) return;
        StatusDefinition definition = null;
        if (ContentRuntime.IsLoaded)
        {
            ContentRuntime.Registry.TryGetStatus(statusId, out definition);
        }
        if (!statuses.TryGetValue(statusId, out RuntimeStatusInstance instance))
        {
            instance = new RuntimeStatusInstance(statusId, definition);
            statuses.Add(statusId, instance);
        }
        else if (instance.Definition == null && definition != null)
        {
            instance.Definition = definition;
        }
        int previousStacks = instance.Stacks;
        int value = ApplyStackingPolicy(instance, previousStacks, amount);
        instance.Stacks = ApplyStackLimit(instance, value);
        if (durationTurns > 0) instance.RemainingTurns = Mathf.Max(instance.RemainingTurns, durationTurns);
        if (!string.IsNullOrWhiteSpace(sourceId)) instance.SourceId = sourceId;

        Changed?.Invoke();
        PublishStatusChanged(statusId, previousStacks, Get(statusId));
        ContentStatusBehaviorRuntime.ExecuteStatus(GetComponent<Unit>(), instance, ContentTriggerKeys.OnStatusChanged);
        if (previousStacks == 0 && instance.Stacks > 0)
        {
            CombatEventBus.Shared.Publish(new StatusGainedEvent(GetComponent<Unit>(), statusId, instance.Stacks));
            ContentStatusBehaviorRuntime.ExecuteStatus(GetComponent<Unit>(), instance, ContentTriggerKeys.OnStatusGained);
            ContentActorBehaviorRuntime.Execute(GetComponent<Unit>(), ContentTriggerKeys.OnStatusGained, null);
        }
    }

    /// <summary>把字符串状态设置为精确层数，并更新持续时间和来源。</summary>
    public void Set(string statusId, int amount, int durationTurns = 0, string sourceId = null)
    {
        Remove(statusId);
        Add(statusId, amount, durationTurns, sourceId);
    }

    /// <summary>移除字符串状态及其实例数据。</summary>
    public void Remove(string statusId)
    {
        statusId = NormalizeStatusId(statusId);
        int previousStacks = Get(statusId);
        bool changed = statuses.Remove(statusId);
        if (changed)
        {
            Changed?.Invoke();
            PublishStatusChanged(statusId, previousStacks, 0);
        }
    }

    /// <summary>减少字符串状态层数，耗尽时移除实例。</summary>
    public void Reduce(string statusId, int amount = 1)
    {
        statusId = NormalizeStatusId(statusId);
        int value = Get(statusId) - Mathf.Max(1, amount);
        if (value <= 0) Remove(statusId);
        else
        {
            int previousStacks = statuses[statusId].Stacks;
            statuses[statusId].Stacks = value;
            Changed?.Invoke();
            PublishStatusChanged(statusId, previousStacks, value);
        }
    }

    public void ClearAll()
    {
        KeyValuePair<string, int>[] removed = statuses.ToDictionary(item => item.Key, item => item.Value.Stacks,
            StringComparer.Ordinal).ToArray();
        statuses.Clear();
        usedRuleKeys.Clear();
        Changed?.Invoke();
        foreach (KeyValuePair<string, int> item in removed) PublishStatusChanged(item.Key, item.Value, 0);
    }

    public void ClearNegative()
    {
        foreach (string statusId in statuses.Keys.ToArray())
        {
            if (GetCategory(statuses[statusId]) == "negative") statuses.Remove(statusId);
        }
        Changed?.Invoke();
    }

    public bool CanTakeTurn(Unit owner)
    {
        ContentRuleQuery query = ContentRuleQueryRuntime.Evaluate(new ContentRuleQuery(
            ContentRuleQueryKeys.CanTakeTurn, owner, null, 1));
        return !query.Cancelled && query.Value > 0;
    }

    public int EffectiveActionPointMaximum(int baseMaximum)
    {
        ContentRuleQuery query = ContentRuleQueryRuntime.Evaluate(new ContentRuleQuery(
            ContentRuleQueryKeys.MaximumActionPoints, GetComponent<Unit>(), null, baseMaximum));
        return Mathf.Max(0, query.Value);
    }

    public void BeginTurn(Unit owner)
    {
        CombatEventBus.Shared.Publish(new UnitTriggerEvent(owner, ContentTriggerKeys.OnUnitTurnStart));
        ActivatedAbilityUsedThisTurn = false;
        ContentStatusBehaviorRuntime.Execute(owner, ContentTriggerKeys.OnUnitTurnStart);
        ContentRuleQuery retention = ContentRuleQueryRuntime.Evaluate(new ContentRuleQuery(
            ContentRuleQueryKeys.ArmorRetention, owner, null, 0));
        if (retention.Value <= 0) owner.ClearArmor();
    }

    public void EndTurn(Unit owner)
    {
        CombatEventBus.Shared.Publish(new UnitTriggerEvent(owner, ContentTriggerKeys.OnUnitTurnEnd));
        ContentStatusBehaviorRuntime.Execute(owner, ContentTriggerKeys.OnUnitTurnEnd);

        foreach (RuntimeStatusInstance instance in GetStatusSnapshot())
            if (instance.Definition?.DurationPolicy == "turns") TickDuration(instance.StatusId);

    }

    public string GetSummary()
    {
        List<string> parts = new List<string>();
        foreach (RuntimeStatusInstance instance in statuses.Values.OrderBy(item => item.StatusId, StringComparer.Ordinal))
            if (instance.Stacks > 0) parts.Add($"{StatusName(instance)}×{instance.Stacks}");
        return parts.Count == 0 ? "暂无" : string.Join("  ", parts);
    }

    /// <summary>推进一个字符串状态的持续时间并在到期时移除。</summary>
    private void TickDuration(string statusId)
    {
        statusId = NormalizeStatusId(statusId);
        if (!statuses.TryGetValue(statusId, out RuntimeStatusInstance instance) || instance.RemainingTurns <= 0) return;
        instance.RemainingTurns--;
        if (instance.RemainingTurns <= 0) Remove(statusId);
    }

    /// <summary>Reads the authored display name and falls back to the stable ID.</summary>
    /// <param name="instance">The runtime status whose definition supplies presentation data.</param>
    /// <returns>The authored display name, legacy localized name, or stable ID.</returns>
    private static string StatusName(RuntimeStatusInstance instance)
    {
        string statusId = instance?.StatusId ?? string.Empty;
        if (instance?.Definition != null)
            return instance.Definition.DisplayName;
        return statusId;
    }

    /// <summary>Calculates new stacks from the definition bound to the runtime status.</summary>
    /// <param name="instance">The runtime status being modified.</param>
    /// <param name="current">The current stack count.</param>
    /// <param name="added">The incoming stack count.</param>
    /// <returns>The stack count before applying the maximum.</returns>
    private static int ApplyStackingPolicy(RuntimeStatusInstance instance, int current, int added)
    {
        if (instance?.Definition != null)
        {
            return instance.Definition.StackingPolicy switch
            {
                "replace" => added,
                "maximum" => Mathf.Max(current, added),
                _ => current + added
            };
        }
        return current + added;
    }

    /// <summary>Applies the bound definition's maximum or the legacy fallback cap.</summary>
    /// <param name="instance">The runtime status being modified.</param>
    /// <param name="value">The uncapped stack count.</param>
    /// <returns>The capped stack count.</returns>
    private static int ApplyStackLimit(RuntimeStatusInstance instance, int value)
    {
        int maximum = instance?.Definition?.MaximumStacks ?? 0;
        return maximum > 0 ? Mathf.Min(maximum, value) : value;
    }

    /// <summary>Reads the bound positive/negative category with a legacy fallback.</summary>
    /// <param name="instance">The runtime status to classify.</param>
    /// <returns>The authored category, or a legacy positive/negative category.</returns>
    private static string GetCategory(RuntimeStatusInstance instance)
    {
        return instance?.Definition?.Category ?? "neutral";
    }

    /// <summary>规范化外部状态 ID，使旧 PascalCase 和新 snake_case 指向同一实例。</summary>
    private static string NormalizeStatusId(string statusId)
    {
        if (string.IsNullOrWhiteSpace(statusId)) return string.Empty;
        return statusId.Trim().ToLowerInvariant();
    }

    /// <summary>Publishes a stable-ID status mutation for content triggers and diagnostics.</summary>
    private void PublishStatusChanged(string statusId, int previousStacks, int currentStacks)
    {
        if (previousStacks == currentStacks) return;
        CombatEventBus.Shared.Publish(new StatusChangedEvent(GetComponent<Unit>(), statusId, previousStacks, currentStacks));
    }
}
