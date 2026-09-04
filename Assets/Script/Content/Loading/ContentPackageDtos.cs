using System;
using System.Collections.Generic;
using LegendsOfFurry.Content.Contracts;

#pragma warning disable 0649 // Unity JsonUtility 会通过反射为这些序列化字段赋值。

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>
/// 提供与发布器 camelCase JSON 一一对应的 Unity JsonUtility 字段模型，并映射到共享内容合同。
/// </summary>
[Serializable]
internal sealed class ContentPackageDto
{
    public int schemaVersion;
    public string contentVersion;
    public string layout;
    public ContentCatalogPartRefDto[] parts;
    public CardDefinitionDto[] cards;
    public CardPoolDefinitionDto[] cardPools;
    public StatusDefinitionDto[] statuses;
    public DeckDefinitionDto[] decks;
    public RarityDefinitionDto[] rarities;
    public AssetDefinitionDto[] assets;
    public ClassProfileDefinitionDto[] classProfiles;
    public UnitDefinitionDto[] units;
    public UnitDefinitionDto[] characters;
    public AiProfileDefinitionDto[] aiProfiles;
    public EquipmentDefinitionDto[] equipment;
    public GameSettingsDefinitionDto gameSettings;

    /// <summary>
    /// 将 Unity 字段 DTO 转换为工具和运行时共同使用的纯 C# 内容合同。
    /// </summary>
    /// <returns>包含所有已发布记录的内容包。</returns>
    public ContentPackage ToContract()
    {
        ContentPackage package = new ContentPackage
        {
            SchemaVersion = schemaVersion,
            ContentVersion = contentVersion ?? string.Empty
        };
        ConvertItems(cards, package.Cards, item => item.ToContract());
        ConvertItems(cardPools, package.CardPools, item => item.ToContract());
        ConvertItems(statuses, package.Statuses, item => item.ToContract());
        ConvertItems(decks, package.Decks, item => item.ToContract());
        ConvertItems(rarities, package.Rarities, item => item.ToContract());
        ConvertItems(assets, package.Assets, item => item.ToContract());
        ConvertItems(classProfiles, package.ClassProfiles, item => item.ToContract());
        ConvertItems(units ?? characters, package.Units, item => item.ToContract());
        ConvertItems(aiProfiles, package.AiProfiles, item => item.ToContract());
        ConvertItems(equipment, package.Equipment, item => item.ToContract());
        package.GameSettings = gameSettings == null ? new GameSettingsDefinition() : gameSettings.ToContract();
        return package;
    }

    /// <summary>
    /// 将可能为空的 DTO 数组逐项映射到目标合同列表。
    /// </summary>
    /// <typeparam name="TSource">Unity 字段 DTO 类型。</typeparam>
    /// <typeparam name="TTarget">共享内容合同类型。</typeparam>
    /// <param name="source">JsonUtility 填充的可选数组。</param>
    /// <param name="target">需要追加结果的合同列表。</param>
    /// <param name="converter">单项 DTO 映射函数。</param>
    private static void ConvertItems<TSource, TTarget>(TSource[] source, ICollection<TTarget> target, Func<TSource, TTarget> converter)
    {
        if (source == null)
        {
            return;
        }

        foreach (TSource item in source)
        {
            if (item != null)
            {
                target.Add(converter(item));
            }
        }
    }
}

/// <summary>表示发布 JSON 中的一张卡牌。</summary>
[Serializable]
internal sealed class CardDefinitionDto
{
    public string cardId;
    public string displayName;
    public string description;
    public string artworkKey;
    public string rarityId;
    public string familyId;
    public bool isAttack;
    public bool exhaustOnPlay;
    public bool temporary;
    public bool curse;
    public bool unplayable;
    public bool enabled;
    public int sortOrder;
    public float aiBaseScore;
    public CardCostDefinitionDto cost;
    public CardTargetRuleDto target;
    public string[] tags;
    public CardPoolMembershipDto[] pools;
    public BehaviorDefinitionDto[] behaviors;

    /// <summary>
    /// 将发布字段映射为一张完整卡牌合同。
    /// </summary>
    /// <returns>可注册到 CardRegistry 的卡牌定义。</returns>
    public CardDefinition ToContract()
    {
        CardDefinition card = new CardDefinition
        {
            CardId = cardId ?? string.Empty,
            DisplayName = displayName ?? string.Empty,
            Description = description ?? string.Empty,
            ArtworkKey = artworkKey,
            RarityId = rarityId ?? string.Empty,
            FamilyId = familyId ?? string.Empty,
            IsAttack = isAttack,
            ExhaustOnPlay = exhaustOnPlay,
            Temporary = temporary,
            Curse = curse,
            Unplayable = unplayable,
            Enabled = enabled,
            SortOrder = sortOrder,
            AiBaseScore = aiBaseScore,
            Cost = cost == null ? new CardCostDefinition() : cost.ToContract(),
            Target = target == null ? new CardTargetRule() : target.ToContract()
        };
        if (tags != null)
        {
            card.Tags.AddRange(tags);
        }
        if (pools != null)
        {
            foreach (CardPoolMembershipDto pool in pools)
            {
                if (pool != null) card.Pools.Add(pool.ToContract());
            }
        }
        if (behaviors != null)
        {
            foreach (BehaviorDefinitionDto behavior in behaviors)
            {
                if (behavior != null) card.Behaviors.Add(behavior.ToContract());
            }
        }
        return card;
    }
}

/// <summary>表示发布 JSON 中的卡牌费用。</summary>
[Serializable]
internal sealed class CardCostDefinitionDto
{
    public int actionCost;
    public int manaCost;
    public bool spendAllAction;
    public bool spendAllMana;

    /// <summary>将费用字段映射为共享合同。</summary>
    /// <returns>结构化卡牌费用。</returns>
    public CardCostDefinition ToContract()
    {
        return new CardCostDefinition
        {
            ActionCost = actionCost,
            ManaCost = manaCost,
            SpendAllAction = spendAllAction,
            SpendAllMana = spendAllMana
        };
    }
}

/// <summary>表示发布 JSON 中的卡牌主目标规则。</summary>
[Serializable]
internal sealed class CardTargetRuleDto
{
    public string selectionMode;
    public int range;
    public string teamFilter;
    public string lifeStateFilter;
    public bool requiresLineOfSight;
    public bool allowSelf;

    /// <summary>将目标字段映射为共享合同。</summary>
    /// <returns>结构化主目标规则。</returns>
    public CardTargetRule ToContract()
    {
        return new CardTargetRule
        {
            SelectionMode = selectionMode ?? "self",
            Range = range,
            TeamFilter = teamFilter ?? "any",
            LifeStateFilter = lifeStateFilter ?? "alive",
            RequiresLineOfSight = requiresLineOfSight,
            AllowSelf = allowSelf
        };
    }
}

/// <summary>表示发布 JSON 中的一条卡池成员关系。</summary>
[Serializable]
internal sealed class CardPoolMembershipDto
{
    public string poolId;
    public float weight;
    public int sortOrder;

    /// <summary>将卡池成员字段映射为共享合同。</summary>
    /// <returns>带权重的卡池成员关系。</returns>
    public CardPoolMembership ToContract()
    {
        return new CardPoolMembership { PoolId = poolId ?? string.Empty, Weight = (decimal)weight, SortOrder = sortOrder };
    }
}

/// <summary>表示发布 JSON 中的一个行为图。</summary>
[Serializable]
internal sealed class BehaviorDefinitionDto
{
    public string behaviorId;
    public string ownerKind;
    public string ownerId;
    public string triggerKey;
    public int priority;
    public bool enabled;
    public BehaviorNodeDefinitionDto[] nodes;

    /// <summary>将行为和节点字段映射为共享合同。</summary>
    /// <returns>保持节点顺序和 JSON 参数的行为图。</returns>
    public BehaviorDefinition ToContract()
    {
        BehaviorDefinition behavior = new BehaviorDefinition
        {
            BehaviorId = behaviorId ?? string.Empty,
            OwnerKind = ownerKind ?? string.Empty,
            OwnerId = ownerId ?? string.Empty,
            TriggerKey = triggerKey ?? string.Empty,
            Priority = priority,
            Enabled = enabled
        };
        if (nodes != null)
        {
            foreach (BehaviorNodeDefinitionDto node in nodes)
            {
                if (node != null) behavior.Nodes.Add(node.ToContract());
            }
        }
        return behavior;
    }
}

/// <summary>表示发布 JSON 中的一个行为节点。</summary>
[Serializable]
internal sealed class BehaviorNodeDefinitionDto
{
    public string nodeId;
    public string parentNodeId;
    public string branchKey;
    public int sortOrder;
    public string nodeKind;
    public string operationKey;
    public string parametersJson;

    /// <summary>将行为节点字段映射为共享合同。</summary>
    /// <returns>包含原始受控参数 JSON 的行为节点。</returns>
    public BehaviorNodeDefinition ToContract()
    {
        return new BehaviorNodeDefinition
        {
            NodeId = nodeId ?? string.Empty,
            ParentNodeId = string.IsNullOrEmpty(parentNodeId) ? null : parentNodeId,
            BranchKey = branchKey ?? "children",
            SortOrder = sortOrder,
            NodeKind = nodeKind ?? string.Empty,
            OperationKey = operationKey ?? string.Empty,
            ParametersJson = parametersJson ?? "{}"
        };
    }
}

/// <summary>表示发布 JSON 中的卡池。</summary>
[Serializable]
internal sealed class CardPoolDefinitionDto
{
    public string poolId;
    public string displayName;
    public bool enabled;
    public int sortOrder;

    /// <summary>将卡池字段映射为共享合同。</summary>
    /// <returns>卡池定义。</returns>
    public CardPoolDefinition ToContract()
    {
        return new CardPoolDefinition { PoolId = poolId ?? string.Empty, DisplayName = displayName ?? string.Empty, Enabled = enabled, SortOrder = sortOrder };
    }
}

/// <summary>表示发布 JSON 中的状态。</summary>
[Serializable]
internal sealed class StatusDefinitionDto
{
    public string statusId;
    public string displayName;
    public string description;
    public string category;
    public int maximumStacks;
    public string stackingPolicy;
    public string durationPolicy;
    public bool enabled;
    public BehaviorDefinitionDto[] behaviors;

    /// <summary>将状态字段和状态行为映射为共享合同。</summary>
    /// <returns>完整状态定义。</returns>
    public StatusDefinition ToContract()
    {
        StatusDefinition status = new StatusDefinition
        {
            StatusId = statusId ?? string.Empty,
            DisplayName = displayName ?? string.Empty,
            Description = description ?? string.Empty,
            Category = category ?? "neutral",
            MaximumStacks = maximumStacks,
            StackingPolicy = stackingPolicy ?? "add",
            DurationPolicy = durationPolicy ?? "none",
            Enabled = enabled
        };
        if (behaviors != null)
        {
            foreach (BehaviorDefinitionDto behavior in behaviors)
            {
                if (behavior != null) status.Behaviors.Add(behavior.ToContract());
            }
        }
        return status;
    }
}

/// <summary>表示发布 JSON 中的固定牌库。</summary>
[Serializable]
internal sealed class DeckDefinitionDto
{
    public string deckId;
    public string displayName;
    public bool isTestDeck;
    public bool enabled;
    public DeckEntryDefinitionDto[] entries;

    /// <summary>将牌库及条目字段映射为共享合同。</summary>
    /// <returns>固定牌库定义。</returns>
    public DeckDefinition ToContract()
    {
        DeckDefinition deck = new DeckDefinition
        {
            DeckId = deckId ?? string.Empty,
            DisplayName = displayName ?? string.Empty,
            IsTestDeck = isTestDeck,
            Enabled = enabled
        };
        if (entries != null)
        {
            foreach (DeckEntryDefinitionDto entry in entries)
            {
                if (entry != null) deck.Entries.Add(entry.ToContract());
            }
        }
        return deck;
    }
}

/// <summary>表示发布 JSON 中的一条固定牌库记录。</summary>
[Serializable]
internal sealed class DeckEntryDefinitionDto
{
    public string cardId;
    public int amount;
    public int sortOrder;

    /// <summary>将牌库条目字段映射为共享合同。</summary>
    /// <returns>固定牌库条目。</returns>
    public DeckEntryDefinition ToContract()
    {
        return new DeckEntryDefinition { CardId = cardId ?? string.Empty, Amount = amount, SortOrder = sortOrder };
    }
}

/// <summary>表示发布 JSON 中的稀有度。</summary>
[Serializable]
internal sealed class RarityDefinitionDto
{
    public string rarityId;
    public string displayName;
    public string colorHex;
    public float defaultWeight;
    public int sortOrder;

    /// <summary>将稀有度字段映射为共享合同。</summary>
    /// <returns>稀有度定义。</returns>
    public RarityDefinition ToContract()
    {
        return new RarityDefinition
        {
            RarityId = rarityId ?? string.Empty,
            DisplayName = displayName ?? string.Empty,
            ColorHex = colorHex ?? "#FFFFFFFF",
            DefaultWeight = (decimal)defaultWeight,
            SortOrder = sortOrder
        };
    }
}

/// <summary>表示发布 JSON 中的受管资源。</summary>
[Serializable]
internal sealed class AssetDefinitionDto
{
    public string assetKey;
    public string assetKind;
    public string relativePath;
    public string sha256;

    /// <summary>将资源字段映射为共享合同。</summary>
    /// <returns>受管资源定义。</returns>
    public AssetDefinition ToContract()
    {
        return new AssetDefinition
        {
            AssetKey = assetKey ?? string.Empty,
            AssetKind = assetKind ?? string.Empty,
            RelativePath = relativePath ?? string.Empty,
            Sha256 = sha256
        };
    }
}

/// <summary>表示发布 JSON 中的职业资料。</summary>
[Serializable]
internal sealed class ClassProfileDefinitionDto
{
    public string classId;
    public string displayName;
    public string description;
    public int initialHealth;
    public int initialMana;
    public int maximumMana;
    public bool enabled;
    public int sortOrder;
    public DeckRecipePoolDefinitionDto[] deckRecipe;
    public BehaviorDefinitionDto[] behaviors;
    public ClassTraitDefinitionDto[] traits;

    /// <summary>将职业字段、牌库配方和被动行为映射到共享合同。</summary>
    public ClassProfileDefinition ToContract()
    {
        ClassProfileDefinition profile = new ClassProfileDefinition
        {
            ClassId = classId ?? string.Empty, DisplayName = displayName ?? string.Empty,
            Description = description ?? string.Empty, InitialHealth = initialHealth,
            InitialMana = initialMana, MaximumMana = maximumMana, Enabled = enabled, SortOrder = sortOrder
        };
        if (deckRecipe != null) foreach (DeckRecipePoolDefinitionDto item in deckRecipe)
            if (item != null) profile.DeckRecipe.Add(item.ToContract());
        if (behaviors != null) foreach (BehaviorDefinitionDto item in behaviors)
            if (item != null) profile.Behaviors.Add(item.ToContract());
        if (traits != null) foreach (ClassTraitDefinitionDto item in traits)
            if (item != null) profile.Traits.Add(item.ToContract());
        return profile;
    }
}

/// <summary>表示职业起始牌库中的一条卡池配方。</summary>
[Serializable]
internal sealed class DeckRecipePoolDefinitionDto
{
    public string poolId;
    public int amount;
    public string fixedCardId;
    public int sortOrder;

    /// <summary>将牌库配方字段映射到共享合同。</summary>
    public DeckRecipePoolDefinition ToContract() => new DeckRecipePoolDefinition
    {
        PoolId = poolId ?? string.Empty, Amount = amount,
        FixedCardId = fixedCardId ?? string.Empty, SortOrder = sortOrder
    };
}

/// <summary>表示发布 JSON 中的一条职业特性。</summary>
[Serializable]
internal sealed class ClassTraitDefinitionDto
{
    public string key;
    public string value;

    /// <summary>将职业特性字段映射到共享合同。</summary>
    public ClassTraitDefinition ToContract() => new ClassTraitDefinition
    {
        Key = key ?? string.Empty, Value = value ?? string.Empty
    };
}

/// <summary>表示发布 JSON 中的基础战斗参数。</summary>
[Serializable]
internal sealed class GameSettingsDefinitionDto
{
    public int handLimit;
    public int startingHandSize;
    public int drawPerTurn;
    public int baseActionPoints;
    public int baseMoveSteps;
    public string playerCharacterId;
    public string enemyCharacterId;
    public string playerUnitId;
    public string defaultWorldId;

    /// <summary>将基础战斗参数映射到共享合同。</summary>
    public GameSettingsDefinition ToContract() => new GameSettingsDefinition
    {
        HandLimit = handLimit, StartingHandSize = startingHandSize, DrawPerTurn = drawPerTurn,
        BaseActionPoints = baseActionPoints, BaseMoveSteps = baseMoveSteps,
        PlayerUnitId = string.IsNullOrWhiteSpace(playerUnitId) ? playerCharacterId ?? string.Empty : playerUnitId,
        DefaultWorldId = string.IsNullOrWhiteSpace(defaultWorldId) ? "demo" : defaultWorldId
    };
}

/// <summary>发布 JSON 中的数据驱动战斗单位。</summary>
[Serializable]
internal sealed class UnitDefinitionDto
{
    public string unitId;
    public string characterId;
    public string displayName;
    public string description;
    public int initialHealth;
    public int baseDamage;
    public int moveSteps;
    public string tokenFrameColor;
    public string portraitKey;
    public string unitKind;
    public string defaultFaction;
    public string controller;
    public bool isBoss;
    public bool recruitable;
    public bool canJoinParty;
    public int initialActionPoints;
    public int startingHandSize;
    public int drawPerTurn;
    public string deckId;
    public string aiProfileId;
    public UnitAiTuningDefinitionDto aiOverrides;
    public BossPhaseDefinitionDto[] bossPhases;
    public string[] capabilities;
    public bool enabled;
    public int sortOrder;
    public string[] tags;
    public BehaviorDefinitionDto[] behaviors;

    public UnitDefinition ToContract()
    {
        UnitDefinition definition = new UnitDefinition
        {
            UnitId = string.IsNullOrWhiteSpace(unitId) ? characterId ?? string.Empty : unitId,
            DisplayName = displayName ?? string.Empty,
            Description = description ?? string.Empty, InitialHealth = initialHealth,
            BaseDamage = baseDamage, MoveSteps = moveSteps,
            UnitKind = string.IsNullOrWhiteSpace(unitKind) ? "character" : unitKind,
            DefaultFaction = string.IsNullOrWhiteSpace(defaultFaction) ? "neutral" : defaultFaction,
            Controller = string.IsNullOrWhiteSpace(controller) ? "player" : controller,
            IsBoss = isBoss, Recruitable = recruitable, CanJoinParty = canJoinParty,
            InitialActionPoints = initialActionPoints > 0 ? initialActionPoints : 3,
            StartingHandSize = startingHandSize > 0 ? startingHandSize : 5,
            DrawPerTurn = drawPerTurn > 0 ? drawPerTurn : 5,
            DeckId = deckId ?? string.Empty, AiProfileId = aiProfileId ?? string.Empty,
            AiOverrides = aiOverrides == null ? new UnitAiTuningDefinition() : aiOverrides.ToContract(),
            TokenFrameColor = tokenFrameColor ?? string.Empty, PortraitKey = portraitKey ?? string.Empty,
            Enabled = enabled, SortOrder = sortOrder
        };
        if (tags != null) definition.Tags.AddRange(tags);
        if (capabilities != null) definition.Capabilities.AddRange(capabilities);
        if (bossPhases != null) foreach (BossPhaseDefinitionDto phase in bossPhases)
            if (phase != null) definition.BossPhases.Add(phase.ToContract());
        if (behaviors != null) foreach (BehaviorDefinitionDto behavior in behaviors)
            if (behavior != null) definition.Behaviors.Add(behavior.ToContract());
        return definition;
    }
}

[Serializable]
internal sealed class UnitAiTuningDefinitionDto
{
    public float attackWeight = float.NaN;
    public float defenseWeight = float.NaN;
    public float healingWeight = float.NaN;
    public float approachWeight = float.NaN;
    public float retreatWeight = float.NaN;
    public float killWeight = float.NaN;
    public float preferredRange = float.NaN;
    public float lowHealthThreshold = float.NaN;

    public UnitAiTuningDefinition ToContract() => new UnitAiTuningDefinition
    {
        AttackWeight = attackWeight, DefenseWeight = defenseWeight, HealingWeight = healingWeight,
        ApproachWeight = approachWeight, RetreatWeight = retreatWeight, KillWeight = killWeight,
        PreferredRange = preferredRange, LowHealthThreshold = lowHealthThreshold
    };
}

[Serializable]
internal sealed class BossPhaseDefinitionDto
{
    public float maximumHealthRatio = 1f;
    public string aiProfileId;
    public UnitAiTuningDefinitionDto aiOverrides;

    public BossPhaseDefinition ToContract() => new BossPhaseDefinition
    {
        MaximumHealthRatio = maximumHealthRatio,
        AiProfileId = aiProfileId ?? string.Empty,
        AiOverrides = aiOverrides == null ? new UnitAiTuningDefinition() : aiOverrides.ToContract()
    };
}

[Serializable]
internal sealed class AiProfileDefinitionDto
{
    public string aiProfileId;
    public string displayName;
    public string description;
    public float attackWeight;
    public float defenseWeight;
    public float healingWeight;
    public float approachWeight;
    public float retreatWeight;
    public float killWeight;
    public float preferredRange;
    public float lowHealthThreshold;
    public bool enabled;
    public int sortOrder;

    public AiProfileDefinition ToContract() => new AiProfileDefinition
    {
        AiProfileId = aiProfileId ?? string.Empty, DisplayName = displayName ?? string.Empty,
        Description = description ?? string.Empty, AttackWeight = attackWeight,
        DefenseWeight = defenseWeight, HealingWeight = healingWeight, ApproachWeight = approachWeight,
        RetreatWeight = retreatWeight, KillWeight = killWeight, PreferredRange = preferredRange,
        LowHealthThreshold = lowHealthThreshold, Enabled = enabled, SortOrder = sortOrder
    };
}

/// <summary>发布 JSON 中的一件可装备内容。</summary>
[Serializable]
internal sealed class EquipmentDefinitionDto
{
    public string equipmentId;
    public string displayName;
    public string description;
    public string slotKey;
    public string cardPoolId;
    public bool enabled;
    public int sortOrder;
    public string[] tags;
    public BehaviorDefinitionDto[] behaviors;

    public EquipmentDefinition ToContract()
    {
        EquipmentDefinition definition = new EquipmentDefinition
        {
            EquipmentId = equipmentId ?? string.Empty, DisplayName = displayName ?? string.Empty,
            Description = description ?? string.Empty, SlotKey = slotKey ?? string.Empty,
            CardPoolId = cardPoolId ?? string.Empty, Enabled = enabled, SortOrder = sortOrder
        };
        if (tags != null) definition.Tags.AddRange(tags);
        if (behaviors != null) foreach (BehaviorDefinitionDto behavior in behaviors)
            if (behavior != null) definition.Behaviors.Add(behavior.ToContract());
        return definition;
    }
}

/// <summary>表示 current.json 中当前内容版本和 manifest 路径。</summary>
[Serializable]
internal sealed class CurrentContentPointerDto
{
    public string contentVersion;
    public string manifestPath;
}

/// <summary>表示 manifest.json 中目录文件及其校验信息。</summary>
[Serializable]
internal sealed class ContentManifestDto
{
    public int schemaVersion;
    public string contentVersion;
    public string generatedAtUtc;
    public string catalogFile;
    public string catalogSha256;
    public int cardCount;
    public int statusCount;
    public int deckCount;
    public int unitCount;
    public int aiProfileCount;
    // schema 2 兼容字段。
    public int characterCount;
    public int equipmentCount;
}

/// <summary>分文件 catalog 索引中的一个切片声明。</summary>
[Serializable]
internal sealed class ContentCatalogPartRefDto
{
    public string kind;
    public string file;
    public string sha256;
}

/// <summary>与受哈希保护的 catalog 放在同一目录的实体外部包清单。</summary>
[Serializable]
internal sealed class ContentPackManifestDto
{
    public int formatVersion;
    public string packId;
    public string packVersion;
    public int schemaVersion;
    public int loadOrder;
    public string[] dependencies;
    public ContentPackDependencyDefinitionDto[] dependencyVersions;
    public ContentOverrideDefinitionDto[] overrides;
    public string catalogFile;
    public string catalogSha256;
}

/// <summary>某个包依赖的可选语义化版本上下界。</summary>
[Serializable]
internal sealed class ContentPackDependencyDefinitionDto
{
    public string packId;
    public string minimumVersion;
    public string maximumVersionExclusive;

    public ContentPackDependencyDefinition ToContract() => new ContentPackDependencyDefinition
    {
        PackId = packId ?? string.Empty,
        MinimumVersion = minimumVersion ?? string.Empty,
        MaximumVersionExclusive = maximumVersionExclusive ?? string.Empty
    };
}

/// <summary>针对某一类型和稳定 ID 的显式替换许可。</summary>
[Serializable]
internal sealed class ContentOverrideDefinitionDto
{
    public string definitionKind;
    public string definitionId;

    public ContentOverrideDefinition ToContract() => new ContentOverrideDefinition
    {
        DefinitionKind = definitionKind ?? string.Empty,
        DefinitionId = definitionId ?? string.Empty
    };
}
}

#pragma warning restore 0649
