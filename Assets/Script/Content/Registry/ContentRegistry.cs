using System;
using System.Collections.Generic;
using System.Linq;
using LegendsOfFurry.Content.Contracts;

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>
/// 为已发布内容建立只读 ID 索引，并只向游戏暴露启用的卡牌、状态和牌库。
/// </summary>
public sealed class ContentRegistry
{
    private readonly Dictionary<string, CardDefinition> cards;
    private readonly Dictionary<string, StatusDefinition> statuses;
    private readonly Dictionary<string, DeckDefinition> decks;
    private readonly Dictionary<string, ClassProfileDefinition> classProfiles;

    /// <summary>
    /// 从经过校验的内容包构建不可变索引；重复 ID 会立即抛出异常而不是覆盖。
    /// </summary>
    /// <param name="package">加载器返回的完整内容包。</param>
    public ContentRegistry(ContentPackage package)
    {
        if (package == null)
        {
            throw new ArgumentNullException(nameof(package));
        }

        Package = package;
        cards = package.Cards.Where(item => item.Enabled).ToDictionary(item => item.CardId, StringComparer.Ordinal);
        statuses = package.Statuses.Where(item => item.Enabled).ToDictionary(item => item.StatusId, StringComparer.Ordinal);
        decks = package.Decks.Where(item => item.Enabled).ToDictionary(item => item.DeckId, StringComparer.Ordinal);
        classProfiles = package.ClassProfiles.Where(item => item.Enabled).ToDictionary(item => item.ClassId, StringComparer.Ordinal);
    }

    public ContentPackage Package { get; }
    public IReadOnlyCollection<CardDefinition> Cards => cards.Values;
    public IReadOnlyCollection<StatusDefinition> Statuses => statuses.Values;
    public IReadOnlyCollection<DeckDefinition> Decks => decks.Values;
    public IReadOnlyCollection<ClassProfileDefinition> ClassProfiles => classProfiles.Values;
    public GameSettingsDefinition GameSettings => Package.GameSettings;

    /// <summary>
    /// 查找一张启用卡牌，并在 ID 不存在时返回 false。
    /// </summary>
    /// <param name="cardId">稳定卡牌 ID。</param>
    /// <param name="card">成功时返回卡牌定义。</param>
    /// <returns>找到启用卡牌时返回 true。</returns>
    public bool TryGetCard(string cardId, out CardDefinition card)
    {
        return cards.TryGetValue(cardId, out card);
    }

    /// <summary>
    /// 获取一张启用卡牌；缺失时抛出包含 ID 的异常，供开发日志定位坏引用。
    /// </summary>
    /// <param name="cardId">稳定卡牌 ID。</param>
    /// <returns>对应卡牌定义。</returns>
    public CardDefinition GetCard(string cardId)
    {
        if (!cards.TryGetValue(cardId, out CardDefinition card))
        {
            throw new KeyNotFoundException($"内容包中不存在启用卡牌：{cardId}");
        }
        return card;
    }

    /// <summary>
    /// 按卡池稳定 ID 返回全部启用卡牌，并保持发布包中的确定性顺序。
    /// </summary>
    /// <param name="poolId">需要查询的卡池 ID。</param>
    /// <returns>属于指定卡池的只读卡牌快照。</returns>
    public IReadOnlyList<CardDefinition> GetCardsInPool(string poolId)
    {
        return Package.Cards
            .Where(card => card.Enabled && card.Pools.Any(pool => pool.PoolId == poolId))
            .ToArray();
    }

    /// <summary>
    /// 按标签返回全部启用卡牌，并使用序号比较避免区域设置影响内容查询。
    /// </summary>
    /// <param name="tag">需要匹配的稳定标签。</param>
    /// <returns>带有指定标签的只读卡牌快照。</returns>
    public IReadOnlyList<CardDefinition> GetCardsWithTag(string tag)
    {
        return Package.Cards
            .Where(card => card.Enabled && card.Tags.Contains(tag, StringComparer.Ordinal))
            .ToArray();
    }

    /// <summary>
    /// 查找一个启用状态定义，并在 ID 不存在时返回 false。
    /// </summary>
    /// <param name="statusId">稳定状态 ID。</param>
    /// <param name="status">成功时返回状态定义。</param>
    /// <returns>找到启用状态时返回 true。</returns>
    public bool TryGetStatus(string statusId, out StatusDefinition status)
    {
        return statuses.TryGetValue(statusId, out status);
    }

    /// <summary>
    /// 获取一套启用固定牌库；缺失时抛出包含 ID 的异常。
    /// </summary>
    /// <param name="deckId">稳定牌库 ID。</param>
    /// <returns>对应固定牌库定义。</returns>
    public DeckDefinition GetDeck(string deckId)
    {
        if (!decks.TryGetValue(deckId, out DeckDefinition deck))
        {
            throw new KeyNotFoundException($"内容包中不存在启用牌库：{deckId}");
        }
        return deck;
    }

    /// <summary>查找启用职业资料。</summary>
    public bool TryGetClassProfile(string classId, out ClassProfileDefinition profile)
    {
        return classProfiles.TryGetValue(classId, out profile);
    }

    /// <summary>按稳定 ID 查找启用卡池，供装备栏等内容驱动界面显示策划名称。</summary>
    public bool TryGetCardPool(string poolId, out CardPoolDefinition pool)
    {
        pool = Package.CardPools.FirstOrDefault(item => item.Enabled && item.PoolId == poolId);
        return pool != null;
    }
}
}
