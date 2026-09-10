#nullable enable
using System.Collections.Generic;

namespace LegendsOfFurry.Content.Contracts
{

/// <summary>
/// 表示从 .lofepackage 加载的完整、只读游戏内容快照。
/// </summary>
public sealed class ContentPackage
{
    public int SchemaVersion { get; set; }
    public string ContentVersion { get; set; } = string.Empty;
    public List<CardDefinition> Cards { get; set; } = new List<CardDefinition>();
    public List<CardPoolDefinition> CardPools { get; set; } = new List<CardPoolDefinition>();
    public List<StatusDefinition> Statuses { get; set; } = new List<StatusDefinition>();
    public List<DeckDefinition> Decks { get; set; } = new List<DeckDefinition>();
    public List<RarityDefinition> Rarities { get; set; } = new List<RarityDefinition>();
    public List<AssetDefinition> Assets { get; set; } = new List<AssetDefinition>();
    public List<ClassProfileDefinition> ClassProfiles { get; set; } = new List<ClassProfileDefinition>();
    public List<UnitDefinition> Units { get; set; } = new List<UnitDefinition>();
    public List<AiProfileDefinition> AiProfiles { get; set; } = new List<AiProfileDefinition>();
    public List<EquipmentDefinition> Equipment { get; set; } = new List<EquipmentDefinition>();
    public GameSettingsDefinition GameSettings { get; set; } = new GameSettingsDefinition();
}

/// <summary>
/// 描述一个可供牌库配方和卡牌查询使用的卡池。
/// </summary>
public sealed class CardPoolDefinition : IContentDefinition
{
    public string PoolId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }

    /// <summary>返回卡池在注册表中的类型。</summary>
    public string GetDefinitionKind() => ContentDefinitionKinds.CardPool;

    /// <summary>返回稳定卡池 ID。</summary>
    public string GetDefinitionId() => PoolId;
}

/// <summary>
/// 描述一个可由卡牌效果引用的状态及其通用叠层规则。
/// </summary>
public sealed class StatusDefinition : IContentDefinition
{
    public string StatusId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "neutral";
    public int MaximumStacks { get; set; }
    public string StackingPolicy { get; set; } = "add";
    public string DurationPolicy { get; set; } = "none";
    public bool Enabled { get; set; } = true;
    public List<string> ApplyOnTerrainIds { get; set; } = new List<string>();
    public List<BehaviorDefinition> Behaviors { get; set; } = new List<BehaviorDefinition>();

    /// <summary>返回状态在注册表和行为归属中的类型。</summary>
    public string GetDefinitionKind() => ContentDefinitionKinds.Status;

    /// <summary>返回稳定状态 ID。</summary>
    public string GetDefinitionId() => StatusId;
}

/// <summary>描述一个可扩展的职业特性键值。</summary>
public sealed class ClassTraitDefinition
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// 描述一套固定牌库及其卡牌数量。
/// </summary>
public sealed class DeckDefinition : IContentDefinition
{
    public string DeckId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsTestDeck { get; set; }
    public bool Enabled { get; set; } = true;
    public List<DeckEntryDefinition> Entries { get; set; } = new List<DeckEntryDefinition>();

    /// <summary>返回固定牌库在注册表中的类型。</summary>
    public string GetDefinitionKind() => ContentDefinitionKinds.Deck;

    /// <summary>返回稳定牌库 ID。</summary>
    public string GetDefinitionId() => DeckId;
}

/// <summary>
/// 描述固定牌库中一张卡牌的加入数量和顺序。
/// </summary>
public sealed class DeckEntryDefinition
{
    public string CardId { get; set; } = string.Empty;
    public int Amount { get; set; } = 1;
    public int SortOrder { get; set; }
}

/// <summary>描述职业基础数值、起始牌库配方和被动行为。</summary>
public sealed class ClassProfileDefinition : IContentDefinition
{
    public string ClassId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int InitialHealth { get; set; } = 10;
    public int InitialMana { get; set; }
    public int MaximumMana { get; set; } = 10;
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }
    public List<DeckRecipePoolDefinition> DeckRecipe { get; set; } = new List<DeckRecipePoolDefinition>();
    public List<BehaviorDefinition> Behaviors { get; set; } = new List<BehaviorDefinition>();
    public List<ClassTraitDefinition> Traits { get; set; } = new List<ClassTraitDefinition>();

    /// <summary>返回职业资料在注册表和行为归属中的类型。</summary>
    public string GetDefinitionKind() => ContentDefinitionKinds.Class;

    /// <summary>返回稳定职业 ID。</summary>
    public string GetDefinitionId() => ClassId;

    /// <summary>读取整数职业特性；缺失或格式无效时返回默认值。</summary>
    public int GetTraitInt(string key, int defaultValue = 0)
    {
        ClassTraitDefinition? trait = Traits.Find(item => item.Key == key);
        return trait != null && int.TryParse(trait.Value, out int value) ? value : defaultValue;
    }

    /// <summary>读取小数职业特性；使用不受系统区域影响的格式。</summary>
    public float GetTraitFloat(string key, float defaultValue = 0f)
    {
        ClassTraitDefinition? trait = Traits.Find(item => item.Key == key);
        return trait != null && float.TryParse(trait.Value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float value) ? value : defaultValue;
    }

    /// <summary>读取布尔职业特性；缺失时返回默认值。</summary>
    public bool GetTraitBool(string key, bool defaultValue = false)
    {
        ClassTraitDefinition? trait = Traits.Find(item => item.Key == key);
        return trait != null && bool.TryParse(trait.Value, out bool value) ? value : defaultValue;
    }

    /// <summary>读取字符串职业特性；缺失或只包含空白时返回默认值。</summary>
    public string GetTraitString(string key, string defaultValue = "")
    {
        ClassTraitDefinition? trait = Traits.Find(item => item.Key == key);
        return trait != null && !string.IsNullOrWhiteSpace(trait.Value) ? trait.Value : defaultValue;
    }
}

/// <summary>描述职业起始牌库从卡池抽取的数量、可选固定卡和排序。</summary>
public sealed class DeckRecipePoolDefinition
{
    public string PoolId { get; set; } = string.Empty;
    public int Amount { get; set; }
    public string FixedCardId { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

/// <summary>描述策划可维护的基础战斗参数。</summary>
public sealed class GameSettingsDefinition
{
    public int HandLimit { get; set; } = 10;
    public int StartingHandSize { get; set; } = 5;
    public int DrawPerTurn { get; set; } = 5;
    public int BaseActionPoints { get; set; } = 3;
    public int BaseMoveSteps { get; set; } = 2;
    public string PlayerUnitId { get; set; } = string.Empty;
    public string DefaultWorldId { get; set; } = "demo";
}

/// <summary>描述一个统一的数据驱动单位。角色、魔物和 Boss 通过字段区分而不是拆表。</summary>
public sealed class UnitDefinition : IContentDefinition
{
    public string UnitId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string UnitKind { get; set; } = "character";
    public string DefaultFaction { get; set; } = "neutral";
    public string Controller { get; set; } = "player";
    public bool IsBoss { get; set; }
    public bool Recruitable { get; set; }
    public bool CanJoinParty { get; set; }
    public int InitialHealth { get; set; } = 10;
    public int BaseDamage { get; set; } = 3;
    public int MoveSteps { get; set; } = 2;
    public int InitialActionPoints { get; set; } = 3;
    public int StartingHandSize { get; set; } = 5;
    public int DrawPerTurn { get; set; } = 5;
    public string DeckId { get; set; } = string.Empty;
    public string AiProfileId { get; set; } = string.Empty;
    public UnitAiTuningDefinition AiOverrides { get; set; } = new UnitAiTuningDefinition();
    public List<BossPhaseDefinition> BossPhases { get; set; } = new List<BossPhaseDefinition>();
    public List<string> Capabilities { get; set; } = new List<string>();
    public string TokenFrameColor { get; set; } = string.Empty;
    public string PortraitKey { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }
    public List<string> Tags { get; set; } = new List<string>();
    public List<BehaviorDefinition> Behaviors { get; set; } = new List<BehaviorDefinition>();

    public bool IsHostile => DefaultFaction == "enemy";

    public string GetDefinitionKind() => ContentDefinitionKinds.Unit;
    public string GetDefinitionId() => UnitId;
}

/// <summary>单位对 AI 模板的可选数值覆盖；NaN 表示继续使用模板值。</summary>
public sealed class UnitAiTuningDefinition
{
    public float AttackWeight { get; set; } = float.NaN;
    public float DefenseWeight { get; set; } = float.NaN;
    public float HealingWeight { get; set; } = float.NaN;
    public float ApproachWeight { get; set; } = float.NaN;
    public float RetreatWeight { get; set; } = float.NaN;
    public float KillWeight { get; set; } = float.NaN;
    public float PreferredRange { get; set; } = float.NaN;
    public float LowHealthThreshold { get; set; } = float.NaN;
}

/// <summary>Boss 在血量阈值内叠加的阶段规则；基础决策器保持不变。</summary>
public sealed class BossPhaseDefinition
{
    public float MaximumHealthRatio { get; set; } = 1f;
    public string AiProfileId { get; set; } = string.Empty;
    public UnitAiTuningDefinition AiOverrides { get; set; } = new UnitAiTuningDefinition();
}

/// <summary>可复用的 Utility AI 权重模板。</summary>
public sealed class AiProfileDefinition : IContentDefinition
{
    public string AiProfileId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public float AttackWeight { get; set; } = 1f;
    public float DefenseWeight { get; set; } = 1f;
    public float HealingWeight { get; set; } = 1f;
    public float ApproachWeight { get; set; } = 1f;
    public float RetreatWeight { get; set; } = 1f;
    public float KillWeight { get; set; } = 1f;
    public float PreferredRange { get; set; } = 1f;
    public float LowHealthThreshold { get; set; } = 0.3f;
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }

    public string GetDefinitionKind() => ContentDefinitionKinds.AiProfile;
    public string GetDefinitionId() => AiProfileId;
}

/// <summary>描述一件可装备内容及其贡献的卡池。</summary>
public sealed class EquipmentDefinition : IContentDefinition
{
    public string EquipmentId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SlotKey { get; set; } = string.Empty;
    public string CardPoolId { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }
    public List<string> Tags { get; set; } = new List<string>();
    public List<BehaviorDefinition> Behaviors { get; set; } = new List<BehaviorDefinition>();

    public string GetDefinitionKind() => ContentDefinitionKinds.Equipment;
    public string GetDefinitionId() => EquipmentId;
}

/// <summary>
/// 描述稀有度的显示属性和默认抽取权重。
/// </summary>
public sealed class RarityDefinition : IContentDefinition
{
    public string RarityId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#FFFFFFFF";
    public decimal DefaultWeight { get; set; } = 1m;
    public int SortOrder { get; set; }

    /// <summary>返回稀有度在注册表中的类型。</summary>
    public string GetDefinitionKind() => ContentDefinitionKinds.Rarity;

    /// <summary>返回稳定稀有度 ID。</summary>
    public string GetDefinitionId() => RarityId;
}

/// <summary>
/// 描述内容数据库引用的受管图片、音效或表现资源。
/// </summary>
public sealed class AssetDefinition : IContentDefinition
{
    public string AssetKey { get; set; } = string.Empty;
    public string AssetKind { get; set; } = "artwork";
    public string RelativePath { get; set; } = string.Empty;
    public string? Sha256 { get; set; }

    /// <summary>返回受管资源在注册表中的类型。</summary>
    public string GetDefinitionKind() => ContentDefinitionKinds.Asset;

    /// <summary>返回稳定受管资源 Key。</summary>
    public string GetDefinitionId() => AssetKey;
}
}
