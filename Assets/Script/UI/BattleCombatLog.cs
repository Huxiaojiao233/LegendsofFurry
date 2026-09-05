using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>战斗信息条目分类，供 UI 筛选。</summary>
[Flags]
public enum BattleLogCategory
{
    None = 0,
    Damage = 1 << 0,
    Heal = 1 << 1,
    Move = 1 << 2,
    Card = 1 << 3,
    Status = 1 << 4,
    Turn = 1 << 5,
    Armor = 1 << 6,
    All = Damage | Heal | Move | Card | Status | Turn | Armor
}

/// <summary>一条可读的战斗记录。</summary>
public readonly struct BattleLogEntry
{
    public BattleLogEntry(BattleLogCategory category, string message, float time)
    {
        Category = category;
        Message = message ?? string.Empty;
        Time = time;
    }

    public BattleLogCategory Category { get; }
    public string Message { get; }
    public float Time { get; }
}

/// <summary>集中收集战斗事件文案，供 Scroll View 实时展示。</summary>
public static class BattleCombatLog
{
    public const int MaxEntries = 200;
    private static readonly List<BattleLogEntry> entries = new List<BattleLogEntry>(MaxEntries);

    public static event Action<BattleLogEntry> EntryAdded;
    public static event Action Cleared;
    public static IReadOnlyList<BattleLogEntry> Entries => entries;

    public static void Clear()
    {
        entries.Clear();
        Cleared?.Invoke();
    }

    public static void Append(BattleLogCategory category, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        if (!IsCombatActive()) return;
        BattleLogEntry entry = new BattleLogEntry(category, message.Trim(), Time.time);
        entries.Add(entry);
        while (entries.Count > MaxEntries) entries.RemoveAt(0);
        EntryAdded?.Invoke(entry);
    }

    private static bool IsCombatActive()
    {
        if (WorldPlaySession.Instance != null) return WorldPlaySession.Instance.IsCombat;
        BattleFlow flow = BattleFlow.Instance;
        return flow != null && flow.Phase != BattlePhase.Exploration && flow.Phase != BattlePhase.GameOver;
    }
}
