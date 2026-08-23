using UnityEngine;

/// <summary>卡牌效果的基础分类，结算系统可据此选择具体处理方式。</summary>
public enum CardEffectType
{
    Heal,
    GainBlock,
    PhysicalDamage,
    Move,
    FireDamage
}

[CreateAssetMenu(
    fileName = "NewCard",
    menuName = "Legends Of Furry/Card Data")]
/// <summary>
/// 单张卡牌的静态配置数据。每种卡牌对应一个 ScriptableObject 资产。
/// </summary>
public class CardData : ScriptableObject
{
    [Header("基础信息")]
    public string cardId;
    public string cardName;

    [TextArea(2, 5)]
    public string description;

    public Sprite artwork;
    public int actionPointCost = 1;

    [Header("卡牌效果")]
    public CardEffectType effectType;
    public int effectValue;

    [Header("攻击范围")]
    [Tooltip("以施法者为中心的正方形边长。爪击 3 表示 3×3，火球术 5 表示再大一圈。0 表示不需要选目标。")]
    [Min(0)]
    public int attackRangeSize;

    public bool RequiresTarget =>
        effectType == CardEffectType.PhysicalDamage ||
        effectType == CardEffectType.FireDamage;

    /// <summary>切比雪夫半径：(边长-1)/2。3×3 → 1，5×5 → 2。</summary>
    public int GetAttackRangeRadius()
    {
        int size = attackRangeSize;
        if (size <= 0 && RequiresTarget)
        {
            size = 3;
        }

        if (size < 1)
        {
            return 0;
        }

        if ((size & 1) == 0)
        {
            size += 1;
        }

        return (size - 1) / 2;
    }

    public bool IsWithinAttackRange(Vector2Int origin, Vector2Int target)
    {
        int radius = GetAttackRangeRadius();
        int deltaX = Mathf.Abs(origin.x - target.x);
        int deltaY = Mathf.Abs(origin.y - target.y);
        return Mathf.Max(deltaX, deltaY) <= radius;
    }
}
