using System;
using LegendsOfFurry.Content.Contracts;

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>
/// 将数据库卡牌定义投影为 HandCardView 可读取的纯运行时显示数据。
/// </summary>
public static class RuntimeCardAdapter
{
    /// <summary>
    /// 创建不保存为 Unity 资产的卡面视图；权威数据仍是传入的 CardDefinition。
    /// </summary>
    /// <param name="definition">数据库发布包中的卡牌定义。</param>
    /// <returns>可供现有手牌界面和费用代码读取的临时对象。</returns>
    public static CardData CreateView(CardDefinition definition)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        CardData card = CardData.Runtime(
            definition.CardId,
            definition.DisplayName,
            definition.Pools.Count > 0 ? definition.Pools[0].PoolId : string.Empty,
            ParseFamily(definition.FamilyId),
            ParseRarity(definition.RarityId),
            BuildCostText(definition.Cost),
            definition.Target.Range,
            ParseTargetMode(definition.Target.SelectionMode),
            definition.IsAttack,
            definition.Description,
            definition.ExhaustOnPlay,
            definition.Temporary,
            definition.Curse,
            definition.Unplayable);
        return card;
    }

    /// <summary>
    /// 将结构化费用转换成旧界面显示文本；结算仍读取结构化字段而非再次解析文本。
    /// </summary>
    /// <param name="cost">数据库中的行动点和法力费用。</param>
    /// <returns>与现有卡面兼容的费用文本。</returns>
    private static string BuildCostText(CardCostDefinition cost)
    {
        string action = cost.SpendAllAction ? "x" : Math.Max(0, cost.ActionCost).ToString();
        if (!cost.SpendAllMana && cost.ManaCost <= 0)
        {
            return action;
        }
        string mana = cost.SpendAllMana ? "x" : Math.Max(0, cost.ManaCost).ToString();
        return action + "+" + mana;
    }

    /// <summary>
    /// 将稳定字符串稀有度映射到迁移期旧枚举；未知值使用灰色并由发布校验负责阻断。
    /// </summary>
    /// <param name="rarityId">稳定稀有度 ID。</param>
    /// <returns>现有界面使用的稀有度枚举。</returns>
    private static CardRarity ParseRarity(string rarityId)
    {
        return Enum.TryParse(rarityId, true, out CardRarity rarity) ? rarity : CardRarity.Gray;
    }

    /// <summary>
    /// 将稳定字符串卡牌家族映射到迁移期旧枚举。
    /// </summary>
    /// <param name="familyId">稳定家族 ID。</param>
    /// <returns>现有规则暂时使用的家族枚举。</returns>
    private static CardFamily ParseFamily(string familyId)
    {
        return Enum.TryParse(familyId, true, out CardFamily family) ? family : CardFamily.None;
    }

    /// <summary>
    /// 将内容合同的目标选择模式映射为现有输入系统的四种入口。
    /// </summary>
    /// <param name="selectionMode">self、unit、cell、direction 或 none。</param>
    /// <returns>现有目标选择枚举。</returns>
    private static CardTargetMode ParseTargetMode(string selectionMode)
    {
        return selectionMode switch
        {
            "unit" => CardTargetMode.Unit,
            "cell" => CardTargetMode.AreaCell,
            "direction" => CardTargetMode.Direction,
            _ => CardTargetMode.Self
        };
    }
}
}
