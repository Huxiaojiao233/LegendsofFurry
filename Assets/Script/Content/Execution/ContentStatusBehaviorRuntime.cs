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
            CardDefinition definition = new CardDefinition
            {
                CardId = "status." + status.StatusId, DisplayName = status.DisplayName,
                RarityId = "gray", FamilyId = "status",
                Target = new CardTargetRule { SelectionMode = "self", AllowSelf = true }
            };
            foreach (BehaviorDefinition source in sourceBehaviors)
            {
                BehaviorDefinition behavior = new BehaviorDefinition
                {
                    BehaviorId = source.BehaviorId, OwnerKind = "card", OwnerId = definition.CardId,
                    TriggerKey = "on_play", Priority = source.Priority, Enabled = source.Enabled
                };
                behavior.Nodes.AddRange(source.Nodes);
                definition.Behaviors.Add(behavior);
            }
            CardPlayResult result = new CardPlayResult();
            ContentCardExecutionContext context = new ContentCardExecutionContext(
                new CardInstance(definition), owner, owner, null, null, null, null, result, true);
            ContentCardEffectExecutor.TryExecuteOnPlay(context);
        }
        return executed;
    }
}
}
