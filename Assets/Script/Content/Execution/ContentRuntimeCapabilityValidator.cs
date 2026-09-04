using System;
using System.IO;
using System.Linq;
using LegendsOfFurry.Content.Contracts;

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>
/// 在建立运行时注册表前验证已启用卡牌只引用当前版本真正可执行的 Trigger 和行为图能力。
/// </summary>
public static class ContentRuntimeCapabilityValidator
{
    /// <summary>
    /// 验证内容包中全部启用卡牌行为；发现未知或尚未实现能力时抛出带卡牌和行为 ID 的错误。
    /// </summary>
    /// <param name="package">完成文件完整性验证后的内容包。</param>
    /// <exception cref="InvalidDataException">内容引用当前运行时不能执行的能力时抛出。</exception>
    public static void ValidateOrThrow(ContentPackage package)
    {
        if (package == null)
        {
            throw new ArgumentNullException(nameof(package));
        }
        ValidateReferences(package);
        ContentBehaviorRuntime runtime = new ContentBehaviorRuntime();
        foreach (CardDefinition card in package.Cards)
        {
            ValidateOwnerBehaviors(runtime, "卡牌", card, card?.Enabled == true, card?.Behaviors,
                ContentCapabilityCatalog.Phase5ExecutableTriggerKeys);
        }
        foreach (StatusDefinition status in package.Statuses)
        {
            ValidateOwnerBehaviors(runtime, "状态", status, status?.Enabled == true, status?.Behaviors,
                ActorTriggerKeys().Append(ContentTriggerKeys.OnStatusChanged).Append(ContentTriggerKeys.OnStatusGained));
        }
        foreach (ClassProfileDefinition profile in package.ClassProfiles)
        {
            ValidateOwnerBehaviors(runtime, "职业", profile, profile?.Enabled == true, profile?.Behaviors,
                ActorTriggerKeys());
        }
        foreach (UnitDefinition unit in package.Units)
            ValidateOwnerBehaviors(runtime, "单位", unit, unit?.Enabled == true, unit?.Behaviors,
                ActorTriggerKeys());
        foreach (EquipmentDefinition item in package.Equipment)
            ValidateOwnerBehaviors(runtime, "装备", item, item?.Enabled == true, item?.Behaviors,
                ActorTriggerKeys());
        if (package.GameSettings.HandLimit <= 0 || package.GameSettings.StartingHandSize < 0 ||
            package.GameSettings.DrawPerTurn < 0 || package.GameSettings.BaseActionPoints < 0)
        {
            throw new InvalidDataException("基础战斗参数包含超出允许范围的数值。");
        }
    }

    private static System.Collections.Generic.IEnumerable<string> ActorTriggerKeys() =>
        new[] { ContentTriggerKeys.OnUnitTurnStart, ContentTriggerKeys.OnUnitTurnEnd, ContentTriggerKeys.OnActivatedAbility,
            ContentTriggerKeys.OnStatusGained, ContentTriggerKeys.OnEnteredTerrain, ContentTriggerKeys.OnMoveCompleted }
            .Concat(ContentRuleQueryKeys.All);

    /// <summary>合成基础内容和全部扩展层之后，校验跨定义引用。</summary>
    private static void ValidateReferences(ContentPackage package)
    {
        System.Collections.Generic.HashSet<string> cardIds = package.Cards.Where(item => item.Enabled)
            .Select(item => item.CardId).ToHashSet(StringComparer.Ordinal);
        System.Collections.Generic.HashSet<string> poolIds = package.CardPools.Where(item => item.Enabled)
            .Select(item => item.PoolId).ToHashSet(StringComparer.Ordinal);
        System.Collections.Generic.HashSet<string> rarityIds = package.Rarities.Select(item => item.RarityId)
            .ToHashSet(StringComparer.Ordinal);
        System.Collections.Generic.HashSet<string> assetIds = package.Assets.Select(item => item.AssetKey)
            .ToHashSet(StringComparer.Ordinal);
        System.Collections.Generic.Dictionary<string, EquipmentDefinition> equipment = package.Equipment
            .Where(item => item.Enabled).ToDictionary(item => item.EquipmentId, StringComparer.Ordinal);
        System.Collections.Generic.HashSet<string> unitIds = package.Units.Where(item => item.Enabled)
            .Select(item => item.UnitId).ToHashSet(StringComparer.Ordinal);
        System.Collections.Generic.HashSet<string> deckIds = package.Decks.Where(item => item.Enabled)
            .Select(item => item.DeckId).ToHashSet(StringComparer.Ordinal);
        System.Collections.Generic.HashSet<string> aiProfileIds = package.AiProfiles.Where(item => item.Enabled)
            .Select(item => item.AiProfileId).ToHashSet(StringComparer.Ordinal);

        foreach (CardDefinition card in package.Cards.Where(item => item.Enabled))
        {
            if ((!string.IsNullOrWhiteSpace(card.RarityId) && !rarityIds.Contains(card.RarityId)) ||
                (!string.IsNullOrWhiteSpace(card.ArtworkKey) && !assetIds.Contains(card.ArtworkKey)) ||
                card.Pools.Any(item => !poolIds.Contains(item.PoolId)))
                throw new InvalidDataException($"卡牌 {card.CardId} 引用了不存在或未启用的稀有度、资源或卡池。");
        }
        foreach (DeckDefinition deck in package.Decks.Where(item => item.Enabled))
            if (deck.Entries.Any(item => !cardIds.Contains(item.CardId)))
                throw new InvalidDataException($"牌库 {deck.DeckId} 引用了不存在或未启用的卡牌。");
        foreach (ClassProfileDefinition profile in package.ClassProfiles.Where(item => item.Enabled))
        {
            if (profile.DeckRecipe.Any(item =>
                    (!string.IsNullOrEmpty(item.PoolId) && !poolIds.Contains(item.PoolId)) ||
                    (!string.IsNullOrEmpty(item.FixedCardId) && !cardIds.Contains(item.FixedCardId))))
                throw new InvalidDataException($"职业 {profile.ClassId} 的牌库配方包含无效引用。");
            foreach (ClassTraitDefinition trait in profile.Traits.Where(item =>
                         item.Key.StartsWith("equipment_", StringComparison.Ordinal) &&
                         item.Key.EndsWith("_id", StringComparison.Ordinal)))
            {
                string slot = trait.Key.Substring("equipment_".Length,
                    trait.Key.Length - "equipment_".Length - "_id".Length);
                if (!equipment.TryGetValue(trait.Value, out EquipmentDefinition item) || item.SlotKey != slot)
                    throw new InvalidDataException($"职业 {profile.ClassId} 的装备引用无效：{trait.Key}={trait.Value}。");
            }
        }
        foreach (EquipmentDefinition item in package.Equipment.Where(item => item.Enabled))
            if (!ContentEquipmentSlotKeys.IsValid(item.SlotKey) || !poolIds.Contains(item.CardPoolId))
                throw new InvalidDataException($"装备 {item.EquipmentId} 的槽位或卡池引用无效。");
        foreach (UnitDefinition unit in package.Units.Where(item => item.Enabled))
        {
            if (!string.IsNullOrEmpty(unit.DeckId) && !deckIds.Contains(unit.DeckId))
                throw new InvalidDataException($"单位 {unit.UnitId} 引用了不存在或未启用的牌库：{unit.DeckId}。");
            if (!string.IsNullOrEmpty(unit.AiProfileId) && !aiProfileIds.Contains(unit.AiProfileId))
                throw new InvalidDataException($"单位 {unit.UnitId} 引用了不存在或未启用的 AI 模板：{unit.AiProfileId}。");
        }
        if (!string.IsNullOrEmpty(package.GameSettings.PlayerUnitId) && !unitIds.Contains(package.GameSettings.PlayerUnitId))
            throw new InvalidDataException("基础战斗设置引用了不存在或未启用的玩家单位。");
    }

    /// <summary>校验稳定身份、行为归属、触发器支持和行为图能力。</summary>
    /// <param name="runtime">用于能力预检的行为图运行时。</param>
    /// <param name="ownerLabel">诊断用的本地化定义类型名。</param>
    /// <param name="owner">拥有该行为集合的定义。</param>
    /// <param name="ownerEnabled">启用状态下的行为图是否必须可执行。</param>
    /// <param name="behaviors">该定义的行为集合。</param>
    /// <param name="allowedTriggers">该定义类型支持的生命周期触发器。</param>
    private static void ValidateOwnerBehaviors(
        ContentBehaviorRuntime runtime,
        string ownerLabel,
        IContentDefinition owner,
        bool ownerEnabled,
        System.Collections.Generic.IEnumerable<BehaviorDefinition> behaviors,
        System.Collections.Generic.IEnumerable<string> allowedTriggers)
    {
        if (owner == null)
            throw new InvalidDataException($"内容包包含空的{ownerLabel}定义。");
        string ownerId = owner.GetDefinitionId();
        string ownerKind = owner.GetDefinitionKind();
        if (!ContentId.IsValid(ownerId))
            throw new InvalidDataException($"{ownerLabel} ID 不符合稳定格式：{ownerId}。");
        if (!ContentDefinitionKinds.CanOwnBehavior(ownerKind))
            throw new InvalidDataException($"{ownerLabel} {ownerId} 使用了无效所有者类型：{ownerKind}。");
        if (!ownerEnabled || behaviors == null) return;
        System.Collections.Generic.HashSet<string> allowed =
            new System.Collections.Generic.HashSet<string>(allowedTriggers, StringComparer.Ordinal);
        foreach (BehaviorDefinition behavior in behaviors)
        {
            if (behavior == null || !behavior.Enabled) continue;
            if (!ContentId.IsValid(behavior.BehaviorId) ||
                behavior.OwnerKind != ownerKind || behavior.OwnerId != ownerId ||
                !allowed.Contains(behavior.TriggerKey) || !runtime.CanExecuteBehavior(behavior) ||
                behavior.Nodes.Any(node => node == null || !ContentId.IsValid(node.NodeId)))
            {
                throw new InvalidDataException(
                    $"{ownerLabel} {ownerId} 的行为 {behavior.BehaviorId} 使用了无效 ID、所有者、触发器或节点。");
            }
        }
    }
}
}
