using System.Collections.Generic;
using LegendsOfFurry.Content.Contracts;

/// <summary>玩法、渲染和寻路只向 Provider 要块，不分支地图是手写还是生成。</summary>
public interface IChunkProvider
{
    bool CanProvide(ChunkPosition position);
    bool TryGetChunk(ChunkPosition position, out StageDefinition chunk);
}

/// <summary>有限手写图：越界不可提供，界内缺文件由加载期校验，运行时按坐标查找。</summary>
public sealed class FiniteChunkProvider : IChunkProvider
{
    private readonly Dictionary<(int x, int y), StageDefinition> chunks =
        new Dictionary<(int, int), StageDefinition>();

    public FiniteChunkProvider(WorldDefinition world)
    {
        if (world == null) return;
        for (int i = 0; i < world.Stages.Count; i++)
        {
            StageDefinition stage = world.Stages[i];
            if (stage == null || !stage.Enabled) continue;
            chunks[(stage.GridX, stage.GridY)] = stage;
        }
    }

    public bool CanProvide(ChunkPosition position) => chunks.ContainsKey((position.X, position.Y));

    public bool TryGetChunk(ChunkPosition position, out StageDefinition chunk) =>
        chunks.TryGetValue((position.X, position.Y), out chunk);
}
