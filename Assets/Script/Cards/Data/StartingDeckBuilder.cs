using System;
using System.Collections.Generic;
using System.Linq;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;
using UnityEngine;

/// <summary>
/// 根据数据库职业配方、已装备物品的从属卡池和稀有度权重创建每场战斗的独立卡牌实例。
/// </summary>
public static class StartingDeckBuilder
{
    private static readonly string[] EquipmentTraitSlots =
    {
        ContentEquipmentSlotKeys.Weapon,
        ContentEquipmentSlotKeys.Offhand,
        ContentEquipmentSlotKeys.Accessory,
        ContentEquipmentSlotKeys.Armor,
        ContentEquipmentSlotKeys.Treasure,
        ContentEquipmentSlotKeys.Boot
    };

    /// <summary>
    /// 先写入职业固定基础牌。主副手把从属牌打入牌库；其余装备每场只放入一张绑定该装备的鉴定牌。
    /// 单手武器抽 3 张从属，未装备副手的武器按双手抽 6 张；配方里的卡池条目在已有装备时跳过，避免和武器从属重复。
    /// </summary>
    /// <param name="profile">当前职业的数据库定义。</param>
    /// <param name="registry">已加载且通过能力校验的内容注册表。</param>
    /// <returns>互相独立、全部持有数据库定义的卡牌实例。</returns>
    public static List<CardInstance> Build(ClassProfileDefinition profile, ContentRegistry registry)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        if (registry == null) throw new ArgumentNullException(nameof(registry));

        List<CardInstance> deck = new List<CardInstance>();
        bool useEquipmentPools = TryCollectEquipped(profile, registry, out List<EquipmentDefinition> equipped);
        foreach (DeckRecipePoolDefinition recipe in profile.DeckRecipe.OrderBy(item => item.SortOrder))
        {
            if (!string.IsNullOrWhiteSpace(recipe.FixedCardId))
            {
                CardDefinition fixedCard = registry.GetCard(recipe.FixedCardId);
                for (int index = 0; index < Mathf.Max(1, recipe.Amount); index++)
                    deck.Add(new CardInstance(fixedCard));
                continue;
            }

            if (useEquipmentPools) continue;
            AppendRolledPoolCards(deck, registry, recipe.PoolId, recipe.Amount);
        }

        if (useEquipmentPools)
        {
            bool hasOffhand = equipped.Any(item => item.SlotKey == ContentEquipmentSlotKeys.Offhand);
            foreach (EquipmentDefinition item in equipped.OrderBy(item => item.SlotKey, StringComparer.Ordinal))
            {
                if (ContentEquipmentSlotKeys.UsesAppraisal(item.SlotKey))
                {
                    AppendAppraisalCard(deck, registry, item);
                    continue;
                }

                int amount = item.SlotKey == ContentEquipmentSlotKeys.Weapon && !hasOffhand ? 6 : 3;
                AppendRolledPoolCards(deck, registry, item.CardPoolId, amount);
            }
        }

        return deck;
    }

    /// <summary>读取职业装备栏特性并解析为已启用的装备定义。</summary>
    private static bool TryCollectEquipped(
        ClassProfileDefinition profile,
        ContentRegistry registry,
        out List<EquipmentDefinition> equipped)
    {
        equipped = new List<EquipmentDefinition>();
        foreach (string slot in EquipmentTraitSlots)
        {
            string equipmentId = profile.GetTraitString($"equipment_{slot}_id",
                profile.GetTraitString($"equipment_{slot}_pool"));
            if (string.IsNullOrWhiteSpace(equipmentId)) continue;
            if (!registry.TryGetEquipment(equipmentId, out EquipmentDefinition definition) || !definition.Enabled)
                continue;
            equipped.Add(definition);
        }
        return equipped.Count > 0;
    }

    /// <summary>为非主副手装备放入一张绑定该装备的鉴定牌。</summary>
    private static void AppendAppraisalCard(
        List<CardInstance> deck,
        ContentRegistry registry,
        EquipmentDefinition equipment)
    {
        if (!registry.TryGetCard(EquipmentAppraisal.CardIdFor(equipment.EquipmentId), out CardDefinition definition) ||
            !definition.Enabled)
            return;
        deck.Add(new CardInstance(definition));
    }

    /// <summary>按稀有度权重从卡池抽取非诅咒、可打出的从属牌。</summary>
    private static void AppendRolledPoolCards(
        List<CardInstance> deck,
        ContentRegistry registry,
        string poolId,
        int amount)
    {
        if (string.IsNullOrWhiteSpace(poolId) || amount <= 0) return;
        List<CardDefinition> candidates = registry.GetCardsInPool(poolId)
            .Where(card => card.Enabled && card.RarityId != "red" && !card.Unplayable)
            .ToList();
        for (int index = 0; index < amount && candidates.Count > 0; index++)
            deck.Add(new CardInstance(RollContentCard(candidates, registry.Package.Rarities)));
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
