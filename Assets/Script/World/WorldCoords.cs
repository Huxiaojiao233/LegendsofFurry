using System;
using UnityEngine;

/// <summary>区块坐标与世界格子之间的换算。负坐标用向下取整，避免 C# 向零截断。</summary>
public readonly struct ChunkPosition : IEquatable<ChunkPosition>
{
    public readonly int X;
    public readonly int Y;

    public ChunkPosition(int x, int y)
    {
        X = x;
        Y = y;
    }

    public bool Equals(ChunkPosition other) => X == other.X && Y == other.Y;
    public override bool Equals(object obj) => obj is ChunkPosition other && Equals(other);
    public override int GetHashCode() => (X * 397) ^ Y;
    public override string ToString() => $"{X}_{Y}";
}

public static class WorldCoords
{
    public static int FloorDiv(int value, int divisor)
    {
        if (divisor <= 0) throw new ArgumentOutOfRangeException(nameof(divisor));
        int quotient = value / divisor;
        if (value < 0 && value % divisor != 0) quotient--;
        return quotient;
    }

    public static int FloorMod(int value, int divisor)
    {
        if (divisor <= 0) throw new ArgumentOutOfRangeException(nameof(divisor));
        int remainder = value % divisor;
        if (remainder < 0) remainder += divisor;
        return remainder;
    }

    public static Vector2Int ToWorldTile(ChunkPosition chunk, int localX, int localY, int chunkWidth, int chunkHeight)
    {
        return new Vector2Int(chunk.X * chunkWidth + localX, chunk.Y * chunkHeight + localY);
    }

    public static void ToChunk(int worldX, int worldY, int chunkWidth, int chunkHeight,
        out ChunkPosition chunk, out int localX, out int localY)
    {
        chunkWidth = Mathf.Max(1, chunkWidth);
        chunkHeight = Mathf.Max(1, chunkHeight);
        chunk = new ChunkPosition(FloorDiv(worldX, chunkWidth), FloorDiv(worldY, chunkHeight));
        localX = FloorMod(worldX, chunkWidth);
        localY = FloorMod(worldY, chunkHeight);
    }
}

/// <summary>
/// 无限块的可逆 Stage ID。必须符合 ContentId：小写字母开头，其余为字母、数字、点、下划线或连字符。
/// 负坐标用 n 前缀，例如 (-1, 2) → cn1_2。
/// </summary>
public static class WorldStageIds
{
    public static string FromChunk(ChunkPosition position) =>
        "c" + Encode(position.X) + "_" + Encode(position.Y);

    public static bool TryParse(string stageId, out ChunkPosition position)
    {
        position = default;
        if (string.IsNullOrEmpty(stageId) || stageId[0] != 'c') return false;
        int split = stageId.IndexOf('_', 1);
        if (split <= 1 || split >= stageId.Length - 1) return false;
        if (!TryDecode(stageId.Substring(1, split - 1), out int x)) return false;
        if (!TryDecode(stageId.Substring(split + 1), out int y)) return false;
        position = new ChunkPosition(x, y);
        return true;
    }

    private static string Encode(int value) => value < 0 ? "n" + (-(long)value) : value.ToString();

    private static bool TryDecode(string token, out int value)
    {
        value = 0;
        if (string.IsNullOrEmpty(token)) return false;
        if (token[0] == 'n')
        {
            if (token.Length < 2 || !int.TryParse(token.Substring(1), out int magnitude) || magnitude <= 0)
                return false;
            value = -magnitude;
            return true;
        }

        return int.TryParse(token, out value) && value >= 0 && token[0] != '-';
    }
}

/// <summary>S_Battle 棋盘绕 Y 转 45°。云海、围栏、大地图物件都跟这个朝向，不要用 identity。</summary>
public static class WorldBoardPose
{
    public const float YawDegrees = 45f;
    public static Quaternion Yaw => Quaternion.Euler(0f, YawDegrees, 0f);

    public static Quaternion Of(Transform board) => board != null ? board.rotation : Yaw;
}
