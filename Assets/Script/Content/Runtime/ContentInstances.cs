using System;
using LegendsOfFurry.Content.Contracts;

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>可变的已装备物品身份，始终绑定到不可变的策划定义。</summary>
public sealed class EquipmentInstance : IContentInstance<EquipmentDefinition>
{
    public EquipmentInstance(EquipmentDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        ContentId.Require(definition.EquipmentId, nameof(definition));
        InstanceId = Guid.NewGuid().ToString("N");
    }

    public string InstanceId { get; }
    public EquipmentDefinition Definition { get; }
}
}
