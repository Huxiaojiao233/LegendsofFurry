using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LegendsOfFurry.Content.Contracts;

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>Deterministically composes a base snapshot with dependency-ordered external content layers.</summary>
public static class ContentPackageComposer
{
    /// <summary>
    /// Merges layers by load order. Stable-ID collisions are rejected unless the incoming pack explicitly declares
    /// the exact kind and ID in its override list.
    /// </summary>
    public static ContentPackage Compose(ContentPackage basePackage, IEnumerable<ContentPackDefinition> packs)
    {
        if (basePackage == null) throw new ArgumentNullException(nameof(basePackage));
        ContentPackage result = CopyPackage(basePackage);
        HashSet<string> loadedPacks = new HashSet<string>(StringComparer.Ordinal);
        foreach (ContentPackDefinition pack in OrderPacks(packs))
        {
            ValidatePack(pack, loadedPacks);
            HashSet<string> overrides = pack.Overrides.Select(ToOverrideKey).ToHashSet(StringComparer.Ordinal);
            Merge(result.Cards, pack.Content.Cards, ContentDefinitionKinds.Card, overrides);
            Merge(result.CardPools, pack.Content.CardPools, ContentDefinitionKinds.CardPool, overrides);
            Merge(result.Statuses, pack.Content.Statuses, ContentDefinitionKinds.Status, overrides);
            Merge(result.Decks, pack.Content.Decks, ContentDefinitionKinds.Deck, overrides);
            Merge(result.Rarities, pack.Content.Rarities, ContentDefinitionKinds.Rarity, overrides);
            Merge(result.Assets, pack.Content.Assets, ContentDefinitionKinds.Asset, overrides);
            Merge(result.ClassProfiles, pack.Content.ClassProfiles, ContentDefinitionKinds.Class, overrides);
            MergeCharacters(result.Characters, pack.Content.Characters, overrides);
            Merge(result.Equipment, pack.Content.Equipment, ContentDefinitionKinds.Equipment, overrides);
            if (pack.Content.GameSettings != null && pack.Content.GameSettings.HandLimit > 0)
                result.GameSettings = OverlayGameSettings(result.GameSettings, pack.Content.GameSettings);
            loadedPacks.Add(pack.PackId);
        }
        return result;
    }

    /// <summary>Returns a dependency-safe deterministic order, using load order and ID only among ready packs.</summary>
    public static IReadOnlyList<ContentPackDefinition> OrderPacks(IEnumerable<ContentPackDefinition> packs)
    {
        List<ContentPackDefinition> remaining = (packs ?? Array.Empty<ContentPackDefinition>()).ToList();
        Dictionary<string, ContentPackDefinition> byId = new Dictionary<string, ContentPackDefinition>(StringComparer.Ordinal);
        foreach (ContentPackDefinition pack in remaining)
        {
            if (pack == null || !ContentId.IsValid(pack.PackId) || !byId.TryAdd(pack.PackId, pack))
                throw new InvalidDataException($"扩展包 ID 无效或重复：{pack?.PackId}。");
        }
        foreach (ContentPackDefinition pack in remaining)
            foreach (string dependency in pack.Dependencies)
                if (!byId.ContainsKey(dependency))
                    throw new InvalidDataException($"扩展包 {pack.PackId} 缺少依赖：{dependency}。");

        List<ContentPackDefinition> ordered = new List<ContentPackDefinition>();
        HashSet<string> loaded = new HashSet<string>(StringComparer.Ordinal);
        while (remaining.Count > 0)
        {
            ContentPackDefinition next = remaining
                .Where(pack => pack.Dependencies.All(loaded.Contains))
                .OrderBy(pack => pack.LoadOrder)
                .ThenBy(pack => pack.PackId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (next == null)
                throw new InvalidDataException("扩展包依赖包含循环。" );
            remaining.Remove(next);
            ordered.Add(next);
            loaded.Add(next.PackId);
        }
        return ordered;
    }

    private static void ValidatePack(ContentPackDefinition pack, ISet<string> loadedPacks)
    {
        if (pack == null || !ContentId.IsValid(pack.PackId))
            throw new InvalidDataException("扩展包 ID 无效。");
        if (loadedPacks.Contains(pack.PackId))
            throw new InvalidDataException($"扩展包 ID 重复：{pack.PackId}。");
        foreach (string dependency in pack.Dependencies)
            if (!loadedPacks.Contains(dependency))
                throw new InvalidDataException($"扩展包 {pack.PackId} 缺少已加载依赖：{dependency}。");
        if (pack.Content == null) throw new InvalidDataException($"扩展包 {pack.PackId} 没有内容快照。");
    }

    private static string ToOverrideKey(ContentOverrideDefinition value)
    {
        if (value == null || !ContentDefinitionKinds.IsKnown(value.DefinitionKind) ||
            !ContentId.IsValid(value.DefinitionId))
            throw new InvalidDataException("扩展包覆盖声明的定义种类或稳定 ID 无效。");
        return value.DefinitionKind + ":" + value.DefinitionId;
    }

    private static void Merge<TDefinition>(
        IList<TDefinition> target,
        IEnumerable<TDefinition> incoming,
        string kind,
        ISet<string> overrides)
        where TDefinition : class, IContentDefinition
    {
        Dictionary<string, int> indexes = target.Select((item, index) => (item, index))
            .ToDictionary(pair => pair.item.GetDefinitionId(), pair => pair.index, StringComparer.Ordinal);
        foreach (TDefinition definition in incoming ?? Array.Empty<TDefinition>())
        {
            string id = ContentId.Require(definition.GetDefinitionId(), nameof(incoming));
            if (!indexes.TryGetValue(id, out int index))
            {
                indexes.Add(id, target.Count);
                target.Add(definition);
                continue;
            }
            if (!overrides.Contains(kind + ":" + id))
                throw new InvalidDataException($"扩展包发生未声明的内容冲突：{kind}:{id}。");
            target[index] = definition;
        }
    }

    /// <summary>覆盖角色时保留未填写的棋子贴图和外框色，避免后加载包把外观字段抹成空。</summary>
    private static void MergeCharacters(
        IList<CharacterDefinition> target,
        IEnumerable<CharacterDefinition> incoming,
        ISet<string> overrides)
    {
        Dictionary<string, int> indexes = target.Select((item, index) => (item, index))
            .ToDictionary(pair => pair.item.GetDefinitionId(), pair => pair.index, StringComparer.Ordinal);
        foreach (CharacterDefinition definition in incoming ?? Array.Empty<CharacterDefinition>())
        {
            string id = ContentId.Require(definition.GetDefinitionId(), nameof(incoming));
            if (!indexes.TryGetValue(id, out int index))
            {
                indexes.Add(id, target.Count);
                target.Add(definition);
                continue;
            }
            if (!overrides.Contains(ContentDefinitionKinds.Character + ":" + id))
                throw new InvalidDataException($"扩展包发生未声明的内容冲突：character:{id}。");
            target[index] = OverlayCharacter(target[index], definition);
        }
    }

    private static CharacterDefinition OverlayCharacter(CharacterDefinition current, CharacterDefinition incoming)
    {
        if (current == null) return incoming;
        if (incoming == null) return current;
        incoming.PortraitKey = FirstNonEmpty(incoming.PortraitKey, current.PortraitKey);
        incoming.TokenFrameColor = FirstNonEmpty(incoming.TokenFrameColor, current.TokenFrameColor);
        if (string.IsNullOrWhiteSpace(incoming.DisplayName)) incoming.DisplayName = current.DisplayName;
        if (string.IsNullOrWhiteSpace(incoming.Description)) incoming.Description = current.Description;
        return incoming;
    }

    private static ContentPackage CopyPackage(ContentPackage source) => new ContentPackage
    {
        SchemaVersion = source.SchemaVersion,
        ContentVersion = source.ContentVersion,
        Cards = new List<CardDefinition>(source.Cards),
        CardPools = new List<CardPoolDefinition>(source.CardPools),
        Statuses = new List<StatusDefinition>(source.Statuses),
        Decks = new List<DeckDefinition>(source.Decks),
        Rarities = new List<RarityDefinition>(source.Rarities),
        Assets = new List<AssetDefinition>(source.Assets),
        ClassProfiles = new List<ClassProfileDefinition>(source.ClassProfiles),
        Characters = new List<CharacterDefinition>(source.Characters),
        Equipment = new List<EquipmentDefinition>(source.Equipment),
        GameSettings = source.GameSettings
    };

    /// <summary>后加载的包只覆盖已填写的战斗参数；空角色 ID 不得清掉前一层编制。</summary>
    private static GameSettingsDefinition OverlayGameSettings(GameSettingsDefinition current, GameSettingsDefinition incoming)
    {
        current ??= new GameSettingsDefinition();
        incoming ??= new GameSettingsDefinition();
        return new GameSettingsDefinition
        {
            HandLimit = incoming.HandLimit > 0 ? incoming.HandLimit : current.HandLimit,
            StartingHandSize = incoming.StartingHandSize > 0 ? incoming.StartingHandSize : current.StartingHandSize,
            DrawPerTurn = incoming.DrawPerTurn > 0 ? incoming.DrawPerTurn : current.DrawPerTurn,
            BaseActionPoints = incoming.BaseActionPoints > 0 ? incoming.BaseActionPoints : current.BaseActionPoints,
            BaseMoveSteps = incoming.BaseMoveSteps > 0 ? incoming.BaseMoveSteps : current.BaseMoveSteps,
            PlayerCharacterId = FirstNonEmpty(incoming.PlayerCharacterId, current.PlayerCharacterId),
            EnemyCharacterId = FirstNonEmpty(incoming.EnemyCharacterId, current.EnemyCharacterId)
        };
    }

    private static string FirstNonEmpty(string preferred, string fallback)
    {
        return string.IsNullOrWhiteSpace(preferred) ? fallback ?? string.Empty : preferred;
    }
}
}
