using System;
using System.Collections.Generic;
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

[DisallowMultipleComponent]
public class CombatantState : MonoBehaviour
{
    private readonly Dictionary<CombatStatus, int> layers = new Dictionary<CombatStatus, int>();
    private readonly Dictionary<CombatStatus, int> turns = new Dictionary<CombatStatus, int>();
    private int pendingManaExhaustion;

    public int Mana { get; private set; }
    public int MaxMana { get; private set; } = 10;
    public bool PriestHealUsedThisTurn { get; set; }
    public bool ReviveAvailable { get; set; }
    public float NormalDamageAvoidChance { get; set; }

    public event Action Changed;

    public int Get(CombatStatus status) => layers.TryGetValue(status, out int value) ? value : 0;
    public bool Has(CombatStatus status) => Get(status) > 0;

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

    public void Add(CombatStatus status, int amount = 1, int durationTurns = 0)
    {
        if (amount <= 0) return;
        int value = Get(status) + amount;
        if (status == CombatStatus.Cold) value = Mathf.Min(3, value);
        if (status == CombatStatus.Broken) value = Mathf.Min(10, value);
        if (status == CombatStatus.Exhaustion) value = Mathf.Min(3, value);
        layers[status] = value;
        if (durationTurns > 0)
            turns[status] = Mathf.Max(turns.TryGetValue(status, out int current) ? current : 0, durationTurns);

        if (status == CombatStatus.Cold && value >= 3 && !Has(CombatStatus.Warming))
        {
            Remove(CombatStatus.Cold);
            layers[CombatStatus.Frozen] = 1;
        }
        Changed?.Invoke();
    }

    public void Set(CombatStatus status, int amount, int durationTurns = 0)
    {
        Remove(status);
        Add(status, amount, durationTurns);
    }

    public void Remove(CombatStatus status)
    {
        bool changed = layers.Remove(status);
        turns.Remove(status);
        if (changed) Changed?.Invoke();
    }

    public void Reduce(CombatStatus status, int amount = 1)
    {
        int value = Get(status) - Mathf.Max(1, amount);
        if (value <= 0) Remove(status);
        else
        {
            layers[status] = value;
            Changed?.Invoke();
        }
    }

    public void ClearAll()
    {
        layers.Clear();
        turns.Clear();
        Changed?.Invoke();
    }

    public void ClearNegative()
    {
        foreach (CombatStatus status in new[] { CombatStatus.Cold, CombatStatus.Frozen,
            CombatStatus.Haze, CombatStatus.Broken, CombatStatus.Poison,
            CombatStatus.Exhaustion, CombatStatus.ManaExhaustion,
            CombatStatus.Corruption, CombatStatus.Vulnerable, CombatStatus.CannotAttack })
        {
            layers.Remove(status);
            turns.Remove(status);
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
    }

    public void EndTurn(Unit owner)
    {
        bool resolveRaisedShield = Has(CombatStatus.RaiseShieldPending);
        if (resolveRaisedShield) Remove(CombatStatus.RaiseShieldPending);

        int poison = Get(CombatStatus.Poison);
        if (poison > 0)
        {
            owner.TakeTypedDamage(poison, DamageType.Poison);
            Reduce(CombatStatus.Poison, 2);
        }

        int regeneration = Get(CombatStatus.Regeneration);
        if (regeneration > 0)
        {
            owner.Heal(regeneration);
            Reduce(CombatStatus.Regeneration);
        }

        int corruption = Get(CombatStatus.Corruption);
        if (corruption > 0) owner.TakeTypedDamage(corruption, DamageType.Dark);
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
        foreach (KeyValuePair<CombatStatus, int> pair in layers)
            if (pair.Value > 0) parts.Add($"{StatusName(pair.Key)}×{pair.Value}");
        return parts.Count == 0 ? "暂无" : string.Join("  ", parts);
    }

    private void TickDuration(CombatStatus status)
    {
        if (!turns.TryGetValue(status, out int remaining)) return;
        remaining--;
        if (remaining <= 0) Remove(status);
        else turns[status] = remaining;
    }

    private static string StatusName(CombatStatus status)
    {
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
}
