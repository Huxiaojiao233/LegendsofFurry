/// <summary>为玩家增加护甲。</summary>
public sealed class BlockCardEffectHandler : ICardEffectHandler
{
    public bool CanExecute(CardData card, CardEffectContext context)
        => context.Player != null && context.Player.IsAlive;

    public void Execute(CardData card, CardEffectContext context)
        => context.Player.AddArmor(card.effectValue);
}
