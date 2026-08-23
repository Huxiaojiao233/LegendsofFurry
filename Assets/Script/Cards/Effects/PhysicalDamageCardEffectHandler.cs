using UnityEngine;

/// <summary>对选中的敌人造成普通物理伤害。</summary>
public sealed class PhysicalDamageCardEffectHandler : ICardEffectHandler
{
    public bool CanExecute(CardData card, CardEffectContext context)
        => IsValidEnemy(context.Target);

    public void Execute(CardData card, CardEffectContext context)
    {
        CombatVfx.PlayClaw(context.Player, context.Target);
        int healthDamage = context.Target.TakeDamage(card.effectValue);
        Debug.Log($"爪击对 {context.Target.name} 造成 {card.effectValue} 点普通伤害，实际损失生命 {healthDamage} 点。");
    }

    private static bool IsValidEnemy(Unit target)
        => target != null && target.IsAlive && target.Faction == UnitFaction.Enemy;
}
