/// <summary>每类卡牌效果都实现此接口，彼此保持独立。</summary>
public interface ICardEffectHandler
{
    bool CanExecute(CardData card, CardEffectContext context);
    void Execute(CardData card, CardEffectContext context);
}
