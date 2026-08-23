/// <summary>恢复玩家生命。</summary>
public sealed class HealCardEffectHandler : ICardEffectHandler
{
    public bool CanExecute(CardData card, CardEffectContext context)
        => context.Player != null && context.Player.IsAlive;

    public void Execute(CardData card, CardEffectContext context)
        => context.Player.Heal(card.effectValue);
}
