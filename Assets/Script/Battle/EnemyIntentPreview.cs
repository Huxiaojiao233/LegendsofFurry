using UnityEngine;

/// <summary>敌方下一行动的只读预览，供意图标记与检视 UI 使用。</summary>
public readonly struct EnemyIntentPreview
{
    public enum Kind
    {
        None,
        PlayCard,
        Approach,
        Retreat
    }

    public EnemyIntentPreview(Kind kind, string label, string cardId, string cardName, Unit target,
        Vector2Int? destination, Vector2Int origin)
    {
        ActionKind = kind;
        Label = label ?? string.Empty;
        CardId = cardId ?? string.Empty;
        CardName = cardName ?? string.Empty;
        Target = target;
        Destination = destination;
        Origin = origin;
    }

    public Kind ActionKind { get; }
    public string Label { get; }
    public string CardId { get; }
    public string CardName { get; }
    public Unit Target { get; }
    public Vector2Int? Destination { get; }
    public Vector2Int Origin { get; }
    public bool HasValue => ActionKind != Kind.None;
}
