using UnityEngine;

/// <summary>对选中的敌人造成火焰伤害；当前同样会先被护甲吸收。</summary>
public sealed class FireDamageCardEffectHandler : ICardEffectHandler
{
    public bool CanExecute(CardData card, CardEffectContext context)
        => IsValidEnemy(context.Target);

    public void Execute(CardData card, CardEffectContext context)
    {
        Unit target = context.Target;
        int value = card.effectValue;
        CombatVfx.PlayFireball(context.Player, target, () =>
        {
            if (target == null)
            {
                return;
            }

            int healthDamage = target.TakeDamage(value);
            Debug.Log($"火球对 {target.name} 造成 {value} 点火焰伤害，实际损失生命 {healthDamage} 点。");
        });
    }

    private static bool IsValidEnemy(Unit target)
        => target != null && target.IsAlive && target.Faction == UnitFaction.Enemy;
}
