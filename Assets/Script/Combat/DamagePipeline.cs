using System;

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

    public Unit Source { get; }
    public Unit Target { get; }
    public int Amount { get; set; }
    public DamageType DamageType { get; set; }
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
