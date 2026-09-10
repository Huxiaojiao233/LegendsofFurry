using System.Collections.Generic;
using LegendsOfFurry.Content.Contracts;

/// <summary>按种子和块坐标确定性生成无限基础块。缺块不落盘。</summary>
public sealed class ProceduralChunkProvider : IChunkProvider
{
    private readonly Dictionary<(int x, int y), StageDefinition> resident =
        new Dictionary<(int, int), StageDefinition>();

    public ProceduralChunkProvider(WorldDefinition world)
    {
        World = world ?? throw new System.ArgumentNullException(nameof(world));
    }

    public WorldDefinition World { get; }

    public bool CanProvide(ChunkPosition position) => true;

    public bool IsResident(ChunkPosition position) => resident.ContainsKey((position.X, position.Y));

    public bool TryGetChunk(ChunkPosition position, out StageDefinition chunk)
    {
        if (resident.TryGetValue((position.X, position.Y), out chunk))
            return true;

        chunk = WorldNoiseGenerator.GenerateChunk(World, position);
        resident[(position.X, position.Y)] = chunk;
        return chunk != null;
    }

    public bool TryGetOrigin(ChunkPosition position, out ChunkOrigin origin)
    {
        origin = ChunkOrigin.Generated;
        return true;
    }

    public void Release(ChunkPosition position) => resident.Remove((position.X, position.Y));
}
