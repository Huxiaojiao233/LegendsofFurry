using UnityEngine;

/// <summary>全局只保留一格地形悬浮高亮，战斗与探索共用。</summary>
public static class BoardTileHover
{
    private static BoardCell current;

    public static BoardCell Current => current;

    public static void Set(BoardCell cell)
    {
        if (current == cell) return;
        if (current != null) current.SetHovered(false);
        current = cell;
        if (current != null) current.SetHovered(true);
    }

    public static void Clear() => Set(null);
}
