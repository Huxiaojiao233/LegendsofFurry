using System;
using System.Collections.Generic;

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>
/// 描述 GenerateCard、MoveCards 和 RemoveCardsByQuery 共用的受控卡牌查询。
/// </summary>
[Serializable]
public sealed class ContentCardQuery
{
    public string cardId;
    public string poolId;
    public string familyId;
    public string rarityId;
    public string tag;
}

/// <summary>
/// 向通用效果执行器提供牌区写入和需要玩家选择的牌堆交互能力。
/// </summary>
public interface IContentCardZoneService : ICardDrawService
{
    /// <summary>
    /// 从运行时注册表按查询生成卡牌实例并放入指定牌区。
    /// </summary>
    /// <param name="query">卡牌查询。</param>
    /// <param name="count">生成数量。</param>
    /// <param name="destinationZone">hand、draw、discard 或 exhaust。</param>
    /// <returns>全部实例成功加入时返回 true。</returns>
    bool GenerateCards(ContentCardQuery query, int count, string destinationZone);

    /// <summary>
    /// 将匹配卡牌从一个牌区移动到另一个牌区。
    /// </summary>
    /// <param name="query">卡牌查询。</param>
    /// <param name="count">最多移动数量；零表示全部。</param>
    /// <param name="sourceZone">来源牌区。</param>
    /// <param name="destinationZone">目标牌区。</param>
    /// <returns>实际移动数量。</returns>
    int MoveCards(ContentCardQuery query, int count, string sourceZone, string destinationZone);

    /// <summary>
    /// 从指定或全部牌区移除匹配卡牌。
    /// </summary>
    /// <param name="query">卡牌查询。</param>
    /// <param name="zone">hand、draw、discard、exhaust 或 all。</param>
    /// <returns>实际移除数量。</returns>
    int RemoveCards(ContentCardQuery query, string zone);

    /// <summary>
    /// 打开“查看牌顶并选择弃置”的异步交互。
    /// </summary>
    /// <param name="count">展示牌顶数量。</param>
    void RevealTopCardsAndChooseDiscard(int count, Action onComplete);

    /// <summary>
    /// 将指定数量牌顶卡加入免费打出队列。
    /// </summary>
    /// <param name="count">处理牌顶数量。</param>
    void PlayTopCardsForFree(int count, Action onComplete);

    /// <summary>
    /// 把一张已生成的卡加入手牌；手牌已满时该张进入弃牌堆。
    /// </summary>
    bool AddCardToHandOrDiscard(CardInstance instance);
}


/// <summary>让通用卡牌效果可以把生成牌交给目标单位，而不依赖玩家或 AI 的具体牌区实现。</summary>
public static class CombatCardZoneRegistry
{
    private static readonly Dictionary<Unit, IContentCardZoneService> Services = new Dictionary<Unit, IContentCardZoneService>();
    public static void Register(Unit owner, IContentCardZoneService service)
    {
        if (owner != null && service != null) Services[owner] = service;
    }
    public static void Unregister(Unit owner, IContentCardZoneService service)
    {
        if (owner != null && Services.TryGetValue(owner, out IContentCardZoneService current) && current == service)
            Services.Remove(owner);
    }
    public static bool TryGet(Unit owner, out IContentCardZoneService service)
    {
        service = null;
        return owner != null && Services.TryGetValue(owner, out service);
    }
}
}
