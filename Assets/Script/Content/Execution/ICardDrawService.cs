namespace LegendsOfFurry.Content.Runtime
{
/// <summary>
/// 为通用卡牌效果提供最小抽牌能力，避免规则执行器依赖手牌界面实现细节。
/// </summary>
public interface ICardDrawService
{
    /// <summary>
    /// 请求抽取指定数量的卡牌，并声明手牌已满时是否必须继续处理。
    /// </summary>
    /// <param name="count">需要抽取的非负张数。</param>
    /// <param name="mandatory">手牌已满时是否进入强制弃牌流程。</param>
    void DrawCards(int count, bool mandatory);
}
}
