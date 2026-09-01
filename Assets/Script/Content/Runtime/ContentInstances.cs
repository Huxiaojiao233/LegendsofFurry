using System;
using LegendsOfFurry.Content.Contracts;

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>A mutable equipped-item identity that remains bound to its immutable authored definition.</summary>
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
