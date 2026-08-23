/// <summary>
/// 卡牌效果执行时需要访问的战斗对象集合。
/// 所有效果处理器共享同一个上下文，避免各自反复查找场景对象。
/// </summary>
public sealed class CardEffectContext
{
    public BoardClickController ActionPoints { get; }
    public Unit Player { get; }
    public Unit Enemy { get; }
    public Unit Target { get; }

    public CardEffectContext(
        BoardClickController actionPoints,
        Unit player,
        Unit enemy,
        Unit target)
    {
        ActionPoints = actionPoints;
        Player = player;
        Enemy = enemy;
        Target = target;
    }
}
