using LegendsOfFurry.Content.Contracts;

/// <summary>
/// C# / TypeScript 共用的 32 位整数哈希。无限地图的 hash.v1 生成器只依赖这里，不使用 Mathf.PerlinNoise。
/// </summary>
public static class WorldIntegerHash
{
    public const string GeneratorId = "hash.v1";
    public const int GeneratorVersion = 1;

    public static int Mix(int x, int y, int seed)
    {
        unchecked
        {
            int hash = x * 374761393 + y * 668265263 + seed * 1274126177;
            hash = (hash ^ (hash >> 13)) * 1274126177;
            hash ^= hash >> 16;
            return hash;
        }
    }

    public static int HeightAt(int worldX, int worldY, int seed)
    {
        int range = WorldTerrain.MaxHeight - WorldTerrain.MinHeight + 1;
        int unit = Mix(worldX, worldY, seed) & 0x7fffffff;
        return WorldTerrain.MinHeight + unit % range;
    }

    public static int MoisturePermille(int worldX, int worldY, int seed) =>
        (Mix(worldX, worldY, seed + 91) & 0x7fffffff) % 1000;

    public static string ChooseTerrain(int height, int moisturePermille)
    {
        if (height <= WorldTerrain.MinHeight + 1 && moisturePermille > 340) return WorldTerrainCatalog.Water;
        if (height < 0) return moisturePermille > 500 ? WorldTerrainCatalog.Water : WorldTerrainCatalog.Sand;
        if (height >= 8) return WorldTerrainCatalog.Stone;
        if (height >= 6) return moisturePermille > 450 ? WorldTerrainCatalog.Stone : WorldTerrainCatalog.Dirt;
        if (moisturePermille > 620 && height <= 5) return WorldTerrainCatalog.Forest;
        if (moisturePermille < 320) return height <= 1 ? WorldTerrainCatalog.Sand : WorldTerrainCatalog.Dirt;
        if (height >= 3) return WorldTerrainCatalog.Dirt;
        return WorldTerrainCatalog.Grass;
    }

    public static float UnitFloat(int x, int y, int seed)
    {
        return (Mix(x, y, seed) & 0x7fffffff) / (float)int.MaxValue;
    }
}
