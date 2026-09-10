using System.Linq;
using LegendsOfFurry.Content.Contracts;

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>执行当前单位身上由数据库状态定义提供的生命周期行为。</summary>
public static class ContentStatusBehaviorRuntime
{
    /// <summary>按状态 ID 和行为优先级执行指定触发器；无内容包时返回 false。</summary>
    public static bool Execute(Unit owner, string triggerKey)
    {
        if (owner == null || !ContentRuntime.IsLoaded) return false;
        bool executed = false;
        foreach (RuntimeStatusInstance instance in owner.State.GetStatusSnapshot())
        {
            if (!ContentRuntime.Registry.TryGetStatus(instance.StatusId, out StatusDefinition status)) continue;
            BehaviorDefinition[] sourceBehaviors = status.Behaviors
                .Where(item => item.Enabled && item.TriggerKey == triggerKey).OrderBy(item => item.Priority).ToArray();
            if (sourceBehaviors.Length == 0) continue;
            executed = true;
            CardPlayResult result = new CardPlayResult();
            ContentCardExecutionContext context = new ContentCardExecutionContext(
                ContentBehaviorOwner.FromStatus(status, instance),
                owner, owner, null, null, null, null, result, true);
            ContentCardEffectExecutor.TryExecuteOwnedTrigger(context, status.Behaviors, triggerKey);
        }
        return executed;
    }

    public static bool ExecuteStatus(Unit owner, RuntimeStatusInstance instance, string triggerKey)
    {
        if (owner == null || instance?.Definition == null) return false;
        BehaviorDefinition[] sourceBehaviors = instance.Definition.Behaviors
            .Where(item => item.Enabled && item.TriggerKey == triggerKey).OrderBy(item => item.Priority).ToArray();
        if (sourceBehaviors.Length == 0) return false;
        CardPlayResult result = new CardPlayResult();
        ContentCardExecutionContext context = new ContentCardExecutionContext(
            ContentBehaviorOwner.FromStatus(instance.Definition, instance),
            owner, owner, null, null, null, null, result, true);
        return ContentCardEffectExecutor.TryExecuteOwnedTrigger(context, sourceBehaviors, triggerKey);
    }
}
}
