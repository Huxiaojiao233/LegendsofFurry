using System.Linq;
using LegendsOfFurry.Content.Contracts;

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>Executes character and equipment graphs through the same owner-neutral rule executor.</summary>
public static class ContentActorBehaviorRuntime
{
    public static CardPlayResult Execute(Unit owner, string triggerKey, BoardClickController actionPoints)
    {
        CardPlayResult result = new CardPlayResult { Success = true };
        if (owner?.Definition != null)
            ExecuteOwner(ContentBehaviorOwner.FromCharacter(owner.Definition, owner), owner.Definition.Behaviors,
                triggerKey, owner, actionPoints, result);
        RuntimeEquipmentLoadout loadout = owner != null ? owner.GetComponent<RuntimeEquipmentLoadout>() : null;
        if (loadout != null)
            foreach (EquipmentInstance item in loadout.GetSnapshot())
                ExecuteOwner(ContentBehaviorOwner.FromEquipment(item), item.Definition.Behaviors,
                    triggerKey, owner, actionPoints, result);
        return result;
    }

    private static void ExecuteOwner(
        ContentBehaviorOwner behaviorOwner,
        System.Collections.Generic.IReadOnlyCollection<BehaviorDefinition> behaviors,
        string triggerKey,
        Unit unit,
        BoardClickController actionPoints,
        CardPlayResult result)
    {
        if (!behaviors.Any(item => item.Enabled && item.TriggerKey == triggerKey)) return;
        ContentCardExecutionContext context = new ContentCardExecutionContext(
            behaviorOwner, unit, unit, null, null, null, actionPoints, result, true);
        result.ExecutionContext = context;
        result.Success &= ContentCardEffectExecutor.TryExecuteOwnedTrigger(context, behaviors, triggerKey);
    }
}
}
