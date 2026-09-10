using System;
using System.Collections.Generic;
using System.IO;
using LegendsOfFurry.Content.Contracts;
using UnityEngine;

/// <summary>地图列表中的一项。只读来源必须先复制到可写层才能保存。</summary>
public readonly struct WorldListEntry
{
    public readonly string WorldId;
    public readonly string DisplayName;
    public readonly bool Writable;
    public readonly bool Infinite;
    public readonly string Mode;

    public WorldListEntry(string worldId, string displayName, bool writable, bool infinite, string mode)
    {
        WorldId = worldId;
        DisplayName = displayName;
        Writable = writable;
        Infinite = infinite;
        Mode = mode;
    }
}

/// <summary>游戏内世界编辑会话：打开、复制、绘制、校验和保存，不创建 UI。</summary>
public sealed class WorldEditorSession
{
    public WorldDefinition World { get; private set; }
    public EditableChunkProvider Provider { get; private set; }
    public WorldEditorCommandStack Commands { get; } = new WorldEditorCommandStack();
    public bool IsReadOnly { get; private set; }
    public string Folder { get; private set; }
    public string Status { get; private set; } = string.Empty;

    public event Action<ChunkPosition> ChunkChanged;
    public event Action StatusChanged;

    public static string WritableFolder(string worldId) =>
        Path.Combine(WorldMapIO.UserWorldsRoot, worldId);

    public static bool IsWritable(string worldId) =>
        !string.IsNullOrWhiteSpace(worldId) && Directory.Exists(WritableFolder(worldId));

    public static List<WorldListEntry> ListWorlds()
    {
        WorldCatalog.Reload();
        IReadOnlyList<WorldDefinition> worlds = WorldCatalog.Worlds;
        var list = new List<WorldListEntry>(worlds.Count);
        for (int i = 0; i < worlds.Count; i++)
        {
            WorldDefinition world = worlds[i];
            if (world == null) continue;
            list.Add(new WorldListEntry(
                world.WorldId,
                world.DisplayName,
                IsWritable(world.WorldId),
                world.IsInfinite,
                world.Mode));
        }

        return list;
    }

    public static WorldEditorSession Open(string worldId)
    {
        WorldCatalog.Reload();
        if (!WorldCatalog.TryGet(worldId, out WorldDefinition world) || world == null)
            throw new InvalidDataException($"找不到地图 {worldId}。");
        return FromWorld(world, IsWritable(world.WorldId));
    }

    public static WorldEditorSession CreateFinite(string worldId, string displayName, int chunksX, int chunksY,
        int chunkSize)
    {
        WorldDefinition world = WorldMapIO.CreateBlank(worldId, displayName, chunksX, chunksY, chunkSize);
        WorldMapIO.SaveUserWorld(world);
        WorldCatalog.Reload();
        return Open(world.WorldId);
    }

    public static WorldEditorSession CreateInfinite(string worldId, string displayName, int seed, int chunkSize)
    {
        WorldDefinition world = WorldMapIO.CreateInfinite(worldId, displayName, seed, chunkSize);
        WorldMapIO.SaveUserWorld(world);
        WorldCatalog.Reload();
        return Open(world.WorldId);
    }

    public static WorldEditorSession ImportFolder(string folder)
    {
        WorldDefinition world = WorldMapIO.LoadChunked(folder);
        string target = WritableFolder(world.WorldId);
        WorldMapIO.SaveChunked(world, target);
        WorldCatalog.Reload();
        return Open(world.WorldId);
    }

    public static bool TryDeleteUserWorld(string worldId, out string error)
    {
        error = string.Empty;
        if (!IsWritable(worldId))
        {
            error = "只能删除可写副本，不能删除内容包里的只读地图。";
            return false;
        }

        string folder = WritableFolder(worldId);
        Directory.Delete(folder, true);
        WorldCatalog.Reload();
        return true;
    }

    public WorldEditorSession DuplicateToWritable(string newWorldId, string displayName)
    {
        if (World == null) throw new InvalidOperationException("没有打开的地图。");
        string oldId = World.WorldId;
        string oldName = World.DisplayName;
        World.WorldId = newWorldId;
        World.DisplayName = string.IsNullOrWhiteSpace(displayName) ? newWorldId : displayName;
        try
        {
            WorldMapIO.SaveChunked(World, WritableFolder(newWorldId));
        }
        finally
        {
            World.WorldId = oldId;
            World.DisplayName = oldName;
        }

        WorldCatalog.Reload();
        return Open(newWorldId);
    }

    public List<string> Validate() => World == null ? new List<string> { "没有打开的地图。" } : WorldMapIO.Validate(World);

    public void ReportStatus(string value) => SetStatus(value);

    public void UpdateWorldSettings(string displayName, int seed)
    {
        if (!EnsureWritable()) return;
        string nextName = string.IsNullOrWhiteSpace(displayName) ? World.WorldId : displayName.Trim();
        string oldName = World.DisplayName;
        int oldSeed = World.Seed;
        if (oldName == nextName && oldSeed == seed) return;
        Commands.Execute("修改地图属性",
            () =>
            {
                World.DisplayName = nextName;
                World.Seed = seed;
                SetStatus("地图属性已更新。");
            },
            () =>
            {
                World.DisplayName = oldName;
                World.Seed = oldSeed;
                SetStatus("已撤销地图属性修改。");
            });
    }

    public bool TryUpdateStage(ChunkPosition chunk, string stageId, string displayName, string stageType,
        string rewardPoolId, string requiredKeyId, string dropKeyId, out string reason)
    {
        reason = string.Empty;
        if (!EnsureWritable() || !Provider.TryGetChunk(chunk, out StageDefinition current) || current == null)
        {
            reason = "找不到要编辑的关卡。";
            return false;
        }

        string nextId = string.IsNullOrWhiteSpace(stageId) ? WorldStageIds.FromChunk(chunk) : stageId.Trim();
        string nextType = string.IsNullOrWhiteSpace(stageType) ? ContentStageTypeKeys.Battle : stageType.Trim();
        if (!ContentId.IsValid(nextId))
        {
            reason = "关卡 ID 只能使用小写字母、数字、点、下划线和短横线。";
            return false;
        }
        if (!ContentStageTypeKeys.IsKnown(nextType))
        {
            reason = "未知的关卡类型。";
            return false;
        }

        foreach (StageDefinition stage in Provider.OverlayChunks())
        {
            if (stage == null || (stage.GridX == chunk.X && stage.GridY == chunk.Y)) continue;
            if (string.Equals(stage.StageId, nextId, StringComparison.OrdinalIgnoreCase))
            {
                reason = "关卡 ID 已被其他关卡使用。";
                return false;
            }
        }

        string oldId = current.StageId;
        string oldName = current.DisplayName;
        string oldType = current.StageType;
        string oldReward = current.RewardPoolId;
        string oldRequired = current.RequiredKeyId;
        string oldDrop = current.DropKeyId;
        Commands.Execute("修改关卡属性",
            () => ApplyStageSettings(chunk, nextId, displayName, nextType, rewardPoolId, requiredKeyId, dropKeyId),
            () => ApplyStageSettings(chunk, oldId, oldName, oldType, oldReward, oldRequired, oldDrop));
        return true;
    }

    public void Save()
    {
        EnsureWritable();
        List<string> issues = Validate();
        if (issues.Count > 0)
        {
            SetStatus("无法保存：" + issues[0]);
            return;
        }

        WorldMapIO.SaveUserWorld(World);
        Provider.ClearDirty();
        Commands.MarkSaved();
        WorldCatalog.Reload();
        SetStatus("已保存到 " + Folder);
    }

    public void Reload()
    {
        WorldEditorSession reloaded = Open(World.WorldId);
        World = reloaded.World;
        Provider = reloaded.Provider;
        Folder = reloaded.Folder;
        IsReadOnly = reloaded.IsReadOnly;
        Commands.Clear();
        SetStatus("已重新加载。未保存的编辑已丢弃。");
    }

    public bool TryGetStage(int worldX, int worldZ, out StageDefinition stage, out int localX, out int localY,
        out ChunkPosition chunk)
    {
        stage = null;
        localX = 0;
        localY = 0;
        chunk = default;
        if (World == null) return false;
        int tw = Mathf.Max(1, World.TerrainWidth);
        int th = Mathf.Max(1, World.TerrainHeight);
        WorldCoords.ToChunk(worldX, worldZ, tw, th, out chunk, out localX, out localY);
        return Provider.TryGetChunk(chunk, out stage) && stage != null;
    }

    public void PaintTerrain(int worldX, int worldZ, string terrainId)
    {
        if (!TryPrepareEdit(worldX, worldZ, out StageDefinition stage, out int lx, out int ly, out ChunkPosition chunk))
            return;
        int tw = World.TerrainWidth;
        int th = World.TerrainHeight;
        string before = stage.TerrainAt(lx, ly, tw, th);
        if (before == terrainId) return;
        Commands.Execute("绘制地形",
            () =>
            {
                StageDefinition live = Provider.EnsureOverlay(chunk);
                live.SetTerrain(lx, ly, tw, th, terrainId);
                NotifyChunk(chunk);
            },
            () =>
            {
                StageDefinition live = Provider.EnsureOverlay(chunk);
                live.SetTerrain(lx, ly, tw, th, before);
                NotifyChunk(chunk);
            });
    }

    public void PaintHeight(int worldX, int worldZ, int height)
    {
        if (!TryPrepareEdit(worldX, worldZ, out StageDefinition stage, out int lx, out int ly, out ChunkPosition chunk))
            return;
        int tw = World.TerrainWidth;
        int th = World.TerrainHeight;
        int before = stage.HeightAt(lx, ly, tw, th);
        int after = Mathf.Clamp(height, WorldTerrain.MinHeight, WorldTerrain.MaxHeight);
        if (before == after) return;
        Commands.Execute("调整高度",
            () =>
            {
                StageDefinition live = Provider.EnsureOverlay(chunk);
                live.SetHeight(lx, ly, tw, th, after);
                NotifyChunk(chunk);
            },
            () =>
            {
                StageDefinition live = Provider.EnsureOverlay(chunk);
                live.SetHeight(lx, ly, tw, th, before);
                NotifyChunk(chunk);
            });
    }

    public bool TryPlaceDecoration(int worldX, int worldZ, WorldDecorationDefinition placement, out string reason)
    {
        reason = string.Empty;
        if (!TryPrepareEdit(worldX, worldZ, out StageDefinition stage, out _, out _, out ChunkPosition chunk))
        {
            reason = "无法编辑该格。";
            return false;
        }

        if (!CanPlaceObject(stage, placement, out reason)) return false;
        Commands.Execute("放置对象",
            () =>
            {
                StageDefinition live = Provider.EnsureOverlay(chunk);
                live.Decorations.Add(placement);
                NotifyChunk(chunk);
            },
            () =>
            {
                StageDefinition live = Provider.EnsureOverlay(chunk);
                live.Decorations.Remove(placement);
                NotifyChunk(chunk);
            });
        return true;
    }

    public bool TryPlaceUnit(int worldX, int worldZ, WorldUnitPlacementDefinition placement, out string reason)
    {
        reason = string.Empty;
        if (!TryPrepareEdit(worldX, worldZ, out StageDefinition stage, out _, out _, out ChunkPosition chunk))
        {
            reason = "无法编辑该格。";
            return false;
        }

        foreach (WorldUnitPlacementDefinition existing in stage.UnitPlacements)
        {
            if (existing != null && existing.Enabled && existing.LocalX == placement.LocalX &&
                existing.LocalY == placement.LocalY)
            {
                reason = "同一格只能部署一个单位。";
                return false;
            }
        }

        Commands.Execute("部署单位",
            () =>
            {
                StageDefinition live = Provider.EnsureOverlay(chunk);
                live.UnitPlacements.Add(placement);
                NotifyChunk(chunk);
            },
            () =>
            {
                StageDefinition live = Provider.EnsureOverlay(chunk);
                live.UnitPlacements.Remove(placement);
                NotifyChunk(chunk);
            });
        return true;
    }

    public void EraseAt(int worldX, int worldZ)
    {
        if (EraseDecorationAt(worldX, worldZ)) return;
        EraseUnitAt(worldX, worldZ);
    }

    public bool EraseDecorationAt(int worldX, int worldZ)
    {
        if (!TryPrepareEdit(worldX, worldZ, out StageDefinition stage, out int lx, out int ly, out ChunkPosition chunk))
            return false;
        WorldDecorationDefinition deco = null;
        for (int i = stage.Decorations.Count - 1; i >= 0; i--)
        {
            if (!Covers(stage.Decorations[i], lx, ly)) continue;
            deco = stage.Decorations[i];
            break;
        }

        if (deco == null) return false;
        int index = stage.Decorations.IndexOf(deco);
        Commands.Execute("删除对象",
            () =>
            {
                StageDefinition live = Provider.EnsureOverlay(chunk);
                live.Decorations.Remove(deco);
                NotifyChunk(chunk);
            },
            () =>
            {
                StageDefinition live = Provider.EnsureOverlay(chunk);
                if (!live.Decorations.Contains(deco))
                    live.Decorations.Insert(Mathf.Clamp(index, 0, live.Decorations.Count), deco);
                NotifyChunk(chunk);
            });
        return true;
    }

    public bool EraseUnitAt(int worldX, int worldZ)
    {
        if (!TryPrepareEdit(worldX, worldZ, out StageDefinition stage, out int lx, out int ly, out ChunkPosition chunk))
            return false;
        WorldUnitPlacementDefinition unit = stage.UnitPlacements.Find(item =>
            item != null && item.Enabled && item.LocalX == lx && item.LocalY == ly);
        if (unit == null) return false;
        int unitIndex = stage.UnitPlacements.IndexOf(unit);
        Commands.Execute("撤除单位",
            () =>
            {
                StageDefinition live = Provider.EnsureOverlay(chunk);
                live.UnitPlacements.Remove(unit);
                NotifyChunk(chunk);
            },
            () =>
            {
                StageDefinition live = Provider.EnsureOverlay(chunk);
                if (!live.UnitPlacements.Contains(unit))
                    live.UnitPlacements.Insert(Mathf.Clamp(unitIndex, 0, live.UnitPlacements.Count), unit);
                NotifyChunk(chunk);
            });
        return true;
    }

    public void GenerateVisible(IEnumerable<ChunkPosition> chunks, WorldNoiseSettings settings)
    {
        if (!EnsureWritable()) return;
        if (settings == null) settings = new WorldNoiseSettings { Seed = World.Seed };
        World.Seed = settings.Seed;
        World.GeneratorId = WorldNoiseGenerator.GeneratorId;
        List<ChunkPosition> list = new List<ChunkPosition>(chunks);
        Commands.BeginStroke("生成可见区域");
        for (int i = 0; i < list.Count; i++)
        {
            ChunkPosition position = list[i];
            StageDefinition before = Provider.TryGetChunk(position, out StageDefinition current)
                ? current.Clone() : null;
            StageDefinition generated = WorldNoiseGenerator.GenerateChunk(World, position, settings);
            Commands.Execute("生成块",
                () =>
                {
                    Provider.Replace(position, generated.Clone());
                    NotifyChunk(position);
                },
                () =>
                {
                    if (before != null) Provider.Replace(position, before.Clone());
                    else Provider.TryRemoveOverlay(position);
                    NotifyChunk(position);
                });
        }

        Commands.EndStroke();
        SetStatus($"已生成 {list.Count} 个可见区块。");
    }

    public void SetStartTile(int worldX, int worldZ)
    {
        if (!EnsureWritable()) return;
        int tw = Mathf.Max(1, World.TerrainWidth);
        int th = Mathf.Max(1, World.TerrainHeight);
        WorldCoords.ToChunk(worldX, worldZ, tw, th, out ChunkPosition chunk, out int lx, out int ly);
        if (!Provider.TryGetChunk(chunk, out StageDefinition selectedStage) || selectedStage == null)
        {
            SetStatus("出生点必须位于已有关卡中。");
            return;
        }
        int oldChunkX = World.StartChunkX;
        int oldChunkY = World.StartChunkY;
        int oldTileX = World.StartTileX;
        int oldTileY = World.StartTileY;
        string oldStage = World.StartStageId;
        Commands.Execute("设置出生点",
            () =>
            {
                World.StartChunkX = chunk.X;
                World.StartChunkY = chunk.Y;
                World.StartTileX = lx;
                World.StartTileY = ly;
                World.StartStageId = selectedStage.StageId;
            },
            () =>
            {
                World.StartChunkX = oldChunkX;
                World.StartChunkY = oldChunkY;
                World.StartTileX = oldTileX;
                World.StartTileY = oldTileY;
                World.StartStageId = oldStage;
            });
        SetStatus($"出生点：块 {chunk} 格 {lx},{ly}。");
    }

    public static WorldEditorSession Wrap(WorldDefinition world, bool writable) => FromWorld(world, writable);

    private static WorldEditorSession FromWorld(WorldDefinition world, bool writable)
    {
        IChunkProvider inner = WorldMapIO.CreateProvider(world);
        EditableChunkProvider editable = inner as EditableChunkProvider ??
                                         new EditableChunkProvider(inner, world.Stages);
        return new WorldEditorSession
        {
            World = world,
            Provider = editable,
            IsReadOnly = !writable,
            Folder = writable ? WritableFolder(world.WorldId) : string.Empty,
            Status = writable ? $"已打开 “{world.DisplayName}”。" : $"只读来源 “{world.DisplayName}”，请先复制为可写副本。"
        };
    }

    private bool TryPrepareEdit(int worldX, int worldZ, out StageDefinition stage, out int lx, out int ly,
        out ChunkPosition chunk)
    {
        stage = null;
        lx = 0;
        ly = 0;
        chunk = default;
        if (!EnsureWritable()) return false;
        if (!TryGetStage(worldX, worldZ, out _, out lx, out ly, out chunk)) return false;
        stage = Provider.EnsureOverlay(chunk);
        return stage != null;
    }

    private bool EnsureWritable()
    {
        if (!IsReadOnly) return true;
        SetStatus("当前地图只读。请先复制到可写层再编辑。");
        return false;
    }

    private bool CanPlaceObject(StageDefinition stage, WorldDecorationDefinition candidate, out string reason)
    {
        reason = string.Empty;
        int tw = World.TerrainWidth;
        int th = World.TerrainHeight;
        for (int y = candidate.LocalY; y < candidate.LocalY + candidate.EffectiveHeight; y++)
        for (int x = candidate.LocalX; x < candidate.LocalX + candidate.EffectiveWidth; x++)
        {
            if (x < 0 || y < 0 || x >= tw || y >= th)
            {
                reason = "对象不能跨出当前区块；请在区块内选择锚点。";
                return false;
            }

            foreach (WorldDecorationDefinition existing in stage.Decorations)
            {
                if (Covers(existing, x, y))
                {
                    reason = "对象不能和已有对象占用同一格。";
                    return false;
                }
            }
        }

        return true;
    }

    private static bool Covers(WorldDecorationDefinition item, int x, int y) =>
        item != null && x >= item.LocalX && y >= item.LocalY &&
        x < item.LocalX + item.EffectiveWidth && y < item.LocalY + item.EffectiveHeight;

    private void ApplyStageSettings(ChunkPosition chunk, string stageId, string displayName, string stageType,
        string rewardPoolId, string requiredKeyId, string dropKeyId)
    {
        StageDefinition live = Provider.EnsureOverlay(chunk);
        if (live == null) return;
        string previousId = live.StageId;
        live.StageId = stageId;
        live.DisplayName = string.IsNullOrWhiteSpace(displayName) ? stageId : displayName.Trim();
        live.StageType = stageType;
        live.RewardPoolId = rewardPoolId?.Trim() ?? string.Empty;
        live.RequiredKeyId = requiredKeyId?.Trim() ?? string.Empty;
        live.DropKeyId = dropKeyId?.Trim() ?? string.Empty;
        if (World.StartChunkX == chunk.X && World.StartChunkY == chunk.Y ||
            string.Equals(World.StartStageId, previousId, StringComparison.Ordinal))
            World.StartStageId = stageId;
        NotifyChunk(chunk);
        SetStatus("关卡属性已更新。");
    }

    private void NotifyChunk(ChunkPosition chunk)
    {
        Provider.MarkDirty(chunk);
        ChunkChanged?.Invoke(chunk);
    }

    private void SetStatus(string value)
    {
        Status = value ?? string.Empty;
        StatusChanged?.Invoke();
    }
}
