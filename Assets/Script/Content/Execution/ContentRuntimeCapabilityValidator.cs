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
        ContentBehaviorRuntime runtime = new ContentBehaviorRuntime();
        foreach (CardDefinition card in package.Cards)
        {
            if (card == null || !card.Enabled)
            {
                continue;
            }
            foreach (BehaviorDefinition behavior in card.Behaviors)
            {
                if (behavior == null || !behavior.Enabled)
                {
                    continue;
                }
                if (!ContentCapabilityCatalog.Phase5ExecutableTriggerKeys.Contains(behavior.TriggerKey))
                {
                    throw new InvalidDataException(
                        $"卡牌 {card.CardId} 的行为 {behavior.BehaviorId} 使用了尚未实现的 Trigger：{behavior.TriggerKey}。");
                }
                if (!runtime.CanExecuteBehavior(behavior))
                {
                    throw new InvalidDataException(
                        $"卡牌 {card.CardId} 的行为 {behavior.BehaviorId} 包含未知、尚未实现或参数无效的节点。");
                }
            }
        }
        foreach (StatusDefinition status in package.Statuses)
        {
            ValidateOwnerBehaviors(runtime, "状态", status.StatusId, status.Enabled, status.Behaviors,
                "on_unit_turn_start", "on_unit_turn_end");
        }
        foreach (ClassProfileDefinition profile in package.ClassProfiles)
        {
            ValidateOwnerBehaviors(runtime, "职业", profile.ClassId, profile.Enabled, profile.Behaviors,
                "on_unit_turn_start", "on_unit_turn_end");
        }
        if (package.GameSettings.HandLimit <= 0 || package.GameSettings.StartingHandSize < 0 ||
            package.GameSettings.DrawPerTurn < 0 || package.GameSettings.BaseActionPoints < 0)
        {
            throw new InvalidDataException("基础战斗参数包含超出允许范围的数值。");
        }
    }

    /// <summary>验证状态或职业所有者的启用行为和允许触发器。</summary>
    private static void ValidateOwnerBehaviors(ContentBehaviorRuntime runtime, string ownerKind, string ownerId,
        bool ownerEnabled, System.Collections.Generic.IEnumerable<BehaviorDefinition> behaviors,
        params string[] allowedTriggers)
    {
        if (!ownerEnabled || behaviors == null) return;
        foreach (BehaviorDefinition behavior in behaviors)
        {
            if (behavior == null || !behavior.Enabled) continue;
            if (Array.IndexOf(allowedTriggers, behavior.TriggerKey) < 0 || !runtime.CanExecuteBehavior(behavior))
                throw new InvalidDataException($"{ownerKind} {ownerId} 的行为 {behavior.BehaviorId} 使用了无效触发器或节点。");
        }
    }
}
}
