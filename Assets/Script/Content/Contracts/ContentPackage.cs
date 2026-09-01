#nullable enable
using System.Collections.Generic;

namespace LegendsOfFurry.Content.Contracts
{

/// <summary>
/// 表示从 SQLite 编辑源导出的完整、只读游戏内容快照。
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
    public List<CharacterDefinition> Characters { get; set; } = new List<CharacterDefinition>();
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

    /// <summary>Returns the registry kind for card pools.</summary>
    public string GetDefinitionKind() => ContentDefinitionKinds.CardPool;

    /// <summary>Returns the stable card-pool ID.</summary>
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
    public List<BehaviorDefinition> Behaviors { get; set; } = new List<BehaviorDefinition>();

    /// <summary>Returns the registry and behavior-owner kind for statuses.</summary>
    public string GetDefinitionKind() => ContentDefinitionKinds.Status;

    /// <summary>Returns the stable status ID.</summary>
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

    /// <summary>Returns the registry kind for fixed decks.</summary>
    public string GetDefinitionKind() => ContentDefinitionKinds.Deck;

    /// <summary>Returns the stable deck ID.</summary>
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

    /// <summary>Returns the registry and behavior-owner kind for class profiles.</summary>
    public string GetDefinitionKind() => ContentDefinitionKinds.Class;

    /// <summary>Returns the stable class ID.</summary>
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
    public string PlayerCharacterId { get; set; } = string.Empty;
    public string EnemyCharacterId { get; set; } = string.Empty;
}

/// <summary>Describes a data-driven combatant independently from its scene GameObject.</summary>
public sealed class CharacterDefinition : IContentDefinition
{
    public string CharacterId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int InitialHealth { get; set; } = 10;
    public int BaseDamage { get; set; } = 3;
    public int MoveSteps { get; set; } = 2;
    public string TokenFrameColor { get; set; } = string.Empty;
    public string PortraitKey { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }
    public List<string> Tags { get; set; } = new List<string>();
    public List<BehaviorDefinition> Behaviors { get; set; } = new List<BehaviorDefinition>();

    public string GetDefinitionKind() => ContentDefinitionKinds.Character;
    public string GetDefinitionId() => CharacterId;
}

/// <summary>Describes one equippable content item and the card pool it contributes.</summary>
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

    /// <summary>Returns the registry kind for rarities.</summary>
    public string GetDefinitionKind() => ContentDefinitionKinds.Rarity;

    /// <summary>Returns the stable rarity ID.</summary>
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

    /// <summary>Returns the registry kind for managed assets.</summary>
    public string GetDefinitionKind() => ContentDefinitionKinds.Asset;

    /// <summary>Returns the stable managed-asset key.</summary>
    public string GetDefinitionId() => AssetKey;
}
}
