using System;
using System.Collections.Generic;
using LegendsOfFurry.Content.Contracts;

/// <summary>
/// 在基础 Provider 上叠加人工覆盖。修改先进入覆盖层和日志，不依赖驻留的 GameObject。
/// </summary>
public sealed class EditableChunkProvider : IChunkProvider
{
    private readonly IChunkProvider source;
    private readonly Dictionary<(int x, int y), StageDefinition> overlays =
        new Dictionary<(int, int), StageDefinition>();
    private readonly HashSet<(int x, int y)> dirty = new HashSet<(int, int)>();
    private readonly List<ChunkEdit> journal = new List<ChunkEdit>();

    public EditableChunkProvider(IChunkProvider source, IEnumerable<StageDefinition> overlayChunks = null)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        World = source.World;
        if (overlayChunks == null) return;
        foreach (StageDefinition stage in overlayChunks)
        {
            if (stage == null) continue;
            overlays[(stage.GridX, stage.GridY)] = stage;
        }
    }

    public WorldDefinition World { get; }
    public int DirtyCount => dirty.Count;
    public int OverlayCount => overlays.Count;
    public int JournalCount => journal.Count;

    public bool CanProvide(ChunkPosition position) =>
        overlays.ContainsKey((position.X, position.Y)) || source.CanProvide(position);

    public bool IsResident(ChunkPosition position) =>
        overlays.ContainsKey((position.X, position.Y)) || source.IsResident(position);

    public bool IsOverlay(ChunkPosition position) => overlays.ContainsKey((position.X, position.Y));

    public bool IsDirty(ChunkPosition position) => dirty.Contains((position.X, position.Y));

    public bool TryGetChunk(ChunkPosition position, out StageDefinition chunk)
    {
        if (overlays.TryGetValue((position.X, position.Y), out chunk))
            return true;
        return source.TryGetChunk(position, out chunk);
    }

    public bool TryGetOrigin(ChunkPosition position, out ChunkOrigin origin)
    {
        if (overlays.ContainsKey((position.X, position.Y)))
        {
            origin = source is ProceduralChunkProvider ? ChunkOrigin.Overlay : ChunkOrigin.Authored;
            return true;
        }

        return source.TryGetOrigin(position, out origin);
    }

    public void Replace(ChunkPosition position, StageDefinition chunk)
    {
        if (chunk == null) throw new ArgumentNullException(nameof(chunk));
        chunk.GridX = position.X;
        chunk.GridY = position.Y;
        overlays.TryGetValue((position.X, position.Y), out StageDefinition previous);
        if (previous == null)
            source.TryGetChunk(position, out previous);
        journal.Add(new ChunkEdit(position, previous?.Clone(), chunk.Clone()));
        overlays[(position.X, position.Y)] = chunk;
        dirty.Add((position.X, position.Y));
        SyncWorldStages();
    }

    public bool TryRemoveOverlay(ChunkPosition position)
    {
        if (!overlays.TryGetValue((position.X, position.Y), out StageDefinition previous))
            return false;
        journal.Add(new ChunkEdit(position, previous.Clone(), null));
        overlays.Remove((position.X, position.Y));
        dirty.Add((position.X, position.Y));
        source.Release(position);
        SyncWorldStages();
        return true;
    }

    public IEnumerable<StageDefinition> OverlayChunks() => overlays.Values;

    public IEnumerable<ChunkPosition> DirtyPositions()
    {
        foreach ((int x, int y) in dirty)
            yield return new ChunkPosition(x, y);
    }

    public void ClearDirty() => dirty.Clear();

    public void Release(ChunkPosition position)
    {
        if (dirty.Contains((position.X, position.Y))) return;
        source.Release(position);
    }

    public StageDefinition EnsureOverlay(ChunkPosition position)
    {
        if (overlays.TryGetValue((position.X, position.Y), out StageDefinition existing) && existing != null)
            return existing;
        if (!source.TryGetChunk(position, out StageDefinition generated) || generated == null)
            return null;
        StageDefinition clone = generated.Clone();
        clone.GridX = position.X;
        clone.GridY = position.Y;
        overlays[(position.X, position.Y)] = clone;
        dirty.Add((position.X, position.Y));
        SyncWorldStages();
        return clone;
    }

    public void MarkDirty(ChunkPosition position) => dirty.Add((position.X, position.Y));

    private void SyncWorldStages()
    {
        if (World?.Stages == null) return;
        if (World.IsInfinite)
        {
            World.Stages.Clear();
            foreach (StageDefinition stage in overlays.Values)
                World.Stages.Add(stage);
            return;
        }

        foreach (StageDefinition overlay in overlays.Values)
        {
            bool replaced = false;
            for (int i = 0; i < World.Stages.Count; i++)
            {
                StageDefinition current = World.Stages[i];
                if (current == null || current.GridX != overlay.GridX || current.GridY != overlay.GridY)
                    continue;
                World.Stages[i] = overlay;
                replaced = true;
                break;
            }

            if (!replaced) World.Stages.Add(overlay);
        }
    }

    public readonly struct ChunkEdit
    {
        public readonly ChunkPosition Position;
        public readonly StageDefinition Before;
        public readonly StageDefinition After;

        public ChunkEdit(ChunkPosition position, StageDefinition before, StageDefinition after)
        {
            Position = position;
            Before = before;
            After = after;
        }
    }
}
