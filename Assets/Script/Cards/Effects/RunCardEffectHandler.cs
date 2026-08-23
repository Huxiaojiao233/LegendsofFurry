/// <summary>让下一次棋盘移动减少指定行动点成本。</summary>
public sealed class RunCardEffectHandler : ICardEffectHandler
{
    public bool CanExecute(CardData card, CardEffectContext context)
        => context.ActionPoints != null;

    public void Execute(CardData card, CardEffectContext context)
        => context.ActionPoints.GrantNextMoveDiscount(card.effectValue);
}
