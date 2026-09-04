using System;
using System.Collections.Generic;

/// <summary>Marker contract for immutable notifications emitted by the combat runtime.</summary>
public interface ICombatEvent { }

/// <summary>A unit lifecycle event using the same trigger key vocabulary as content behaviors.</summary>
public sealed class UnitTriggerEvent : ICombatEvent
{
    public UnitTriggerEvent(Unit unit, string triggerKey)
    {
        Unit = unit;
        TriggerKey = triggerKey ?? string.Empty;
    }

    public Unit Unit { get; }
    public string TriggerKey { get; }
}

/// <summary>Notification emitted before a mutable damage request is finalized.</summary>
public sealed class DamageRequestedEvent : ICombatEvent
{
    public DamageRequestedEvent(DamageRequest request) => Request = request;
    public DamageRequest Request { get; }
}

/// <summary>Notification emitted after armor, health, revive, and death resolution.</summary>
public sealed class DamageResolvedEvent : ICombatEvent
{
    public DamageResolvedEvent(DamageResolution resolution) => Resolution = resolution;
    public DamageResolution Resolution { get; }
}

/// <summary>Notification describing a stable-ID status mutation.</summary>
public sealed class StatusChangedEvent : ICombatEvent
{
    public StatusChangedEvent(Unit unit, string statusId, int previousStacks, int currentStacks)
    {
        Unit = unit;
        StatusId = statusId ?? string.Empty;
        PreviousStacks = previousStacks;
        CurrentStacks = currentStacks;
    }

    public Unit Unit { get; }
    public string StatusId { get; }
    public int PreviousStacks { get; }
    public int CurrentStacks { get; }
}

public sealed class StatusGainedEvent : ICombatEvent
{
    public StatusGainedEvent(Unit unit, string statusId, int stacks) { Unit = unit; StatusId = statusId ?? string.Empty; Stacks = stacks; }
    public Unit Unit { get; }
    public string StatusId { get; }
    public int Stacks { get; }
}

public sealed class TerrainEnteredEvent : ICombatEvent
{
    public TerrainEnteredEvent(Unit unit, string terrainId, UnityEngine.Vector2Int from, UnityEngine.Vector2Int to)
    { Unit = unit; TerrainId = terrainId ?? string.Empty; From = from; To = to; }
    public Unit Unit { get; }
    public string TerrainId { get; }
    public UnityEngine.Vector2Int From { get; }
    public UnityEngine.Vector2Int To { get; }
}

public sealed class UnitMoveCompletedEvent : ICombatEvent
{
    public UnitMoveCompletedEvent(Unit unit, UnityEngine.Vector2Int from, UnityEngine.Vector2Int to)
    { Unit = unit; From = from; To = to; }
    public Unit Unit { get; }
    public UnityEngine.Vector2Int From { get; }
    public UnityEngine.Vector2Int To { get; }
}

/// <summary>Notification describing a card instance moving between stable card zones.</summary>
public sealed class CardZoneChangedEvent : ICombatEvent
{
    public CardZoneChangedEvent(CardInstance card, string sourceZone, string destinationZone)
    {
        Card = card;
        SourceZone = sourceZone ?? string.Empty;
        DestinationZone = destinationZone ?? string.Empty;
    }

    public CardInstance Card { get; }
    public string SourceZone { get; }
    public string DestinationZone { get; }
}

/// <summary>
/// Small synchronous event stream for battle rules and diagnostics. Publishers do not know concrete listeners,
/// while subscriptions are explicitly disposable to avoid scene-lifetime leaks.
/// </summary>
public sealed class CombatEventBus
{
    private readonly Dictionary<Type, List<Delegate>> handlers = new Dictionary<Type, List<Delegate>>();

    public static CombatEventBus Shared { get; } = new CombatEventBus();

    /// <summary>Registers a typed listener and returns a handle that removes it.</summary>
    public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : ICombatEvent
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        Type type = typeof(TEvent);
        if (!handlers.TryGetValue(type, out List<Delegate> values))
        {
            values = new List<Delegate>();
            handlers.Add(type, values);
        }
        values.Add(handler);
        return new Subscription(() => values.Remove(handler));
    }

    /// <summary>Synchronously publishes one event to a snapshot of its typed listeners.</summary>
    public void Publish<TEvent>(TEvent combatEvent) where TEvent : ICombatEvent
    {
        if (combatEvent == null || !handlers.TryGetValue(typeof(TEvent), out List<Delegate> values)) return;
        foreach (Delegate value in values.ToArray()) ((Action<TEvent>)value)(combatEvent);
    }

    private sealed class Subscription : IDisposable
    {
        private Action dispose;
        public Subscription(Action disposeAction) => dispose = disposeAction;
        public void Dispose()
        {
            Action action = dispose;
            dispose = null;
            action?.Invoke();
        }
    }
}
