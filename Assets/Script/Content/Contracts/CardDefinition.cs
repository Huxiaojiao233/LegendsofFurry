#nullable enable
using System.Collections.Generic;

namespace LegendsOfFurry.Content.Contracts
{

/// <summary>
/// 描述一张由内容数据库维护、可被游戏运行时加载的卡牌。
/// </summary>
public sealed class CardDefinition : IContentDefinition
{
    public string CardId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ArtworkKey { get; set; }
    public string RarityId { get; set; } = "gray";
    public string FamilyId { get; set; } = "none";
    public bool IsAttack { get; set; }
    public bool ExhaustOnPlay { get; set; }
    public bool Temporary { get; set; }
    public bool Curse { get; set; }
    public bool Unplayable { get; set; }
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }
    public CardCostDefinition Cost { get; set; } = new CardCostDefinition();
    public CardTargetRule Target { get; set; } = new CardTargetRule();
    public List<string> Tags { get; set; } = new List<string>();
    public List<CardPoolMembership> Pools { get; set; } = new List<CardPoolMembership>();
    public List<BehaviorDefinition> Behaviors { get; set; } = new List<BehaviorDefinition>();

    /// <summary>Returns the registry and behavior-owner kind for cards.</summary>
    public string GetDefinitionKind() => ContentDefinitionKinds.Card;

    /// <summary>Returns the stable card ID authored by the content package.</summary>
    public string GetDefinitionId() => CardId;
}

/// <summary>
/// 保存卡牌的结构化资源费用，显示文本由这些字段派生。
/// </summary>
public sealed class CardCostDefinition
{
    public int ActionCost { get; set; }
    public int ManaCost { get; set; }
    public bool SpendAllAction { get; set; }
    public bool SpendAllMana { get; set; }
}

/// <summary>
/// 描述玩家为一张卡选择主目标时必须满足的通用规则。
/// </summary>
public sealed class CardTargetRule
{
    public string SelectionMode { get; set; } = "self";
    public int Range { get; set; }
    public string TeamFilter { get; set; } = "any";
    public string LifeStateFilter { get; set; } = "alive";
    public bool RequiresLineOfSight { get; set; }
    public bool AllowSelf { get; set; } = true;
}

/// <summary>
/// 描述卡牌在一个卡池中的权重和显示顺序。
/// </summary>
public sealed class CardPoolMembership
{
    public string PoolId { get; set; } = string.Empty;
    public decimal Weight { get; set; } = 1m;
    public int SortOrder { get; set; }
}
}
