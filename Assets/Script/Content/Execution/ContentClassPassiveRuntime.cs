using System.Linq;
using LegendsOfFurry.Content.Contracts;

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>把职业被动行为复用到通用效果执行器，避免 BattleFlow 按职业枚举分支。</summary>
public static class ContentClassPassiveRuntime
{
    /// <summary>按当前游戏会话职业查找已发布资料。</summary>
    public static bool TryGetSelectedProfile(out ClassProfileDefinition profile)
    {
        profile = null;
        return ContentRuntime.IsLoaded && ContentRuntime.Registry.TryGetClassProfile(
            GameSession.SelectedClass.ToString().ToLowerInvariant(), out profile);
    }

    /// <summary>读取当前职业整数特性。</summary>
    public static int GetSelectedTraitInt(string key, int defaultValue = 0)
    {
        return TryGetSelectedProfile(out ClassProfileDefinition profile) ? profile.GetTraitInt(key, defaultValue) : defaultValue;
    }

    /// <summary>读取当前职业布尔特性。</summary>
    public static bool GetSelectedTraitBool(string key, bool defaultValue = false)
    {
        return TryGetSelectedProfile(out ClassProfileDefinition profile) ? profile.GetTraitBool(key, defaultValue) : defaultValue;
    }
    /// <summary>执行职业指定触发器并返回免费移动、结束回合等控制结果。</summary>
    public static CardPlayResult Execute(ClassProfileDefinition profile, string triggerKey, Unit owner,
        BoardClickController actionPoints)
    {
        CardPlayResult result = new CardPlayResult { Success = true };
        if (profile == null || owner == null) return result;
        BehaviorDefinition[] sourceBehaviors = profile.Behaviors
            .Where(item => item.Enabled && item.TriggerKey == triggerKey).ToArray();
        if (sourceBehaviors.Length == 0) return result;
        CardDefinition definition = new CardDefinition
        {
            CardId = "class." + profile.ClassId,
            DisplayName = profile.DisplayName,
            RarityId = "gray",
            FamilyId = "class_passive",
            Target = new CardTargetRule { SelectionMode = "self", AllowSelf = true }
        };
        foreach (BehaviorDefinition source in sourceBehaviors)
        {
            BehaviorDefinition behavior = new BehaviorDefinition
            {
                BehaviorId = source.BehaviorId,
                OwnerKind = "card",
                OwnerId = definition.CardId,
                TriggerKey = "on_play",
                Priority = source.Priority,
                Enabled = source.Enabled
            };
            behavior.Nodes.AddRange(source.Nodes);
            definition.Behaviors.Add(behavior);
        }
        CardInstance card = new CardInstance(definition);
        ContentCardExecutionContext context = new ContentCardExecutionContext(
            card, owner, owner, null, null, null, actionPoints, result, true);
        result.ExecutionContext = context;
        result.Success = ContentCardEffectExecutor.TryExecuteOnPlay(context);
        return result;
    }
}
}
