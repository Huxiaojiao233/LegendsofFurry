using System;
using System.Collections.Generic;
using System.Linq;
using LegendsOfFurry.Content.Contracts;

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>
/// 将已发布固定牌库展开为互相独立的运行时卡牌实例，供手牌系统和测试共同使用。
/// </summary>
public static class ContentDeckFactory
{
    /// <summary>
    /// 按牌库条目顺序和数量创建卡牌实例；不存在的牌库或卡牌由注册表抛出可定位异常。
    /// </summary>
    /// <param name="registry">当前已加载的只读内容注册表。</param>
    /// <param name="deckId">需要展开的固定牌库 ID。</param>
    /// <returns>按照 SortOrder 和原始条目顺序排列的运行时卡牌实例。</returns>
    public static IReadOnlyList<CardInstance> CreateInstances(ContentRegistry registry, string deckId)
    {
        if (registry == null)
        {
            throw new ArgumentNullException(nameof(registry));
        }

        DeckDefinition deck = registry.GetDeck(deckId);
        List<CardInstance> instances = new List<CardInstance>();
        foreach (DeckEntryDefinition entry in deck.Entries.OrderBy(item => item.SortOrder))
        {
            CardDefinition definition = registry.GetCard(entry.CardId);
            for (int index = 0; index < Math.Max(1, entry.Amount); index++)
            {
                instances.Add(new CardInstance(definition));
            }
        }
        return instances;
    }
}
}
