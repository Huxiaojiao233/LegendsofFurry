using System;
using System.Collections.Generic;
using UnityEngine;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;

public enum CardTargetMode { Self, Unit, Direction, AreaCell }
public enum DamageType { Normal, Fire, Ice, Grass, Lightning, Rock, Wind, Water, Light, Dark, Poison, True }

/// <summary>
/// 供 Unity 卡面与输入系统读取的纯运行时投影；权威内容始终来自数据库 CardDefinition。
/// </summary>
public sealed class CardData
{
    [Header("基础信息")]
    public string cardId;
    public string cardName;
    [TextArea(2, 6)] public string description;
    public Sprite artwork;
    public string rarityId;
    public string sourcePool;
    public string familyId;

    [Header("费用与类型")]
    public string costText = "1";
    public int actionPointCost = 1;
    public int manaCost;
    public bool spendsAllActionPoints;
    public bool spendsAllMana;
    public bool isAttack;
    public bool exhaust;
    public bool temporary;
    public bool curse;
    public bool unplayable;

    [Header("目标")]
    public CardTargetMode targetMode = CardTargetMode.Self;
    [Min(0)] public int range;

    public bool RequiresTarget => targetMode != CardTargetMode.Self;

    /// <summary>
    /// 从数据库字段创建一份只在本次运行中使用的卡面投影。
    /// </summary>
    /// <param name="id">稳定卡牌 ID。</param>
    /// <param name="displayName">卡面显示名称。</param>
    /// <param name="pool">首个卡池的显示标识。</param>
    /// <param name="cardFamily">卡牌家族。</param>
    /// <param name="cardRarity">卡牌稀有度。</param>
    /// <param name="cost">派生后的费用文本。</param>
    /// <param name="cardRange">目标距离。</param>
    /// <param name="mode">目标选择模式。</param>
    /// <param name="attack">是否为攻击牌。</param>
    /// <param name="rulesText">卡牌规则描述。</param>
    /// <param name="isExhaust">打出后是否消耗。</param>
    /// <param name="isTemporary">是否为临时牌。</param>
    /// <param name="isCurse">是否为诅咒牌。</param>
    /// <param name="cannotPlay">是否禁止主动打出。</param>
    /// <returns>不参与 Unity 资产序列化的卡面数据。</returns>
    public static CardData Runtime(
        string id, string displayName, string pool, string cardFamilyId,
        string cardRarityId, string cost, int cardRange, CardTargetMode mode,
        bool attack, string rulesText, bool isExhaust = false,
        bool isTemporary = false, bool isCurse = false, bool cannotPlay = false)
    {
        CardData card = new CardData();
        card.cardId = id;
        card.cardName = displayName;
        card.sourcePool = pool;
        card.familyId = cardFamilyId ?? string.Empty;
        card.rarityId = cardRarityId ?? string.Empty;
        card.costText = string.IsNullOrWhiteSpace(cost) ? "/" : cost;
        card.range = Mathf.Max(0, cardRange);
        card.targetMode = mode;
        card.isAttack = attack;
        card.description = rulesText;
        card.exhaust = isExhaust;
        card.temporary = isTemporary;
        card.curse = isCurse;
        card.unplayable = cannotPlay;
        ParseCost(card);
        return card;
    }

    /// <summary>
    /// 将数据库结构化费用派生出的显示文本同步为现有界面读取的费用字段。
    /// </summary>
    /// <param name="card">需要补齐费用字段的运行时卡面。</param>
    private static void ParseCost(CardData card)
    {
        card.actionPointCost = 0;
        card.manaCost = 0;
        card.spendsAllActionPoints = false;
        card.spendsAllMana = false;

        if (card.costText.Equals("x", System.StringComparison.OrdinalIgnoreCase))
        {
            card.spendsAllActionPoints = true;
            return;
        }

        string[] parts = card.costText.Split('+');
        if (parts.Length > 0 && int.TryParse(parts[0], out int actionCost))
            card.actionPointCost = Mathf.Max(0, actionCost);

        if (parts.Length <= 1) return;
        if (parts[1].Equals("x", System.StringComparison.OrdinalIgnoreCase))
            card.spendsAllMana = true;
        else if (int.TryParse(parts[1], out int parsedMana))
            card.manaCost = Mathf.Max(0, parsedMana);
    }
}

public sealed class CardInstance : IContentInstance<CardDefinition>
{
    private readonly Dictionary<string, int> runtimeValues = new Dictionary<string, int>();

    public string InstanceId { get; }
    public CardData Data { get; }
    public CardDefinition Definition { get; }
    public bool FreePlay { get; set; }
    public bool ForcedPlay { get; set; }

    /// <summary>
    /// 从数据库发布定义创建卡牌实例，并生成只用于 Unity 卡面显示的运行时投影。
    /// </summary>
    /// <param name="definition">运行时加载的共享卡牌定义。</param>
    public CardInstance(CardDefinition definition)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        ContentId.Require(definition.CardId, nameof(definition));
        InstanceId = Guid.NewGuid().ToString("N");
        Definition = definition;
        Data = RuntimeCardAdapter.CreateView(definition);
    }

    /// <summary>鉴定抽出的装备牌：打出后消耗，未打出则进弃牌堆并随弃牌回流。</summary>
    public void ApplyAppraisalRewardFlags()
    {
        Data.temporary = true;
        Data.exhaust = true;
    }

    /// <summary>
    /// 读取当前卡牌实例上的整数运行时值；尚未写入的 key 返回零。
    /// </summary>
    /// <param name="key">稳定的运行时值 key。</param>
    /// <returns>当前整数值，或不存在时的零。</returns>
    public int GetRuntimeValue(string key)
    {
        return !string.IsNullOrWhiteSpace(key) && runtimeValues.TryGetValue(key, out int value) ? value : 0;
    }

    /// <summary>
    /// 设置当前卡牌实例上的整数运行时值，供永久伤害成长等通用效果保存状态。
    /// </summary>
    /// <param name="key">稳定的运行时值 key。</param>
    /// <param name="value">需要保存的新值。</param>
    public void SetRuntimeValue(string key, int value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }
        runtimeValues[key] = value;
    }

    /// <summary>
    /// 原子地增减当前卡牌实例上的整数运行时值并返回修改后的结果。
    /// </summary>
    /// <param name="key">稳定的运行时值 key。</param>
    /// <param name="delta">本次增量，可为负数。</param>
    /// <returns>修改后的整数值；key 无效时返回零。</returns>
    public int ModifyRuntimeValue(string key, int delta)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return 0;
        }
        int value = GetRuntimeValue(key) + delta;
        runtimeValues[key] = value;
        return value;
    }
}
