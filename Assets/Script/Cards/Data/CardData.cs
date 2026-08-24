using UnityEngine;

public enum CardEffectType
{
    Heal,
    GainBlock,
    PhysicalDamage,
    Move,
    FireDamage,
    Custom
}

public enum CardRarity { Gray, Blue, Purple, Gold, Red }
public enum CardTargetMode { Self, Unit, Direction, AreaCell }
public enum CardFamily { None, Sword, Shield, Bow, Staff, Scepter, Dagger, Equipment }
public enum DamageType { Normal, Fire, Ice, Grass, Lightning, Rock, Wind, Water, Light, Dark, Poison, True }

[CreateAssetMenu(fileName = "NewCard", menuName = "Legends Of Furry/Card Data")]
public class CardData : ScriptableObject
{
    [Header("基础信息")]
    public string cardId;
    public string cardName;
    [TextArea(2, 6)] public string description;
    public Sprite artwork;
    public CardRarity rarity;
    public string sourcePool;
    public CardFamily family;

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

    // 旧版字段保留，避免项目中已有资产和简单效果处理器失效。
    [Header("旧版兼容")]
    public CardEffectType effectType = CardEffectType.Custom;
    public int effectValue;
    [Min(0)] public int attackRangeSize;

    public bool RequiresTarget => targetMode != CardTargetMode.Self;

    public int GetAttackRangeRadius()
    {
        if (range > 0) return range;
        return attackRangeSize <= 0 ? 0 : Mathf.Max(0, (attackRangeSize - 1) / 2);
    }

    public bool IsWithinAttackRange(Vector2Int origin, Vector2Int target)
    {
        int distance = Mathf.Abs(origin.x - target.x) + Mathf.Abs(origin.y - target.y);
        return distance <= GetAttackRangeRadius();
    }

    public static CardData Runtime(
        string id, string displayName, string pool, CardFamily cardFamily,
        CardRarity cardRarity, string cost, int cardRange, CardTargetMode mode,
        bool attack, string rulesText, bool isExhaust = false,
        bool isTemporary = false, bool isCurse = false, bool cannotPlay = false)
    {
        CardData card = CreateInstance<CardData>();
        card.hideFlags = HideFlags.DontSave;
        card.cardId = id;
        card.cardName = displayName;
        card.sourcePool = pool;
        card.family = cardFamily;
        card.rarity = cardRarity;
        card.costText = string.IsNullOrWhiteSpace(cost) ? "/" : cost;
        card.range = Mathf.Max(0, cardRange);
        card.targetMode = mode;
        card.isAttack = attack;
        card.description = rulesText;
        card.exhaust = isExhaust;
        card.temporary = isTemporary;
        card.curse = isCurse;
        card.unplayable = cannotPlay;
        card.effectType = CardEffectType.Custom;
        ParseCost(card);
        return card;
    }

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

public sealed class CardInstance
{
    public CardData Data { get; }
    public int PersistentDamageBonus { get; set; }
    public bool FreePlay { get; set; }
    public bool ForcedPlay { get; set; }

    public CardInstance(CardData data) { Data = data; }
}
