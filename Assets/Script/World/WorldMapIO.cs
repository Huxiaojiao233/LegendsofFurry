using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;
using UnityEngine;

/// <summary>
/// 读写世界分块目录。优先内容包 worlds/，StreamingAssets/Content/Worlds 仅作兼容回退。
/// </summary>
public static class WorldMapIO
{
    public static string WorldsRoot =>
        Path.Combine(Application.streamingAssetsPath, "Content", "Worlds");

    public static string WorldFolder(string worldId) => Path.Combine(WorldsRoot, worldId);

    public static string ChunkFileName(int x, int y) => $"{x}_{y}.json";

    public static List<WorldDefinition> LoadAll()
    {
        Dictionary<string, WorldDefinition> byId =
            new Dictionary<string, WorldDefinition>(StringComparer.Ordinal);
        LoadAllFromRoot(WorldsRoot, byId, replaceExisting: false);
        if (ContentRuntime.IsLoaded)
        {
            foreach (ContentPackLoadInfo pack in ContentRuntime.LoadedPacks)
            {
                if (pack == null || string.IsNullOrEmpty(pack.Directory)) continue;
                LoadAllFromRoot(Path.Combine(pack.Directory, "worlds"), byId, replaceExisting: true);
            }
        }

        return new List<WorldDefinition>(byId.Values);
    }

    /// <summary>从指定根目录加载全部世界；replaceExisting 为真时同 ID 覆盖（内容包优先）。</summary>
    public static void LoadAllFromRoot(string root, Dictionary<string, WorldDefinition> byId, bool replaceExisting)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root) || byId == null) return;
        foreach (string directory in Directory.GetDirectories(root))
        {
            string worldPath = Path.Combine(directory, "world.json");
            if (!File.Exists(worldPath)) continue;
            try
            {
                WorldDefinition world = LoadChunked(directory);
                if (byId.ContainsKey(world.WorldId) && !replaceExisting) continue;
                byId[world.WorldId] = world;
            }
            catch (Exception exception)
            {
                Debug.LogError($"世界地图 {worldPath} 读取失败：{exception.Message}");
            }
        }
    }

    public static List<WorldDefinition> LoadAllFromRoot(string root)
    {
        Dictionary<string, WorldDefinition> byId =
            new Dictionary<string, WorldDefinition>(StringComparer.Ordinal);
        LoadAllFromRoot(root, byId, replaceExisting: true);
        return new List<WorldDefinition>(byId.Values);
    }

    public static WorldDefinition LoadChunked(string folder)
    {
        string worldPath = Path.Combine(folder, "world.json");
        WorldHeaderDto header = JsonUtility.FromJson<WorldHeaderDto>(File.ReadAllText(worldPath));
        if (header == null || !ContentId.IsValid(header.id))
            throw new InvalidDataException($"世界 ID 无效：{worldPath}");

        WorldDefinition world = HeaderToWorld(header);
        string chunkDir = Path.Combine(folder, "chunks");
        if (!Directory.Exists(chunkDir))
            throw new InvalidDataException($"世界 {world.WorldId} 缺少 chunks 目录。");

        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
        for (int gy = world.BoundsMinY; gy <= world.BoundsMaxY; gy++)
        {
            for (int gx = world.BoundsMinX; gx <= world.BoundsMaxX; gx++)
            {
                string chunkPath = Path.Combine(chunkDir, ChunkFileName(gx, gy));
                if (!File.Exists(chunkPath))
                    throw new InvalidDataException($"有限世界 {world.WorldId} 缺块 {gx}_{gy}。");
                StageDefinition stage = ReadChunk(chunkPath, world.TerrainWidth, world.TerrainHeight);
                if (stage.GridX != gx || stage.GridY != gy)
                    throw new InvalidDataException($"块 {chunkPath} 的坐标与文件名不一致。");
                if (!stage.Enabled) continue;
                if (!ids.Add(stage.StageId))
                    throw new InvalidDataException($"关卡 {stage.StageId} 的 ID 重复。");
                world.Stages.Add(stage);
            }
        }

        if (string.IsNullOrEmpty(world.StartStageId) ||
            world.Stages.Find(item => item.StageId == world.StartStageId) == null)
            throw new InvalidDataException($"世界 {world.WorldId} 的起始关卡不存在。");

        WorldTerrain.FillMissingHeights(world);
        return world;
    }

    public static void SaveChunked(WorldDefinition world) => SaveChunked(world, WorldFolder(world.WorldId));

    public static void SaveChunked(WorldDefinition world, string folder)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
        if (!ContentId.IsValid(world.WorldId))
            throw new InvalidDataException($"世界 ID 无效：{world.WorldId}");
        if (string.IsNullOrEmpty(folder)) folder = WorldFolder(world.WorldId);

        string chunkDir = Path.Combine(folder, "chunks");
        Directory.CreateDirectory(chunkDir);
        File.WriteAllText(Path.Combine(folder, "world.json"), JsonUtility.ToJson(WorldToHeader(world), true),
            new UTF8Encoding(false));

        Dictionary<(int, int), StageDefinition> byGrid = new Dictionary<(int, int), StageDefinition>();
        foreach (StageDefinition stage in world.Stages)
        {
            if (stage == null) continue;
            byGrid[(stage.GridX, stage.GridY)] = stage;
        }

        int tw = Mathf.Max(1, world.TerrainWidth);
        int th = Mathf.Max(1, world.TerrainHeight);
        for (int gy = world.BoundsMinY; gy <= world.BoundsMaxY; gy++)
        {
            for (int gx = world.BoundsMinX; gx <= world.BoundsMaxX; gx++)
            {
                StageDefinition stage;
                if (!byGrid.TryGetValue((gx, gy), out stage) || stage == null)
                    stage = CreateEmptyChunk(world, gx, gy, false);
                stage.EnsureGrids(tw, th);
                File.WriteAllText(Path.Combine(chunkDir, ChunkFileName(gx, gy)),
                    JsonUtility.ToJson(ChunkToDto(stage, tw, th), true), new UTF8Encoding(false));
            }
        }
    }

    public static List<string> Validate(WorldDefinition world)
    {
        List<string> issues = new List<string>();
        if (world == null)
        {
            issues.Add("世界为空。");
            return issues;
        }

        if (!ContentId.IsValid(world.WorldId)) issues.Add("世界 ID 无效。");
        if (world.TerrainWidth <= 0 || world.TerrainHeight <= 0) issues.Add("区块尺寸必须为正。");
        if (world.StageGridWidth <= 0 || world.StageGridHeight <= 0) issues.Add("世界边界必须为正。");
        bool hasStart = false;
        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
        HashSet<(int, int)> cells = new HashSet<(int, int)>();
        int tw = Mathf.Max(1, world.TerrainWidth);
        int th = Mathf.Max(1, world.TerrainHeight);
        foreach (StageDefinition stage in world.Stages)
        {
            if (stage == null || !stage.Enabled) continue;
            if (!ContentId.IsValid(stage.StageId) || !ContentStageTypeKeys.IsKnown(stage.StageType))
                issues.Add($"关卡 {stage.StageId} 的 ID 或类型无效。");
            if (!ids.Add(stage.StageId)) issues.Add($"关卡 {stage.StageId} 的 ID 重复。");
            if (!cells.Add((stage.GridX, stage.GridY))) issues.Add($"关卡 {stage.StageId} 的坐标重复。");
            if (stage.StageId == world.StartStageId) hasStart = true;
            stage.EnsureGrids(tw, th);
            if (stage.Heights.Length != tw * th) issues.Add($"关卡 {stage.StageId} 高度格子数不对。");
            if (stage.TerrainIds.Length != tw * th) issues.Add($"关卡 {stage.StageId} 地形格子数不对。");
            HashSet<string> instanceIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<(int, int)> placementCells = new HashSet<(int, int)>();
            foreach (WorldUnitPlacementDefinition placement in stage.UnitPlacements ?? new List<WorldUnitPlacementDefinition>())
            {
                if (placement == null || !placement.Enabled) continue;
                if (!ContentId.IsValid(placement.UnitId))
                    issues.Add($"关卡 {stage.StageId} 的部署单位 ID 无效：{placement.UnitId}。");
                if (string.IsNullOrWhiteSpace(placement.InstanceId) || !instanceIds.Add(placement.InstanceId))
                    issues.Add($"关卡 {stage.StageId} 的单位实例 ID 为空或重复：{placement.InstanceId}。");
                if (placement.LocalX < 0 || placement.LocalY < 0 || placement.LocalX >= tw || placement.LocalY >= th)
                    issues.Add($"关卡 {stage.StageId} 的单位 {placement.InstanceId} 超出区块范围。");
                if (!placementCells.Add((placement.LocalX, placement.LocalY)))
                    issues.Add($"关卡 {stage.StageId} 在 ({placement.LocalX},{placement.LocalY}) 重叠部署了单位。");
            }
        }

        if (!hasStart) issues.Add("起始关卡不存在。");
        issues.AddRange(WorldTerrain.FindBorderMismatches(world));
        return issues;
    }

    public static WorldDefinition CreateBlank(string worldId, string displayName, int chunksX, int chunksY,
        int chunkSize)
    {
        chunksX = Mathf.Max(1, chunksX);
        chunksY = Mathf.Max(1, chunksY);
        chunkSize = Mathf.Max(2, chunkSize);
        WorldDefinition world = new WorldDefinition
        {
            FormatVersion = 1,
            WorldId = worldId,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? worldId : displayName,
            Mode = "finite",
            StageGridWidth = chunksX,
            StageGridHeight = chunksY,
            TerrainWidth = chunkSize,
            TerrainHeight = chunkSize,
            StartStageId = "start",
            StartTileX = chunkSize / 2,
            StartTileY = chunkSize / 2
        };
        for (int y = 0; y < chunksY; y++)
        {
            for (int x = 0; x < chunksX; x++)
            {
                bool start = x == 0 && y == 0;
                StageDefinition stage = CreateEmptyChunk(world, x, y, true);
                if (start)
                {
                    stage.StageId = "start";
                    stage.DisplayName = "营地";
                    stage.StageType = ContentStageTypeKeys.Start;
                }
                else
                {
                    stage.StageId = $"cell-{x}-{y}";
                    stage.DisplayName = $"区域 {x},{y}";
                    stage.StageType = ContentStageTypeKeys.Battle;
                }

                FillFlatTerrain(stage, chunkSize, chunkSize, 0, WorldTerrainCatalog.Grass);
                world.Stages.Add(stage);
            }
        }

        return world;
    }

    public static StageDefinition CreateEmptyChunk(WorldDefinition world, int gridX, int gridY, bool enabled)
    {
        StageDefinition stage = new StageDefinition
        {
            StageId = $"cell-{gridX}-{gridY}".Replace('_', '-'),
            DisplayName = enabled ? $"区域 {gridX},{gridY}" : "空地",
            StageType = ContentStageTypeKeys.Battle,
            GridX = gridX,
            GridY = gridY,
            Enabled = enabled
        };
        if (stage.StageId[0] == '-') stage.StageId = "cell" + stage.StageId;
        if (!ContentId.IsValid(stage.StageId))
            stage.StageId = $"cell{Math.Abs(gridX)}n{Math.Abs(gridY)}";
        FillFlatTerrain(stage, Mathf.Max(1, world.TerrainWidth), Mathf.Max(1, world.TerrainHeight), 0,
            enabled ? WorldTerrainCatalog.Grass : WorldTerrainCatalog.Void);
        return stage;
    }

    public static void FillFlatTerrain(StageDefinition stage, int width, int height, int heightValue, string terrainId)
    {
        int length = width * height;
        stage.Heights = new int[length];
        stage.TerrainIds = new string[length];
        for (int i = 0; i < length; i++)
        {
            stage.Heights[i] = heightValue;
            stage.TerrainIds[i] = terrainId;
        }
    }

    private static WorldDefinition HeaderToWorld(WorldHeaderDto header)
    {
        int[] size = header.chunkSize;
        int tw = size != null && size.Length > 0 && size[0] > 0 ? size[0] : 10;
        int th = size != null && size.Length > 1 && size[1] > 0 ? size[1] : tw;
        int minX = ReadVec(header.boundsMin, 0, 0);
        int minY = ReadVec(header.boundsMin, 1, 0);
        int maxX = ReadVec(header.boundsMax, 0, minX);
        int maxY = ReadVec(header.boundsMax, 1, minY);
        return new WorldDefinition
        {
            FormatVersion = header.formatVersion > 0 ? header.formatVersion : 1,
            WorldId = header.id,
            DisplayName = string.IsNullOrWhiteSpace(header.displayName) ? header.id : header.displayName,
            Mode = string.IsNullOrWhiteSpace(header.mode) ? "finite" : header.mode,
            BoundsMinX = minX,
            BoundsMinY = minY,
            StageGridWidth = Math.Max(1, maxX - minX + 1),
            StageGridHeight = Math.Max(1, maxY - minY + 1),
            TerrainWidth = tw,
            TerrainHeight = th,
            StartStageId = header.startStageId ?? string.Empty,
            StartTileX = ReadVec(header.startTile, 0, tw / 2),
            StartTileY = ReadVec(header.startTile, 1, th / 2),
            Seed = header.seed,
            GeneratorId = header.generatorId ?? string.Empty
        };
    }

    private static WorldHeaderDto WorldToHeader(WorldDefinition world)
    {
        return new WorldHeaderDto
        {
            formatVersion = world.FormatVersion > 0 ? world.FormatVersion : 1,
            id = world.WorldId,
            displayName = world.DisplayName,
            mode = string.IsNullOrEmpty(world.Mode) ? "finite" : world.Mode,
            chunkSize = new[] { world.TerrainWidth, world.TerrainHeight },
            startChunk = StartChunkOf(world),
            startTile = new[] { world.StartTileX, world.StartTileY },
            boundsMin = new[] { world.BoundsMinX, world.BoundsMinY },
            boundsMax = new[] { world.BoundsMaxX, world.BoundsMaxY },
            startStageId = world.StartStageId,
            seed = world.Seed,
            generatorId = world.GeneratorId
        };
    }

    private static int[] StartChunkOf(WorldDefinition world)
    {
        foreach (StageDefinition stage in world.Stages)
            if (stage != null && stage.StageId == world.StartStageId)
                return new[] { stage.GridX, stage.GridY };
        return new[] { world.BoundsMinX, world.BoundsMinY };
    }

    private static StageDefinition ReadChunk(string path, int width, int height)
    {
        ChunkFileDto dto = JsonUtility.FromJson<ChunkFileDto>(File.ReadAllText(path));
        if (dto == null) throw new InvalidDataException($"块无法解析：{path}");
        int gx = ReadVec(dto.position, 0, 0);
        int gy = ReadVec(dto.position, 1, 0);
        StageDefinition stage = new StageDefinition
        {
            StageId = string.IsNullOrWhiteSpace(dto.id) ? $"cell-{gx}-{gy}" : dto.id,
            DisplayName = string.IsNullOrWhiteSpace(dto.displayName) ? dto.id : dto.displayName,
            StageType = ContentStageTypeKeys.IsKnown(dto.stageType) ? dto.stageType : ContentStageTypeKeys.Battle,
            GridX = gx,
            GridY = gy,
            RewardPoolId = dto.rewardPoolId ?? string.Empty,
            RequiredKeyId = dto.requiredKeyId ?? string.Empty,
            DropKeyId = dto.dropKeyId ?? string.Empty,
            Enabled = dto.enabled
        };
        if (!ContentId.IsValid(stage.StageId))
            throw new InvalidDataException($"关卡 {stage.StageId} 的 ID 无效。");
        AddUnitPlacements(stage, dto.unitPlacements);
        stage.Heights = NormalizeHeights(dto.heights, width, height);
        stage.TerrainIds = DecodeTerrain(dto.terrainLegend, dto.terrain, width, height, stage.Heights);
        if (dto.objects != null)
        {
            foreach (DecorationFileDto item in dto.objects)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.id)) continue;
                stage.Decorations.Add(new WorldDecorationDefinition
                {
                    Id = item.id,
                    Definition = item.definition ?? string.Empty,
                    LocalX = item.x,
                    LocalY = item.y,
                    Rotation = item.rotation
                });
            }
        }

        return stage;
    }

    private static ChunkFileDto ChunkToDto(StageDefinition stage, int width, int height)
    {
        stage.EnsureGrids(width, height);
        Dictionary<string, string> legend = new Dictionary<string, string>();
        string[] rows = new string[height];
        StringBuilder row = new StringBuilder(width);
        for (int y = 0; y < height; y++)
        {
            row.Length = 0;
            for (int x = 0; x < width; x++)
            {
                string id = stage.TerrainAt(x, y, width, height);
                string glyph = WorldTerrainCatalog.GlyphOf(id);
                legend[glyph] = id;
                row.Append(glyph);
            }

            rows[y] = row.ToString();
        }

        List<TerrainLegendDto> legendList = new List<TerrainLegendDto>();
        foreach (KeyValuePair<string, string> pair in legend)
            legendList.Add(new TerrainLegendDto { glyph = pair.Key, definition = pair.Value });

        List<DecorationFileDto> objects = new List<DecorationFileDto>();
        foreach (WorldDecorationDefinition deco in stage.Decorations)
        {
            if (deco == null || string.IsNullOrWhiteSpace(deco.Id)) continue;
            objects.Add(new DecorationFileDto
            {
                id = deco.Id,
                definition = deco.Definition,
                x = deco.LocalX,
                y = deco.LocalY,
                rotation = deco.Rotation
            });
        }

        List<UnitPlacementFileDto> units = new List<UnitPlacementFileDto>();
        foreach (WorldUnitPlacementDefinition placement in stage.UnitPlacements)
        {
            if (placement == null || string.IsNullOrWhiteSpace(placement.UnitId)) continue;
            units.Add(new UnitPlacementFileDto
            {
                instanceId = placement.InstanceId,
                unitId = placement.UnitId,
                x = placement.LocalX,
                y = placement.LocalY,
                factionOverride = placement.FactionOverride,
                controllerOverride = placement.ControllerOverride,
                deckIdOverride = placement.DeckIdOverride,
                enabled = placement.Enabled
            });
        }

        return new ChunkFileDto
        {
            position = new[] { stage.GridX, stage.GridY },
            id = stage.StageId,
            displayName = stage.DisplayName,
            stageType = stage.StageType,
            enabled = stage.Enabled,
            terrainLegend = legendList.ToArray(),
            terrain = rows,
            heights = stage.Heights,
            objects = objects.Count == 0 ? Array.Empty<DecorationFileDto>() : objects.ToArray(),
            unitPlacements = units.ToArray(),
            rewardPoolId = stage.RewardPoolId,
            requiredKeyId = stage.RequiredKeyId,
            dropKeyId = stage.DropKeyId
        };
    }

    /// <summary>读取世界编辑器保存的单位部署实例。</summary>
    private static void AddUnitPlacements(StageDefinition stage, UnitPlacementFileDto[] placements)
    {
        if (placements == null) return;
        foreach (UnitPlacementFileDto item in placements)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.unitId)) continue;
            stage.UnitPlacements.Add(new WorldUnitPlacementDefinition
            {
                InstanceId = string.IsNullOrWhiteSpace(item.instanceId)
                    ? $"{stage.StageId}-{item.unitId}-{item.x}-{item.y}" : item.instanceId,
                UnitId = item.unitId,
                LocalX = item.x,
                LocalY = item.y,
                FactionOverride = item.factionOverride ?? string.Empty,
                ControllerOverride = item.controllerOverride ?? string.Empty,
                DeckIdOverride = item.deckIdOverride ?? string.Empty,
                Enabled = item.enabled
            });
        }
    }

    private static int[] NormalizeHeights(int[] source, int width, int height)
    {
        int length = width * height;
        int[] heights = new int[length];
        if (source == null) return heights;
        int copy = Math.Min(length, source.Length);
        Array.Copy(source, heights, copy);
        return heights;
    }

    private static string[] DecodeTerrain(TerrainLegendDto[] legend, string[] rows, int width, int height, int[] heights)
    {
        Dictionary<char, string> map = new Dictionary<char, string>();
        if (legend != null)
        {
            foreach (TerrainLegendDto item in legend)
            {
                if (item == null || string.IsNullOrEmpty(item.glyph)) continue;
                map[item.glyph[0]] = string.IsNullOrEmpty(item.definition)
                    ? WorldTerrainCatalog.Grass
                    : item.definition;
            }
        }

        string[] ids = new string[width * height];
        for (int y = 0; y < height; y++)
        {
            string row = rows != null && y < rows.Length ? rows[y] ?? string.Empty : string.Empty;
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                if (x < row.Length && map.TryGetValue(row[x], out string id))
                    ids[index] = id;
                else
                    ids[index] = WorldTerrainCatalog.FromHeight(heights != null && index < heights.Length
                        ? heights[index]
                        : 0);
            }
        }

        return ids;
    }

    private static int ReadVec(int[] values, int index, int fallback)
    {
        if (values == null || index < 0 || index >= values.Length) return fallback;
        return values[index];
    }

    [Serializable]
    private sealed class WorldHeaderDto
    {
        public int formatVersion;
        public string id;
        public string displayName;
        public string mode;
        public int[] chunkSize;
        public int[] startChunk;
        public int[] startTile;
        public int[] boundsMin;
        public int[] boundsMax;
        public string startStageId;
        public int seed;
        public string generatorId;
    }

    [Serializable]
    private sealed class ChunkFileDto
    {
        public int[] position;
        public string id;
        public string displayName;
        public string stageType;
        public bool enabled = true;
        public TerrainLegendDto[] terrainLegend;
        public string[] terrain;
        public int[] heights;
        public DecorationFileDto[] objects;
        public UnitPlacementFileDto[] unitPlacements;
        public string rewardPoolId;
        public string requiredKeyId;
        public string dropKeyId;
    }

    [Serializable]
    private sealed class TerrainLegendDto
    {
        public string glyph;
        public string definition;
    }

    [Serializable]
    private sealed class DecorationFileDto
    {
        public string id;
        public string definition;
        public int x;
        public int y;
        public int rotation;
    }

    [Serializable]
    private sealed class UnitPlacementFileDto
    {
        public string instanceId;
        public string unitId;
        public int x;
        public int y;
        public string factionOverride;
        public string controllerOverride;
        public string deckIdOverride;
        public bool enabled = true;
    }
}
