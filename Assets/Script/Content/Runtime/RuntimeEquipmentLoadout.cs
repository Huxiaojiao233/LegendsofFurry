using System;
using System.Collections.Generic;
using System.Linq;
using LegendsOfFurry.Content.Contracts;
using UnityEngine;

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>Owns equipped item instances independently from the HUD that displays them.</summary>
[DisallowMultipleComponent]
public sealed class RuntimeEquipmentLoadout : MonoBehaviour
{
    private readonly Dictionary<string, EquipmentInstance> slots =
        new Dictionary<string, EquipmentInstance>(StringComparer.Ordinal);

    public event Action Changed;

    public IReadOnlyList<EquipmentInstance> GetSnapshot() => slots.Values
        .OrderBy(item => item.Definition.SlotKey, StringComparer.Ordinal).ToArray();

    public bool TryGet(string slotKey, out EquipmentInstance instance) =>
        slots.TryGetValue(slotKey ?? string.Empty, out instance);

    public bool Equip(string equipmentId)
    {
        if (!ContentRuntime.IsLoaded ||
            !ContentRuntime.Registry.TryGetEquipment(equipmentId, out EquipmentDefinition definition)) return false;
        slots[definition.SlotKey] = new EquipmentInstance(definition);
        Changed?.Invoke();
        return true;
    }

    public bool Unequip(string slotKey)
    {
        bool removed = slots.Remove(slotKey ?? string.Empty);
        if (removed) Changed?.Invoke();
        return removed;
    }

    /// <summary>Builds the selected class loadout; legacy pool trait keys remain a schema-v1 compatibility fallback.</summary>
    public void Configure(ClassProfileDefinition profile)
    {
        slots.Clear();
        if (profile == null) return;
        ConfigureSlot(profile, ContentEquipmentSlotKeys.Weapon);
        ConfigureSlot(profile, ContentEquipmentSlotKeys.Offhand);
        ConfigureSlot(profile, ContentEquipmentSlotKeys.Accessory);
        ConfigureSlot(profile, ContentEquipmentSlotKeys.Armor);
        ConfigureSlot(profile, ContentEquipmentSlotKeys.Treasure);
        ConfigureSlot(profile, ContentEquipmentSlotKeys.Boot);
        Changed?.Invoke();
    }

    private void ConfigureSlot(ClassProfileDefinition profile, string slotKey)
    {
        string equipmentId = profile.GetTraitString($"equipment_{slotKey}_id",
            profile.GetTraitString($"equipment_{slotKey}_pool"));
        if (!string.IsNullOrEmpty(equipmentId)) Equip(equipmentId);
    }
}
}
