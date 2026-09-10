using System.Collections.Generic;
using LegendsOfFurry.Content.Contracts;

/// <summary>块从哪里来。玩法层只问 Provider，不分支手写还是生成。</summary>
public enum ChunkOrigin
{
    Authored = 0,
    Generated = 1,
    Overlay = 2
}

/// <summary>玩法、渲染和寻路只向 Provider 要块，不分支地图是手写还是生成。</summary>
public interface IChunkProvider
{
    WorldDefinition World { get; }
    bool CanProvide(ChunkPosition position);
    bool IsResident(ChunkPosition position);
    bool TryGetChunk(ChunkPosition position, out StageDefinition chunk);
    bool TryGetOrigin(ChunkPosition position, out ChunkOrigin origin);
    void Release(ChunkPosition position);
}

/// <summary>有限手写图：越界不可提供，界内缺文件由加载期校验，运行时按坐标查找。</summary>
public sealed class FiniteChunkProvider : IChunkProvider
{
    private readonly Dictionary<(int x, int y), StageDefinition> chunks =
        new Dictionary<(int, int), StageDefinition>();

    public FiniteChunkProvider(WorldDefinition world)
    {
        World = world;
        if (world == null) return;
        for (int i = 0; i < world.Stages.Count; i++)
        {
            StageDefinition stage = world.Stages[i];
            if (stage == null || !stage.Enabled) continue;
            chunks[(stage.GridX, stage.GridY)] = stage;
        }
    }

    public WorldDefinition World { get; }

    public bool CanProvide(ChunkPosition position) => chunks.ContainsKey((position.X, position.Y));

    public bool IsResident(ChunkPosition position) => CanProvide(position);

    public bool TryGetChunk(ChunkPosition position, out StageDefinition chunk) =>
        chunks.TryGetValue((position.X, position.Y), out chunk);

    public bool TryGetOrigin(ChunkPosition position, out ChunkOrigin origin)
    {
        origin = ChunkOrigin.Authored;
        return CanProvide(position);
    }

    public void Release(ChunkPosition position)
    {
        // 有限手写块留在 WorldDefinition.Stages 里，不从内存卸载。
    }
}
