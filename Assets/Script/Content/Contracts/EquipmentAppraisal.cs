#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace LegendsOfFurry.Content.Contracts
{
/// <summary>
/// 装备鉴定：诅咒判定、按现有品质套表加权，再在该品质中无同名抽取。
/// </summary>
public static class EquipmentAppraisal
{
    public const float CurseChance = 0.20f;
    public const string CardIdSuffix = "_appraise";

    /// <summary>每件装备各自一张鉴定牌，稳定 ID 为「装备ID_appraise」。</summary>
    public static string CardIdFor(string equipmentId) =>
        string.IsNullOrWhiteSpace(equipmentId) ? string.Empty : equipmentId + CardIdSuffix;

    /// <summary>从鉴定牌 ID 反推装备 ID。</summary>
    public static bool TryGetEquipmentIdFromCard(string? cardId, out string equipmentId)
    {
        equipmentId = string.Empty;
        if (string.IsNullOrEmpty(cardId) ||
            !cardId.EndsWith(CardIdSuffix, StringComparison.Ordinal) ||
            cardId.Length <= CardIdSuffix.Length)
            return false;
        equipmentId = cardId.Substring(0, cardId.Length - CardIdSuffix.Length);
        return ContentId.IsValid(equipmentId);
    }

    private static readonly string[] RankedRarities = { "gray", "blue", "purple", "gold" };

    private static readonly (string[] Rarities, int[] Weights)[] WeightTables =
    {
        (new[] { "gray", "blue" }, new[] { 60, 40 }),
        (new[] { "gray", "blue", "purple" }, new[] { 55, 30, 15 }),
        (new[] { "gray", "blue", "gold" }, new[] { 60, 35, 5 }),
        (new[] { "gray", "blue", "purple", "gold" }, new[] { 55, 30, 10, 5 }),
    };

    /// <summary>诅咒牌：勾选了诅咒，或稀有度为红。</summary>
    public static bool IsCurseCard(CardDefinition? card) =>
        card != null && card.Enabled && (card.Curse ||
            string.Equals(card.RarityId, "red", StringComparison.Ordinal));

    /// <summary>
    /// 用三个 [0,1) 随机量完成一次鉴定。诅咒池为空时诅咒判定改为抽正常牌。
    /// </summary>
    public static CardDefinition? Roll(
        IReadOnlyList<CardDefinition> poolCards,
        float curseUnit,
        float rarityUnit,
        float cardUnit)
    {
        if (poolCards == null || poolCards.Count == 0) return null;
        List<CardDefinition> curse = UniqueByName(poolCards.Where(IsCurseCard));
        List<CardDefinition> normal = UniqueByName(poolCards.Where(card =>
            card != null && card.Enabled && !card.Unplayable && !IsCurseCard(card)));
        if (curse.Count > 0 && curseUnit < CurseChance)
            return Pick(curse, cardUnit);
        if (normal.Count == 0) return curse.Count > 0 ? Pick(curse, cardUnit) : null;
        string rarity = RollRarity(normal, rarityUnit);
        List<CardDefinition> bucket = normal
            .Where(card => string.Equals(card.RarityId, rarity, StringComparison.Ordinal))
            .OrderBy(card => card.CardId, StringComparer.Ordinal)
            .ToList();
        if (bucket.Count == 0)
            bucket = normal.OrderBy(card => card.CardId, StringComparer.Ordinal).ToList();
        return Pick(bucket, cardUnit);
    }

    /// <summary>同名只保留卡牌 ID 最小的一张，避免鉴定抽出重复名称。</summary>
    public static List<CardDefinition> UniqueByName(IEnumerable<CardDefinition> cards)
    {
        return cards
            .Where(card => card != null)
            .GroupBy(card => string.IsNullOrWhiteSpace(card.DisplayName) ? card.CardId : card.DisplayName,
                StringComparer.Ordinal)
            .Select(group => group.OrderBy(card => card.CardId, StringComparer.Ordinal).First())
            .OrderBy(card => card.CardId, StringComparer.Ordinal)
            .ToList();
    }

    private static string RollRarity(IReadOnlyList<CardDefinition> normal, float rarityUnit)
    {
        HashSet<string> present = new HashSet<string>(
            normal.Select(card => card.RarityId).Where(id => RankedRarities.Contains(id, StringComparer.Ordinal)),
            StringComparer.Ordinal);
        if (present.Count == 0) return normal[0].RarityId;
        (string[] rarities, int[] weights) = SelectTable(present);
        int total = 0;
        for (int index = 0; index < rarities.Length; index++)
        {
            if (present.Contains(rarities[index])) total += weights[index];
        }
        if (total <= 0) return present.OrderBy(id => id, StringComparer.Ordinal).First();
        float roll = ClampUnit(rarityUnit) * total;
        for (int index = 0; index < rarities.Length; index++)
        {
            if (!present.Contains(rarities[index])) continue;
            roll -= weights[index];
            if (roll <= 0f) return rarities[index];
        }
        return rarities[rarities.Length - 1];
    }

    private static (string[] Rarities, int[] Weights) SelectTable(HashSet<string> present)
    {
        (string[] Rarities, int[] Weights)? exact = null;
        (string[] Rarities, int[] Weights)? smallestSuperset = null;
        foreach ((string[] rarities, int[] weights) in WeightTables)
        {
            var table = new HashSet<string>(rarities, StringComparer.Ordinal);
            if (table.SetEquals(present)) exact = (rarities, weights);
            if (present.All(table.Contains) &&
                (smallestSuperset == null || rarities.Length < smallestSuperset.Value.Rarities.Length))
                smallestSuperset = (rarities, weights);
        }
        if (exact != null) return exact.Value;
        if (smallestSuperset != null) return smallestSuperset.Value;
        return (present.OrderBy(id => Array.IndexOf(RankedRarities, id)).ToArray(),
            Enumerable.Repeat(1, present.Count).ToArray());
    }

    private static CardDefinition Pick(IReadOnlyList<CardDefinition> cards, float cardUnit)
    {
        int index = Math.Min(cards.Count - 1, (int)(ClampUnit(cardUnit) * cards.Count));
        return cards[index];
    }

    private static float ClampUnit(float value) =>
        value < 0f ? 0f : value >= 1f ? 0.999999f : value;
}
}
