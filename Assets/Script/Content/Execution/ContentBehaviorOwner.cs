using System;
using LegendsOfFurry.Content.Contracts;

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>
/// Identifies the real definition and optional runtime instance that owns a behavior graph.
/// This keeps status, class, character, and equipment behavior execution independent from cards.
/// </summary>
public sealed class ContentBehaviorOwner
{
    /// <summary>Creates a validated behavior owner at the runtime content boundary.</summary>
    /// <param name="definition">The published definition that owns the graph.</param>
    /// <param name="displayName">The authored display name used in diagnostics.</param>
    /// <param name="runtimeInstance">The optional mutable instance associated with this execution.</param>
    /// <exception cref="ArgumentNullException">Thrown when the definition is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the kind or ID is invalid.</exception>
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

    /// <summary>Creates an owner descriptor for a card runtime instance.</summary>
    /// <param name="card">The card instance whose definition owns the graph.</param>
    /// <returns>A validated card behavior owner.</returns>
    public static ContentBehaviorOwner FromCard(CardInstance card)
    {
        if (card == null)
        {
            throw new ArgumentNullException(nameof(card));
        }

        return new ContentBehaviorOwner(card.Definition, card.Definition.DisplayName, card);
    }

    /// <summary>Creates an owner descriptor for a status runtime instance.</summary>
    /// <param name="definition">The published status definition.</param>
    /// <param name="instance">The mutable status instance currently firing the trigger.</param>
    /// <returns>A validated status behavior owner.</returns>
    public static ContentBehaviorOwner FromStatus(StatusDefinition definition, RuntimeStatusInstance instance)
    {
        return new ContentBehaviorOwner(definition, definition?.DisplayName, instance);
    }

    /// <summary>Creates an owner descriptor for a class profile.</summary>
    /// <param name="definition">The selected published class profile.</param>
    /// <returns>A validated class behavior owner.</returns>
    public static ContentBehaviorOwner FromClass(ClassProfileDefinition definition)
    {
        return new ContentBehaviorOwner(definition, definition?.DisplayName);
    }

    /// <summary>Creates an owner descriptor for a character definition and its scene unit instance.</summary>
    public static ContentBehaviorOwner FromCharacter(CharacterDefinition definition, Unit instance) =>
        new ContentBehaviorOwner(definition, definition?.DisplayName, instance);

    /// <summary>Creates an owner descriptor for an equipped item instance.</summary>
    public static ContentBehaviorOwner FromEquipment(EquipmentInstance instance)
    {
        if (instance == null) throw new ArgumentNullException(nameof(instance));
        return new ContentBehaviorOwner(instance.Definition, instance.Definition.DisplayName, instance);
    }
}
}
