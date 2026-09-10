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
            GameSession.SelectedClassId, out profile);
    }

    /// <summary>执行职业指定触发器并返回免费移动、结束回合等控制结果。</summary>
    public static CardPlayResult Execute(ClassProfileDefinition profile, string triggerKey, Unit owner,
        BoardClickController actionPoints, Unit selectedUnit = null, ICardDrawService hand = null)
    {
        CardPlayResult result = new CardPlayResult { Success = true };
        if (profile == null || owner == null) return result;
        BehaviorDefinition[] sourceBehaviors = profile.Behaviors
            .Where(item => item.Enabled && item.TriggerKey == triggerKey).ToArray();
        if (sourceBehaviors.Length == 0) return result;
        ContentCardExecutionContext context = new ContentCardExecutionContext(
            ContentBehaviorOwner.FromClass(profile),
            owner, selectedUnit ?? owner, null, null, hand, actionPoints, result, true);
        result.ExecutionContext = context;
        result.Success = ContentCardEffectExecutor.TryExecuteOwnedTrigger(context, profile.Behaviors, triggerKey);
        return result;
    }
}
}
