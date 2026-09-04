namespace LegendsOfFurry.Content.Runtime
{
/// <summary>玩家输入与自动决策共用的行动点最小接口。</summary>
public interface IActionPointPool
{
    int CurrentActionPoints { get; }
    int MaxActionPoints { get; }
    bool TrySpendActionPoints(int amount);
    void GainActionPoints(int amount);
    void IncreaseMaximumActionPoints(int amount);
}
}
