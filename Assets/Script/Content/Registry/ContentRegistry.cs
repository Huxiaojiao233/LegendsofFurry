using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LegendsOfFurry.Content.Contracts;

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>
/// 为已发布内容建立只读 ID 索引，并只向游戏暴露启用的卡牌、状态和牌库。
/// </summary>
public sealed class ContentRegistry
{
    private readonly Dictionary<string, CardDefinition> cards;
    private readonly Dictionary<string, StatusDefinition> statuses;
    private readonly Dictionary<string, DeckDefinition> decks;
    private readonly Dictionary<string, ClassProfileDefinition> classProfiles;
    private readonly Dictionary<string, CardPoolDefinition> cardPools;
    private readonly Dictionary<string, RarityDefinition> rarities;
    private readonly Dictionary<string, AssetDefinition> assets;
    private readonly Dictionary<string, CharacterDefinition> characters;
    private readonly Dictionary<string, EquipmentDefinition> equipment;

    /// <summary>
    /// 从经过校验的内容包构建不可变索引；重复 ID 会立即抛出异常而不是覆盖。
    /// </summary>
    /// <param name="package">加载器返回的完整内容包。</param>
    public ContentRegistry(ContentPackage package)
    {
        if (package == null)
        {
            throw new ArgumentNullException(nameof(package));
        }

        Package = package;
        cards = BuildIndex(package.Cards, item => item.Enabled, ContentDefinitionKinds.Card);
        statuses = BuildIndex(package.Statuses, item => item.Enabled, ContentDefinitionKinds.Status);
        decks = BuildIndex(package.Decks, item => item.Enabled, ContentDefinitionKinds.Deck);
        classProfiles = BuildIndex(package.ClassProfiles, item => item.Enabled, ContentDefinitionKinds.Class);
        cardPools = BuildIndex(package.CardPools, item => item.Enabled, ContentDefinitionKinds.CardPool);
        rarities = BuildIndex(package.Rarities, _ => true, ContentDefinitionKinds.Rarity);
        assets = BuildIndex(package.Assets, _ => true, ContentDefinitionKinds.Asset);
        characters = BuildIndex(package.Characters, item => item.Enabled, ContentDefinitionKinds.Character);
        equipment = BuildIndex(package.Equipment, item => item.Enabled, ContentDefinitionKinds.Equipment);
    }

    public ContentPackage Package { get; }
    public IReadOnlyCollection<CardDefinition> Cards => cards.Values;
    public IReadOnlyCollection<StatusDefinition> Statuses => statuses.Values;
    public IReadOnlyCollection<DeckDefinition> Decks => decks.Values;
    public IReadOnlyCollection<ClassProfileDefinition> ClassProfiles => classProfiles.Values;
    public IReadOnlyCollection<CardPoolDefinition> CardPools => cardPools.Values;
    public IReadOnlyCollection<RarityDefinition> Rarities => rarities.Values;
    public IReadOnlyCollection<AssetDefinition> Assets => assets.Values;
    public IReadOnlyCollection<CharacterDefinition> Characters => characters.Values;
    public IReadOnlyCollection<EquipmentDefinition> Equipment => equipment.Values;
    public GameSettingsDefinition GameSettings => Package.GameSettings;

    /// <summary>
    /// 查找一张启用卡牌，并在 ID 不存在时返回 false。
    /// </summary>
    /// <param name="cardId">稳定卡牌 ID。</param>
    /// <param name="card">成功时返回卡牌定义。</param>
    /// <returns>找到启用卡牌时返回 true。</returns>
    public bool TryGetCard(string cardId, out CardDefinition card)
    {
        return cards.TryGetValue(cardId, out card);
    }

    /// <summary>
    /// 获取一张启用卡牌；缺失时抛出包含 ID 的异常，供开发日志定位坏引用。
    /// </summary>
    /// <param name="cardId">稳定卡牌 ID。</param>
    /// <returns>对应卡牌定义。</returns>
    public CardDefinition GetCard(string cardId)
    {
        if (!cards.TryGetValue(cardId, out CardDefinition card))
        {
            throw new KeyNotFoundException($"内容包中不存在启用卡牌：{cardId}");
        }
        return card;
    }

    /// <summary>
    /// 按卡池稳定 ID 返回全部启用卡牌，并保持发布包中的确定性顺序。
    /// </summary>
    /// <param name="poolId">需要查询的卡池 ID。</param>
    /// <returns>属于指定卡池的只读卡牌快照。</returns>
    public IReadOnlyList<CardDefinition> GetCardsInPool(string poolId)
    {
        return Package.Cards
            .Where(card => card.Enabled && card.Pools.Any(pool => pool.PoolId == poolId))
            .ToArray();
    }

    /// <summary>
    /// 按标签返回全部启用卡牌，并使用序号比较避免区域设置影响内容查询。
    /// </summary>
    /// <param name="tag">需要匹配的稳定标签。</param>
    /// <returns>带有指定标签的只读卡牌快照。</returns>
    public IReadOnlyList<CardDefinition> GetCardsWithTag(string tag)
    {
        return Package.Cards
            .Where(card => card.Enabled && card.Tags.Contains(tag, StringComparer.Ordinal))
            .ToArray();
    }

    /// <summary>
    /// 查找一个启用状态定义，并在 ID 不存在时返回 false。
    /// </summary>
    /// <param name="statusId">稳定状态 ID。</param>
    /// <param name="status">成功时返回状态定义。</param>
    /// <returns>找到启用状态时返回 true。</returns>
    public bool TryGetStatus(string statusId, out StatusDefinition status)
    {
        return statuses.TryGetValue(statusId, out status);
    }

    /// <summary>
    /// 获取一套启用固定牌库；缺失时抛出包含 ID 的异常。
    /// </summary>
    /// <param name="deckId">稳定牌库 ID。</param>
    /// <returns>对应固定牌库定义。</returns>
    public DeckDefinition GetDeck(string deckId)
    {
        if (!decks.TryGetValue(deckId, out DeckDefinition deck))
        {
            throw new KeyNotFoundException($"内容包中不存在启用牌库：{deckId}");
        }
        return deck;
    }

    /// <summary>查找启用职业资料。</summary>
    public bool TryGetClassProfile(string classId, out ClassProfileDefinition profile)
    {
        return classProfiles.TryGetValue(classId, out profile);
    }

    /// <summary>按稳定 ID 查找启用卡池，供装备栏等内容驱动界面显示策划名称。</summary>
    public bool TryGetCardPool(string poolId, out CardPoolDefinition pool)
    {
        return cardPools.TryGetValue(poolId ?? string.Empty, out pool);
    }

    /// <summary>按稳定 ID 查找已发布稀有度。</summary>
    /// <param name="rarityId">稳定稀有度 ID。</param>
    /// <param name="rarity">找到时返回对应定义。</param>
    /// <returns>内容包包含该稀有度时返回 true。</returns>
    public bool TryGetRarity(string rarityId, out RarityDefinition rarity)
    {
        return rarities.TryGetValue(rarityId ?? string.Empty, out rarity);
    }

    /// <summary>按稳定 Key 查找受管资源。</summary>
    /// <param name="assetKey">稳定资源 Key。</param>
    /// <param name="asset">找到时返回对应定义。</param>
    /// <returns>内容包包含该资源时返回 true。</returns>
    public bool TryGetAsset(string assetKey, out AssetDefinition asset)
    {
        return assets.TryGetValue(assetKey ?? string.Empty, out asset);
    }

    /// <summary>按稳定 ID 查找已启用的数据驱动角色。</summary>
    public bool TryGetCharacter(string characterId, out CharacterDefinition character) =>
        characters.TryGetValue(characterId ?? string.Empty, out character);

    /// <summary>获取已启用角色；运行时引用损坏时抛出可定位错误。</summary>
    public CharacterDefinition GetCharacter(string characterId)
    {
        if (!TryGetCharacter(characterId, out CharacterDefinition character))
            throw new KeyNotFoundException($"内容包中不存在启用角色：{characterId}");
        return character;
    }

    /// <summary>按稳定 ID 查找已启用装备定义。</summary>
    public bool TryGetEquipment(string equipmentId, out EquipmentDefinition definition) =>
        equipment.TryGetValue(equipmentId ?? string.Empty, out definition);

    /// <summary>
    /// 构建一份定义索引，同时校验空值、类型、稳定 ID 格式和重复项。
    /// 停用定义仍会校验，但不会进入运行时查找表。
    /// </summary>
    /// <typeparam name="TDefinition">具体的共享定义类型。</typeparam>
    /// <param name="definitions">该内容集合的全部定义。</param>
    /// <param name="isEnabled">决定有效定义是否暴露给运行时的谓词。</param>
    /// <param name="expectedKind">该集合期望的定义类型。</param>
    /// <returns>按序号比较的稳定 ID 索引，只含启用定义。</returns>
    private static Dictionary<string, TDefinition> BuildIndex<TDefinition>(
        IEnumerable<TDefinition> definitions,
        Func<TDefinition, bool> isEnabled,
        string expectedKind)
        where TDefinition : class, IContentDefinition
    {
        if (definitions == null)
        {
            throw new InvalidDataException($"内容包缺少 {expectedKind} 定义集合。");
        }

        Dictionary<string, TDefinition> result =
            new Dictionary<string, TDefinition>(StringComparer.Ordinal);
        HashSet<string> allIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (TDefinition definition in definitions)
        {
            if (definition == null)
            {
                throw new InvalidDataException($"内容包的 {expectedKind} 集合包含空定义。");
            }

            string id = definition.GetDefinitionId();
            if (definition.GetDefinitionKind() != expectedKind || !ContentId.IsValid(id))
            {
                throw new InvalidDataException($"内容定义类型或稳定 ID 无效：{expectedKind}:{id}。");
            }
            if (!allIds.Add(id))
            {
                throw new InvalidDataException($"内容包包含重复定义：{expectedKind}:{id}。");
            }
            if (isEnabled(definition))
            {
                result.Add(id, definition);
            }
        }

        return result;
    }
}
}
