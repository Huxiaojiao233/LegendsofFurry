using System;
using System.Collections.Generic;
using System.Linq;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;
using UnityEngine;

/// <summary>
/// 根据数据库职业配方和稀有度权重创建每场战斗的独立卡牌实例。
/// </summary>
public static class StartingDeckBuilder
{
    /// <summary>
    /// 按数据库职业卡池配方、固定卡条目和排序构建初始牌库。
    /// </summary>
    /// <param name="profile">当前职业的数据库定义。</param>
    /// <param name="registry">已加载且通过能力校验的内容注册表。</param>
    /// <returns>互相独立、全部持有数据库定义的卡牌实例。</returns>
    public static List<CardInstance> Build(ClassProfileDefinition profile, ContentRegistry registry)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        if (registry == null) throw new ArgumentNullException(nameof(registry));

        List<CardInstance> deck = new List<CardInstance>();
        foreach (DeckRecipePoolDefinition recipe in profile.DeckRecipe.OrderBy(item => item.SortOrder))
        {
            if (!string.IsNullOrWhiteSpace(recipe.FixedCardId))
            {
                CardDefinition fixedCard = registry.GetCard(recipe.FixedCardId);
                for (int index = 0; index < Mathf.Max(1, recipe.Amount); index++)
                {
                    deck.Add(new CardInstance(fixedCard));
                }
                continue;
            }

            List<CardDefinition> candidates = registry.GetCardsInPool(recipe.PoolId)
                .Where(card => card.Enabled && card.RarityId != "red")
                .ToList();
            for (int index = 0; index < recipe.Amount && candidates.Count > 0; index++)
            {
                deck.Add(new CardInstance(RollContentCard(candidates, registry.Package.Rarities)));
            }
        }
        return deck;
    }

    /// <summary>
    /// 使用数据库稀有度权重抽取一张候选卡；相同随机值下按卡牌 ID 保持稳定顺序。
    /// </summary>
    /// <param name="candidates">同一配方条目允许抽取的卡牌。</param>
    /// <param name="rarities">数据库维护的稀有度默认权重。</param>
    /// <returns>本次随机抽中的卡牌定义。</returns>
    private static CardDefinition RollContentCard(
        IReadOnlyList<CardDefinition> candidates,
        IReadOnlyList<RarityDefinition> rarities)
    {
        Dictionary<string, decimal> weights = rarities.ToDictionary(
            item => item.RarityId, item => item.DefaultWeight, StringComparer.Ordinal);
        float total = candidates.Sum(card => (float)(weights.TryGetValue(card.RarityId, out decimal weight) ? weight : 1m));
        float roll = UnityEngine.Random.value * Mathf.Max(0.0001f, total);
        foreach (CardDefinition card in candidates.OrderBy(item => item.CardId, StringComparer.Ordinal))
        {
            roll -= (float)(weights.TryGetValue(card.RarityId, out decimal weight) ? weight : 1m);
            if (roll <= 0f) return card;
        }
        return candidates[candidates.Count - 1];
    }
}
