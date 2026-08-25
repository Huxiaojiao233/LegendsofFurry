using System;
using System.Collections.Generic;
using System.Linq;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;
using UnityEngine;

public enum CombatStatus
{
    Sharp,
    Quick,
    BlockRetention,
    NormalImmunity,
    DodgeNextNormal,
    Cold,
    Frozen,
    Warming,
    Haze,
    Broken,
    Poison,
    Exhaustion,
    ManaExhaustion,
    HeartFire,
    Regeneration,
    Corruption,
    Vulnerable,
    CannotAttack,
    RaiseShieldPending,
    SwordCharge
}

/// <summary>保存一个字符串状态 ID 的层数、剩余回合、来源和实例扩展数据。</summary>
public sealed class RuntimeStatusInstance
{
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
    private int pendingManaExhaustion;

    public int Mana { get; private set; }
    public int MaxMana { get; private set; } = 10;
    public bool PriestHealUsedThisTurn { get; set; }
    public bool ReviveAvailable { get; set; }
    public float NormalDamageAvoidChance { get; set; }

    public event Action Changed;

    /// <summary>取得字符串状态 ID 的当前层数。</summary>
    public int Get(string statusId) => statuses.TryGetValue(NormalizeStatusId(statusId), out RuntimeStatusInstance value) ? value.Stacks : 0;

    /// <summary>判断字符串状态 ID 是否存在有效层数。</summary>
    public bool Has(string statusId) => Get(statusId) > 0;

    /// <summary>取得兼容枚举状态的当前层数。</summary>
    public int Get(CombatStatus status) => Get(ToStatusId(status));

    /// <summary>判断兼容枚举状态是否存在。</summary>
    public bool Has(CombatStatus status) => Has(ToStatusId(status));

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
        if (amount <= 0 || Has(CombatStatus.ManaExhaustion)) return false;
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
        if (!statuses.TryGetValue(statusId, out RuntimeStatusInstance instance))
        {
            instance = new RuntimeStatusInstance { StatusId = statusId };
            statuses.Add(statusId, instance);
        }
        int value = ApplyStackingPolicy(statusId, instance.Stacks, amount);
        instance.Stacks = ApplyStackLimit(statusId, value);
        if (durationTurns > 0) instance.RemainingTurns = Mathf.Max(instance.RemainingTurns, durationTurns);
        if (!string.IsNullOrWhiteSpace(sourceId)) instance.SourceId = sourceId;

        if (statusId == ToStatusId(CombatStatus.Cold) && instance.Stacks >= 3 && !Has(CombatStatus.Warming))
        {
            Remove(statusId);
            Add(CombatStatus.Frozen);
        }
        Changed?.Invoke();
    }

    /// <summary>以兼容枚举添加状态。</summary>
    public void Add(CombatStatus status, int amount = 1, int durationTurns = 0) =>
        Add(ToStatusId(status), amount, durationTurns);

    /// <summary>把字符串状态设置为精确层数，并更新持续时间和来源。</summary>
    public void Set(string statusId, int amount, int durationTurns = 0, string sourceId = null)
    {
        Remove(statusId);
        Add(statusId, amount, durationTurns, sourceId);
    }

    /// <summary>以兼容枚举设置状态。</summary>
    public void Set(CombatStatus status, int amount, int durationTurns = 0) => Set(ToStatusId(status), amount, durationTurns);

    /// <summary>移除字符串状态及其实例数据。</summary>
    public void Remove(string statusId)
    {
        bool changed = statuses.Remove(NormalizeStatusId(statusId));
        if (changed) Changed?.Invoke();
    }

    /// <summary>移除兼容枚举状态。</summary>
    public void Remove(CombatStatus status) => Remove(ToStatusId(status));

    /// <summary>减少字符串状态层数，耗尽时移除实例。</summary>
    public void Reduce(string statusId, int amount = 1)
    {
        statusId = NormalizeStatusId(statusId);
        int value = Get(statusId) - Mathf.Max(1, amount);
        if (value <= 0) Remove(statusId);
        else
        {
            statuses[statusId].Stacks = value;
            Changed?.Invoke();
        }
    }

    /// <summary>减少兼容枚举状态层数。</summary>
    public void Reduce(CombatStatus status, int amount = 1) => Reduce(ToStatusId(status), amount);

    public void ClearAll()
    {
        statuses.Clear();
        Changed?.Invoke();
    }

    public void ClearNegative()
    {
        foreach (string statusId in statuses.Keys.ToArray())
        {
            if (GetCategory(statusId) == "negative") statuses.Remove(statusId);
        }
        Changed?.Invoke();
    }

    public void ScheduleManaExhaustion(int turnCount)
    {
        pendingManaExhaustion = Mathf.Max(pendingManaExhaustion, turnCount);
    }

    public bool ConsumeFrozenForTurn()
    {
        if (!Has(CombatStatus.Frozen)) return false;
        Remove(CombatStatus.Frozen);
        Add(CombatStatus.Warming, 1, 1);
        return true;
    }

    public int EffectiveActionPointMaximum(int baseMaximum)
    {
        return Mathf.Max(0, baseMaximum - Get(CombatStatus.Exhaustion));
    }

    public void BeginTurn(Unit owner)
    {
        PriestHealUsedThisTurn = false;
        if (Has(CombatStatus.HeartFire)) owner.AddArmor(5);
        if (Has(CombatStatus.BlockRetention)) Reduce(CombatStatus.BlockRetention);
        else owner.ClearArmor();
        if (Has(CombatStatus.NormalImmunity)) Remove(CombatStatus.NormalImmunity);
        ContentStatusBehaviorRuntime.Execute(owner, "on_unit_turn_start");
    }

    public void EndTurn(Unit owner)
    {
        bool resolveRaisedShield = Has(CombatStatus.RaiseShieldPending);
        if (resolveRaisedShield) Remove(CombatStatus.RaiseShieldPending);

        bool usedContentBehaviors = ContentStatusBehaviorRuntime.Execute(owner, "on_unit_turn_end");
        int poison = Get(CombatStatus.Poison);
        if (!usedContentBehaviors && poison > 0)
        {
            owner.TakeTypedDamage(poison, DamageType.Poison);
            Reduce(CombatStatus.Poison, 2);
        }

        int regeneration = Get(CombatStatus.Regeneration);
        if (!usedContentBehaviors && regeneration > 0)
        {
            owner.Heal(regeneration);
            Reduce(CombatStatus.Regeneration);
        }

        int corruption = Get(CombatStatus.Corruption);
        if (!usedContentBehaviors && corruption > 0) owner.TakeTypedDamage(corruption, DamageType.Dark);
        if (Has(CombatStatus.Cold)) Reduce(CombatStatus.Cold);
        if (Has(CombatStatus.Broken)) Reduce(CombatStatus.Broken);

        TickDuration(CombatStatus.Haze);
        TickDuration(CombatStatus.HeartFire);
        TickDuration(CombatStatus.Vulnerable);
        TickDuration(CombatStatus.CannotAttack);
        TickDuration(CombatStatus.Warming);
        TickDuration(CombatStatus.Exhaustion);
        TickDuration(CombatStatus.ManaExhaustion);

        if (resolveRaisedShield)
        {
            owner.AddArmor(owner.Armor / 2);
            Add(CombatStatus.CannotAttack, 1, 1);
        }

        if (pendingManaExhaustion > 0)
        {
            Set(CombatStatus.ManaExhaustion, 1, pendingManaExhaustion);
            pendingManaExhaustion = 0;
        }
    }

    public string GetSummary()
    {
        List<string> parts = new List<string>();
        foreach (RuntimeStatusInstance instance in statuses.Values.OrderBy(item => item.StatusId, StringComparer.Ordinal))
            if (instance.Stacks > 0) parts.Add($"{StatusName(instance.StatusId)}×{instance.Stacks}");
        return parts.Count == 0 ? "暂无" : string.Join("  ", parts);
    }

    private void TickDuration(CombatStatus status) => TickDuration(ToStatusId(status));

    /// <summary>推进一个字符串状态的持续时间并在到期时移除。</summary>
    private void TickDuration(string statusId)
    {
        statusId = NormalizeStatusId(statusId);
        if (!statuses.TryGetValue(statusId, out RuntimeStatusInstance instance) || instance.RemainingTurns <= 0) return;
        instance.RemainingTurns--;
        if (instance.RemainingTurns <= 0) Remove(statusId);
    }

    /// <summary>读取内容定义显示名，并为迁移期内置状态提供兼容名称。</summary>
    private static string StatusName(string statusId)
    {
        if (ContentRuntime.IsLoaded && ContentRuntime.Registry.TryGetStatus(statusId, out StatusDefinition definition))
            return definition.DisplayName;
        if (!TryParseLegacyStatus(statusId, out CombatStatus status)) return statusId;
        return status switch
        {
            CombatStatus.Sharp => "锋利", CombatStatus.Quick => "速攻",
            CombatStatus.BlockRetention => "格挡", CombatStatus.NormalImmunity => "普通免疫",
            CombatStatus.DodgeNextNormal => "闪避", CombatStatus.Cold => "寒冷",
            CombatStatus.Frozen => "冻结", CombatStatus.Warming => "回暖",
            CombatStatus.Haze => "恍惚", CombatStatus.Broken => "破损",
            CombatStatus.Poison => "中毒", CombatStatus.Exhaustion => "力竭",
            CombatStatus.ManaExhaustion => "魔力枯竭", CombatStatus.HeartFire => "心火",
            CombatStatus.Regeneration => "再生", CombatStatus.Corruption => "腐化",
            CombatStatus.Vulnerable => "易损", CombatStatus.CannotAttack => "禁攻",
            CombatStatus.SwordCharge => "蓄力", _ => status.ToString()
        };
    }

    /// <summary>根据状态定义的叠层策略计算新层数。</summary>
    private static int ApplyStackingPolicy(string statusId, int current, int added)
    {
        if (ContentRuntime.IsLoaded && ContentRuntime.Registry.TryGetStatus(statusId, out StatusDefinition definition))
        {
            return definition.StackingPolicy switch
            {
                "replace" => added,
                "maximum" => Mathf.Max(current, added),
                _ => current + added
            };
        }
        return current + added;
    }

    /// <summary>根据数据库上限或旧规则限制状态层数。</summary>
    private static int ApplyStackLimit(string statusId, int value)
    {
        int maximum = 0;
        if (ContentRuntime.IsLoaded && ContentRuntime.Registry.TryGetStatus(statusId, out StatusDefinition definition))
            maximum = definition.MaximumStacks;
        else if (TryParseLegacyStatus(statusId, out CombatStatus legacy))
            maximum = legacy == CombatStatus.Cold ? 3 : legacy == CombatStatus.Broken ? 10 : legacy == CombatStatus.Exhaustion ? 3 : 0;
        return maximum > 0 ? Mathf.Min(maximum, value) : value;
    }

    /// <summary>读取数据库正负面分类，并为尚未发布定义的旧状态提供兼容分类。</summary>
    private static string GetCategory(string statusId)
    {
        if (ContentRuntime.IsLoaded && ContentRuntime.Registry.TryGetStatus(statusId, out StatusDefinition definition))
            return definition.Category;
        return TryParseLegacyStatus(statusId, out CombatStatus legacy) && new[] {
            CombatStatus.Cold, CombatStatus.Frozen, CombatStatus.Haze, CombatStatus.Broken,
            CombatStatus.Poison, CombatStatus.Exhaustion, CombatStatus.ManaExhaustion,
            CombatStatus.Corruption, CombatStatus.Vulnerable, CombatStatus.CannotAttack }.Contains(legacy)
            ? "negative" : "positive";
    }

    /// <summary>把兼容枚举稳定转换为数据库 snake_case ID。</summary>
    public static string ToStatusId(CombatStatus status)
    {
        string name = status.ToString();
        return string.Concat(name.Select((character, index) =>
            char.IsUpper(character) && index > 0 ? "_" + char.ToLowerInvariant(character) : char.ToLowerInvariant(character).ToString()));
    }

    /// <summary>规范化外部状态 ID，使旧 PascalCase 和新 snake_case 指向同一实例。</summary>
    private static string NormalizeStatusId(string statusId)
    {
        if (string.IsNullOrWhiteSpace(statusId)) return string.Empty;
        return Enum.TryParse(statusId, true, out CombatStatus legacy) ? ToStatusId(legacy) : statusId.Trim().ToLowerInvariant();
    }

    /// <summary>尝试把 snake_case 或旧枚举名还原为迁移期状态枚举。</summary>
    private static bool TryParseLegacyStatus(string statusId, out CombatStatus status)
    {
        string compact = (statusId ?? string.Empty).Replace("_", string.Empty);
        foreach (CombatStatus candidate in Enum.GetValues(typeof(CombatStatus)))
        {
            if (!candidate.ToString().Equals(compact, StringComparison.OrdinalIgnoreCase)) continue;
            status = candidate;
            return true;
        }
        return Enum.TryParse(statusId, true, out status);
    }
}
