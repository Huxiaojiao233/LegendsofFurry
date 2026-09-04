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

/// <summary>S_Battle 棋盘绕 Y 转 45°。云海、围栏、大地图物件都跟这个朝向，不要用 identity。</summary>
public static class WorldBoardPose
{
    public const float YawDegrees = 45f;
    public static Quaternion Yaw => Quaternion.Euler(0f, YawDegrees, 0f);

    public static Quaternion Of(Transform board) => board != null ? board.rotation : Yaw;
}
