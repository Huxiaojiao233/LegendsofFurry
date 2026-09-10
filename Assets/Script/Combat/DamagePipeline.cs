using System;

/// <summary>Stable authored IDs for the engine's closed set of damage calculation strategies.</summary>
public static class DamageTypeIds
{
    public const string Normal = "normal";
    public const string Fire = "fire";
    public const string Ice = "ice";
    public const string Grass = "grass";
    public const string Lightning = "lightning";
    public const string Rock = "rock";
    public const string Wind = "wind";
    public const string Water = "water";
    public const string Light = "light";
    public const string Dark = "dark";
    public const string Poison = "poison";
    public const string Direct = "direct";

    /// <summary>Converts a compatibility enum value to its authored stable ID.</summary>
    public static string FromLegacy(DamageType value) => value.ToString().ToLowerInvariant();

    /// <summary>Parses an authored stable ID into the compatibility enum used by calculation code.</summary>
    public static bool TryToLegacy(string value, out DamageType damageType) =>
        Enum.TryParse(value, true, out damageType);
}

/// <summary>保存一次可在结算前由监听器修改或取消的伤害请求。</summary>
public sealed class DamageRequest
{
    /// <summary>创建一次带来源、目标、数值和类型的伤害请求。</summary>
    public DamageRequest(Unit source, Unit target, int amount, DamageType damageType, bool showPresentation)
    {
        Source = source;
        Target = target;
        Amount = Math.Max(0, amount);
        DamageType = damageType;
        ShowPresentation = showPresentation;
    }

    /// <summary>Creates a request from the stable damage type ID authored by content.</summary>
    public DamageRequest(Unit source, Unit target, int amount, string damageTypeId, bool showPresentation)
        : this(source, target, amount,
            DamageTypeIds.TryToLegacy(damageTypeId, out DamageType parsed) ? parsed : DamageType.Normal,
            showPresentation)
    {
    }

    public Unit Source { get; }
    public Unit Target { get; }
    public int Amount { get; set; }
    public DamageType DamageType { get; set; }
    public string DamageTypeId => DamageTypeIds.FromLegacy(DamageType);
    public bool ShowPresentation { get; set; }
    public bool Cancelled { get; set; }
}

/// <summary>保存伤害管线完成后的护甲、生命、复活和击杀结果。</summary>
public sealed class DamageResolution
{
    public DamageRequest Request { get; internal set; }
    public int FinalDamage { get; internal set; }
    public int AbsorbedByArmor { get; internal set; }
    public int HealthDamage { get; internal set; }
    public bool WasDodgedOrImmune { get; internal set; }
    public bool Revived { get; internal set; }
    public bool Killed { get; internal set; }
}

/// <summary>Coordinates the mutable request, legacy callbacks, calculation, and shared combat events.</summary>
public static class DamagePipeline
{
    /// <summary>Runs one damage request through every supported interception and notification boundary.</summary>
    public static DamageResolution Resolve(
        DamageRequest request,
        Action<DamageRequest> beforeDamage,
        Func<DamageRequest, DamageResolution> applyDamage,
        Action<DamageResolution> afterDamage)
    {
        DamageResolution empty = new DamageResolution { Request = request };
        if (request == null || request.Target == null || request.Amount <= 0 || !request.Target.IsAlive)
            return empty;

        CombatEventBus.Shared.Publish(new DamageRequestedEvent(request));
        beforeDamage?.Invoke(request);
        DamageResolution resolution = request.Cancelled || request.Amount <= 0
            ? empty
            : applyDamage(request);
        afterDamage?.Invoke(resolution);
        CombatEventBus.Shared.Publish(new DamageResolvedEvent(resolution));
        return resolution;
    }
}
