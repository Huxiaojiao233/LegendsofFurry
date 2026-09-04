using System;
using LegendsOfFurry.Content.Contracts;

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>
/// 标识真正拥有行为图的定义，以及可选的运行时实例。
/// 这样状态、职业、角色和装备的行为执行可以不依赖卡牌。
/// </summary>
public sealed class ContentBehaviorOwner
{
    /// <summary>在运行时内容边界创建经过校验的行为归属。</summary>
    /// <param name="definition">拥有该行为图的已发布定义。</param>
    /// <param name="displayName">诊断使用的策划显示名。</param>
    /// <param name="runtimeInstance">本次执行关联的可选可变实例。</param>
    /// <exception cref="ArgumentNullException">定义为空时抛出。</exception>
    /// <exception cref="ArgumentException">类型或 ID 无效时抛出。</exception>
    public ContentBehaviorOwner(
        IContentDefinition definition,
        string displayName,
        object runtimeInstance = null)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        OwnerKind = definition.GetDefinitionKind();
        OwnerId = ContentId.Require(definition.GetDefinitionId(), nameof(definition));
        if (!ContentDefinitionKinds.CanOwnBehavior(OwnerKind))
        {
            throw new ArgumentException($"Definition kind cannot own behavior graphs: {OwnerKind}", nameof(definition));
        }

        DisplayName = string.IsNullOrWhiteSpace(displayName) ? OwnerId : displayName;
        RuntimeInstance = runtimeInstance;
    }

    public string OwnerKind { get; }
    public string OwnerId { get; }
    public string DisplayName { get; }
    public IContentDefinition Definition { get; }
    public object RuntimeInstance { get; }

    /// <summary>为卡牌运行时实例创建归属描述。</summary>
    /// <param name="card">其定义拥有行为图的卡牌实例。</param>
    /// <returns>经过校验的卡牌行为归属。</returns>
    public static ContentBehaviorOwner FromCard(CardInstance card)
    {
        if (card == null)
        {
            throw new ArgumentNullException(nameof(card));
        }

        return new ContentBehaviorOwner(card.Definition, card.Definition.DisplayName, card);
    }

    /// <summary>为状态运行时实例创建归属描述。</summary>
    /// <param name="definition">已发布的状态定义。</param>
    /// <param name="instance">当前正在触发的可变状态实例。</param>
    /// <returns>经过校验的状态行为归属。</returns>
    public static ContentBehaviorOwner FromStatus(StatusDefinition definition, RuntimeStatusInstance instance)
    {
        return new ContentBehaviorOwner(definition, definition?.DisplayName, instance);
    }

    /// <summary>为职业资料创建归属描述。</summary>
    /// <param name="definition">当前选中的已发布职业资料。</param>
    /// <returns>经过校验的职业行为归属。</returns>
    public static ContentBehaviorOwner FromClass(ClassProfileDefinition definition)
    {
        return new ContentBehaviorOwner(definition, definition?.DisplayName);
    }

    /// <summary>为角色定义及其场景单位实例创建归属描述。</summary>
    public static ContentBehaviorOwner FromUnit(UnitDefinition definition, Unit instance) =>
        new ContentBehaviorOwner(definition, definition?.DisplayName, instance);

    /// <summary>为已装备物品实例创建归属描述。</summary>
    public static ContentBehaviorOwner FromEquipment(EquipmentInstance instance)
    {
        if (instance == null) throw new ArgumentNullException(nameof(instance));
        return new ContentBehaviorOwner(instance.Definition, instance.Definition.DisplayName, instance);
    }
}
}
