#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LegendsOfFurry.Content.Contracts;
using NUnit.Framework;
using UnityEngine;

public sealed class WorldMapFoundationTests
{
    [Test]
    public void DemoWorldAdjacentBordersShareHeight()
    {
        WorldDefinition world = WorldCatalog.CreateDemoWorld();
        Assert.That(WorldTerrain.FindBorderMismatches(world), Is.Empty);
        Assert.That(world.StageGridWidth, Is.EqualTo(5));
        Assert.That(world.TerrainWidth, Is.EqualTo(10));
    }

    [Test]
    public void DemoPathIsAdjacentThroughKeyAndBoss()
    {
        WorldDefinition world = WorldCatalog.CreateDemoWorld();
        AssertPath(world, "start", "fight-1");
        AssertPath(world, "fight-1", "reward-1");
        AssertPath(world, "reward-1", "fight-2");
        AssertPath(world, "fight-2", "shop-1");
        AssertPath(world, "shop-1", "fight-3");
        AssertPath(world, "reward-1", "fight-4");
        AssertPath(world, "reward-1", "boss-1");

        StageDefinition keyFight = world.Stages.Single(item => item.StageId == "fight-3");
        Assert.That(keyFight.DropKeyId, Is.EqualTo("boss-key"));
        StageDefinition boss = world.Stages.Single(item => item.StageId == "boss-1");
        Assert.That(boss.RequiredKeyId, Is.EqualTo("boss-key"));
        Assert.That(boss.StageType, Is.EqualTo(ContentStageTypeKeys.Boss));
    }

    [Test]
    public void BoardCoordsMapOntoDemoStages()
    {
        WorldDefinition world = WorldCatalog.CreateDemoWorld();
        Vector2Int startCenter = WorldLayout.StageCenterCell(world.Stages[0], world);
        Assert.That(WorldLayout.TryGetStage(world, startCenter.x, startCenter.y, out StageDefinition start));
        Assert.That(start.StageId, Is.EqualTo("start"));

        Vector2Int fightCell = WorldLayout.ToBoard(
            world.Stages.Single(item => item.StageId == "fight-1"), 0, 0, world);
        Assert.That(WorldLayout.TryGetStage(world, fightCell.x, fightCell.y, out StageDefinition fight));
        Assert.That(fight.StageId, Is.EqualTo("fight-1"));
        Assert.That(WorldLayout.IsOrthogonalNeighbor(start, fight));
    }

    [Test]
    public void CurrentStageHasAtMostFourOrthogonalNeighbors()
    {
        WorldDefinition world = WorldCatalog.CreateDemoWorld();
        StageDefinition start = world.Stages.Single(item => item.StageId == "start");
        List<StageDefinition> neighbors = WorldLayout.OrthogonalNeighbors(world, start);
        Assert.That(neighbors.Count, Is.EqualTo(1));
        Assert.That(neighbors[0].StageId, Is.EqualTo("fight-1"));

        StageDefinition reward = world.Stages.Single(item => item.StageId == "reward-1");
        List<string> ids = WorldLayout.OrthogonalNeighbors(world, reward).ConvertAll(item => item.StageId);
        Assert.That(ids, Is.EquivalentTo(new[] { "fight-1", "fight-2", "fight-4", "boss-1" }));
    }

    [Test]
    public void NegativeWorldTilesUseFloorDivision()
    {
        Assert.That(WorldCoords.FloorDiv(-1, 10), Is.EqualTo(-1));
        Assert.That(WorldCoords.FloorMod(-1, 10), Is.EqualTo(9));
        WorldCoords.ToChunk(-1, 0, 10, 10, out ChunkPosition chunk, out int localX, out int localY);
        Assert.That(chunk.X, Is.EqualTo(-1));
        Assert.That(localX, Is.EqualTo(9));
        Assert.That(localY, Is.EqualTo(0));
        Vector2Int world = WorldCoords.ToWorldTile(chunk, localX, localY, 10, 10);
        Assert.That(world, Is.EqualTo(new Vector2Int(-1, 0)));
    }

    [Test]
    public void FiniteProviderServesAuthoredChunksOnly()
    {
        WorldDefinition world = WorldCatalog.CreateDemoWorld();
        FiniteChunkProvider provider = new FiniteChunkProvider(world);
        Assert.That(provider.CanProvide(new ChunkPosition(0, 2)), Is.True);
        Assert.That(provider.TryGetChunk(new ChunkPosition(0, 2), out StageDefinition start), Is.True);
        Assert.That(start.StageId, Is.EqualTo("start"));
        Assert.That(provider.CanProvide(new ChunkPosition(9, 9)), Is.False);
    }

    [Test]
    public void OneHeightLevelIsHalfAWorldUnit()
    {
        Assert.That(WorldTerrain.StepY, Is.EqualTo(0.5f));
        Assert.That(WorldTerrain.MinHeight, Is.EqualTo(-5));
        Assert.That(WorldTerrain.MaxHeight, Is.EqualTo(10));
        Assert.That(WorldTerrain.MaxHeight * WorldTerrain.StepY, Is.EqualTo(5f));
    }

    [Test]
    public void PerlinNoiseGenerationIsDeterministicForSameSeed()
    {
        WorldDefinition first = WorldMapIO.CreateBlank("noise-a", "噪声A", 2, 2, 10);
        WorldDefinition second = WorldMapIO.CreateBlank("noise-b", "噪声B", 2, 2, 10);
        WorldNoiseSettings settings = new WorldNoiseSettings { Seed = 4242 };
        WorldNoiseGenerator.Generate(first, settings);
        WorldNoiseGenerator.Generate(second, settings);
        Assert.That(first.GeneratorId, Is.EqualTo(WorldNoiseGenerator.GeneratorId));
        Assert.That(first.Seed, Is.EqualTo(4242));
        Assert.That(WorldMapIO.Validate(first), Is.Empty);
        Assert.That(first.Stages.Count, Is.EqualTo(second.Stages.Count));
        for (int i = 0; i < first.Stages.Count; i++)
        {
            StageDefinition a = first.Stages[i];
            StageDefinition b = second.Stages[i];
            Assert.That(a.Heights, Is.EqualTo(b.Heights));
            Assert.That(a.TerrainIds, Is.EqualTo(b.TerrainIds));
            Assert.That(a.StageType, Is.EqualTo(b.StageType));
            Assert.That(a.Decorations.Count, Is.EqualTo(b.Decorations.Count));
            Assert.That(a.UnitPlacements.Count, Is.EqualTo(b.UnitPlacements.Count));
        }

        Assert.That(first.StartTileX, Is.EqualTo(second.StartTileX));
        Assert.That(first.StartTileY, Is.EqualTo(second.StartTileY));
        Assert.That(WorldTerrain.FindBorderMismatches(first), Is.Empty);
    }

    [Test]
    public void HeightRangeClampsToEditorLimits()
    {
        WorldDefinition world = WorldMapIO.CreateBlank("height-clamp", "高度夹紧", 1, 1, 4);
        StageDefinition stage = world.Stages[0];
        stage.SetHeight(0, 0, 4, 4, -99);
        stage.SetHeight(1, 0, 4, 4, 99);
        Assert.That(stage.HeightAt(0, 0, 4, 4), Is.EqualTo(WorldTerrain.MinHeight));
        Assert.That(stage.HeightAt(1, 0, 4, 4), Is.EqualTo(WorldTerrain.MaxHeight));
    }

    [Test]
    public void ChunkedWorldRoundTripKeepsTerrainAndStart()
    {
        string folder = Path.Combine(Path.GetTempPath(), "lofe-world-map-test");
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        WorldDefinition source = WorldMapIO.CreateBlank("roundtrip", "往返测试", 2, 2, 10);
        StageDefinition start = source.Stages[0];
        start.SetTerrain(1, 1, 10, 10, WorldTerrainCatalog.Water);
        start.SetHeight(1, 1, 10, 10, -1);
        start.Decorations.Add(new WorldDecorationDefinition
        {
            Id = "tree-01",
            Definition = WorldTerrainCatalog.Tree,
            LocalX = 2,
            LocalY = 3
        });
        start.UnitPlacements.Add(new WorldUnitPlacementDefinition
        {
            InstanceId = "roundtrip-slime-1",
            UnitId = "slime",
            LocalX = 7,
            LocalY = 5,
            FactionOverride = "enemy",
            ControllerOverride = "ai",
            Enabled = true
        });
        source.Seed = 77;
        source.GeneratorId = "hand";
        WorldMapIO.SaveChunked(source, folder);
        WorldDefinition loaded = WorldMapIO.LoadChunked(folder);
        Assert.That(loaded.WorldId, Is.EqualTo("roundtrip"));
        Assert.That(loaded.Seed, Is.EqualTo(77));
        Assert.That(loaded.GeneratorId, Is.EqualTo("hand"));
        Assert.That(loaded.Stages.Count, Is.EqualTo(4));
        Assert.That(WorldMapIO.Validate(loaded), Is.Empty);
        WorldCatalog.TryGetStageAt(loaded, 0, 0, out StageDefinition loadedStart);
        Assert.That(loadedStart.TerrainAt(1, 1, 10, 10), Is.EqualTo(WorldTerrainCatalog.Water));
        Assert.That(loadedStart.HeightAt(1, 1, 10, 10), Is.EqualTo(-1));
        Assert.That(loadedStart.Decorations, Has.Count.EqualTo(1));
        Assert.That(loadedStart.Decorations[0].Definition, Is.EqualTo(WorldTerrainCatalog.Tree));
        Assert.That(loadedStart.UnitPlacements, Has.Count.EqualTo(1));
        Assert.That(loadedStart.UnitPlacements[0].UnitId, Is.EqualTo("slime"));
        Assert.That(loadedStart.UnitPlacements[0].LocalX, Is.EqualTo(7));
    }

    [Test]
    public void WorldValidationRejectsOverlappingOrOutOfBoundsUnitPlacements()
    {
        WorldDefinition world = WorldMapIO.CreateBlank("badplacements", "错误部署", 1, 1, 10);
        StageDefinition stage = world.Stages[0];
        stage.UnitPlacements.Add(new WorldUnitPlacementDefinition
        {
            InstanceId = "same", UnitId = "slime", LocalX = 1, LocalY = 1, Enabled = true
        });
        stage.UnitPlacements.Add(new WorldUnitPlacementDefinition
        {
            InstanceId = "same", UnitId = "taigao", LocalX = 1, LocalY = 1, Enabled = true
        });
        stage.UnitPlacements.Add(new WorldUnitPlacementDefinition
        {
            InstanceId = "outside", UnitId = "slime", LocalX = 10, LocalY = 0, Enabled = true
        });

        List<string> issues = WorldMapIO.Validate(world);
        Assert.That(issues.Any(item => item.Contains("实例 ID")), Is.True);
        Assert.That(issues.Any(item => item.Contains("重叠部署")), Is.True);
        Assert.That(issues.Any(item => item.Contains("超出区块范围")), Is.True);
    }

    [Test]
    public void ChunkedWorldRoundTripKeepsMultiCellObjectAndContentPackReference()
    {
        string folder = Path.Combine(Path.GetTempPath(), "lofe-world-object-footprint-test");
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        WorldDefinition source = WorldMapIO.CreateBlank("objects", "对象测试", 1, 1, 8);
        source.ContentPackId = "lofe-core";
        StageDefinition stage = source.Stages[0];
        stage.Decorations.Add(new WorldDecorationDefinition
        {
            Id = "gate", Definition = "base.gate", LocalX = 1, LocalY = 2,
            Rotation = 90, FootprintWidth = 3, FootprintHeight = 2
        });

        WorldMapIO.SaveChunked(source, folder);
        WorldDefinition loaded = WorldMapIO.LoadChunked(folder);
        WorldDecorationDefinition gate = loaded.Stages[0].Decorations.Single();
        Assert.That(loaded.ContentPackId, Is.EqualTo("lofe-core"));
        Assert.That(gate.FootprintWidth, Is.EqualTo(3));
        Assert.That(gate.FootprintHeight, Is.EqualTo(2));
        Assert.That(gate.EffectiveWidth, Is.EqualTo(2));
        Assert.That(gate.EffectiveHeight, Is.EqualTo(3));
        Assert.That(WorldMapIO.Validate(loaded), Is.Empty);
    }

    [Test]
    public void FiniteWorldRejectsMissingChunkFile()
    {
        string folder = Path.Combine(Path.GetTempPath(), "lofe-world-missing-chunk");
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        WorldDefinition source = WorldMapIO.CreateBlank("missingchunk", "缺块", 2, 1, 10);
        WorldMapIO.SaveChunked(source, folder);
        File.Delete(Path.Combine(folder, "chunks", "1_0.json"));
        Assert.Throws<InvalidDataException>(() => WorldMapIO.LoadChunked(folder));
    }

    [Test]
    public void DemoWorldHasWalkableTerrainAtStart()
    {
        WorldDefinition world = WorldCatalog.CreateDemoWorld();
        WorldCatalog.TryGetStage(world, "start", out StageDefinition start);
        Assert.That(start.TerrainIds.Length, Is.EqualTo(100));
        Assert.That(WorldTerrainCatalog.IsWalkable(start.TerrainAt(5, 5, 10, 10)), Is.True);
    }

    [Test]
    public void ForestPrefabNamesBecomeValidContentIds()
    {
        Assert.That(WorldDecorationCatalog.IdFromPrefabName("P_HS_LP_Tree_Oak_01"), Is.EqualTo("hs.tree.oak.01"));
        Assert.That(WorldDecorationCatalog.IdFromPrefabName("P_HS_LP_Bush_Berry_Blue_01"),
            Is.EqualTo("hs.bush.berry.blue.01"));
        Assert.That(ContentId.IsValid(WorldDecorationCatalog.IdFromPrefabName("P_HS_LP_Bush_Berry_Blue_01")));
        Assert.That(WorldDecorationCatalog.CanonicalId(WorldTerrainCatalog.Tree), Is.EqualTo("hs.tree.oak.01"));
        Assert.That(WorldDecorationCatalog.CanonicalId(WorldTerrainCatalog.Rock), Is.EqualTo("hs.rock.medium.01"));
    }

    [Test]
    public void ForestEssentialsPrefabsAreDecorationResources()
    {
        WorldDecorationCatalog.Refresh();
        Assert.That(WorldDecorationCatalog.All.Count, Is.GreaterThan(100));
        Assert.That(WorldDecorationCatalog.TryGet(WorldTerrainCatalog.Tree, out WorldDecorationEntry tree));
        Assert.That(tree.Id, Is.EqualTo("hs.tree.oak.01"));
        Assert.That(WorldDecorationCatalog.LoadPrefab(WorldTerrainCatalog.Tree), Is.Not.Null);
        Assert.That(WorldDecorationCatalog.LoadPrefab("hs.rock.medium.01"), Is.Not.Null);
    }

    [Test]
    public void CloudSeaCoversUnexploredOnly()
    {
        StageDefinition current = new StageDefinition { StageId = "here", GridX = 1, GridY = 1 };
        StageDefinition east = new StageDefinition { StageId = "east", GridX = 2, GridY = 1 };
        StageDefinition far = new StageDefinition { StageId = "far", GridX = 3, GridY = 1 };
        Assert.That(WorldCloudRules.HasCloudOver(current, current, true, false), Is.False);
        Assert.That(WorldCloudRules.HasCloudOver(current, east, true, false), Is.False);
        Assert.That(WorldCloudRules.HasCloudOver(current, east, false, false), Is.True);
        Assert.That(WorldCloudRules.HasCloudOver(current, far, true, false), Is.False);
        Assert.That(WorldCloudRules.HasCloudOver(current, far, false, false), Is.True);
        Assert.That(WorldCloudRules.HasCloudOver(current, null, false, false), Is.True);
        Assert.That(WorldCloudRules.HasCloudOver(current, east, true, true), Is.False);
    }

    [Test]
    public void AuthoredDemoFolderLoadsTwentyFiveChunks()
    {
        string packWorld = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            Application.dataPath, "..", "..", "ContentProjects", "lofe_core", "Worlds", "demo"));
        string streaming = Path.Combine(Application.streamingAssetsPath, "Content", "Worlds", "demo");
        bool hasPack = File.Exists(Path.Combine(packWorld, "world.json"));
        bool hasStreaming = File.Exists(Path.Combine(streaming, "world.json"));
        if (!hasPack && !hasStreaming)
            Assert.Ignore("demo 世界还没写到内容工程 Worlds 或 StreamingAssets。");

        if (hasPack)
        {
            WorldDefinition pack = WorldMapIO.LoadChunked(packWorld);
            Assert.That(pack.FormatVersion, Is.EqualTo(1));
            Assert.That(pack.Stages.Count, Is.EqualTo(25));
            Assert.That(WorldMapIO.Validate(pack), Is.Empty);
        }

        if (!hasStreaming) return;
        WorldDefinition world = WorldMapIO.LoadChunked(streaming);
        Assert.That(world.FormatVersion, Is.EqualTo(1));
        Assert.That(world.Stages.Count, Is.EqualTo(25));
        Assert.That(world.StartStageId, Is.EqualTo("start"));
        Assert.That(WorldMapIO.Validate(world), Is.Empty);
        WorldCatalog.TryGetStage(world, "start", out StageDefinition start);
        Assert.That(start.TerrainAt(0, 5, 10, 10), Is.EqualTo(WorldTerrainCatalog.Grass));
        Assert.That(start.TerrainAt(1, 1, 10, 10), Is.EqualTo(WorldTerrainCatalog.Water));
        Assert.That(start.Decorations, Is.Not.Empty);
    }

    [Test]
    public void AuthoredDemoDeploysSlimeOnBattlesAndTaigaoOnBoss()
    {
        string packWorld = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            Application.dataPath, "..", "..", "ContentProjects", "lofe_core", "Worlds", "demo"));
        string streaming = Path.Combine(Application.streamingAssetsPath, "Content", "Worlds", "demo");
        string folder = File.Exists(Path.Combine(packWorld, "world.json")) ? packWorld
            : File.Exists(Path.Combine(streaming, "world.json")) ? streaming : null;
        if (folder == null)
            Assert.Ignore("demo 世界还没写到内容工程 Worlds 或 StreamingAssets。");
        WorldDefinition world = WorldMapIO.LoadChunked(folder);
        WorldUnitPlacementDefinition[] battleUnits = world.Stages
            .Where(stage => stage.StageType == ContentStageTypeKeys.Battle)
            .SelectMany(stage => stage.UnitPlacements)
            .Where(item => item != null && item.Enabled)
            .ToArray();
        WorldUnitPlacementDefinition[] bossUnits = world.Stages
            .Where(stage => stage.StageType == ContentStageTypeKeys.Boss)
            .SelectMany(stage => stage.UnitPlacements)
            .Where(item => item != null && item.Enabled)
            .ToArray();
        Assert.That(battleUnits, Is.Not.Empty);
        Assert.That(battleUnits.All(item => item.UnitId == "slime"), Is.True);
        Assert.That(bossUnits.Any(item => item.UnitId == "taigao"), Is.True);
        Assert.That(bossUnits.All(item => item.UnitId != "slime"), Is.True);
    }

    [Test]
    public void EnteringConfiguredTerrainAppliesMatchingStatus()
    {
        UnityEngine.GameObject owner = new UnityEngine.GameObject("TerrainStatusOwner");
        try
        {
            Unit unit = owner.AddComponent<Unit>();
            StatusDefinition wet = new StatusDefinition { StatusId = "wet", Enabled = true };
            wet.ApplyOnTerrainIds.Add(WorldTerrainCatalog.Water);

            TerrainMovementRuntime.ApplyStatusesForTerrain(unit, WorldTerrainCatalog.Water, new[] { wet });
            Assert.That(unit.State.Has("wet"), Is.True);

            TerrainMovementRuntime.ApplyStatusesForTerrain(unit, WorldTerrainCatalog.Grass, new[] { wet });
            Assert.That(unit.State.Get("wet"), Is.EqualTo(1));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(owner);
        }
    }

    private static void AssertPath(WorldDefinition world, string fromId, string toId)
    {
        StageDefinition from = world.Stages.Single(item => item.StageId == fromId);
        StageDefinition to = world.Stages.Single(item => item.StageId == toId);
        Assert.That(System.Math.Abs(from.GridX - to.GridX) + System.Math.Abs(from.GridY - to.GridY),
            Is.EqualTo(1), $"{fromId} 应与 {toId} 相邻。");
    }

    [Test]
    public void CoordinateStageIdsAreReversibleAndValidContentIds()
    {
        ChunkPosition[] samples =
        {
            new ChunkPosition(0, 0),
            new ChunkPosition(2, 3),
            new ChunkPosition(-1, 2),
            new ChunkPosition(3, -4),
            new ChunkPosition(-1, -1),
            new ChunkPosition(-12, 0)
        };
        foreach (ChunkPosition position in samples)
        {
            string id = WorldStageIds.FromChunk(position);
            Assert.That(ContentId.IsValid(id), Is.True, id);
            Assert.That(WorldStageIds.TryParse(id, out ChunkPosition parsed), Is.True, id);
            Assert.That(parsed.X, Is.EqualTo(position.X));
            Assert.That(parsed.Y, Is.EqualTo(position.Y));
        }
    }

    [Test]
    public void HashGeneratorGoldenVectorsMatchIntegerMix()
    {
        Assert.That(WorldIntegerHash.Mix(0, 0, 4242), Is.EqualTo(391329929));
        Assert.That(WorldIntegerHash.HeightAt(0, 0, 4242), Is.EqualTo(4));
        Assert.That(WorldIntegerHash.MoisturePermille(0, 0, 4242), Is.EqualTo(163));
        Assert.That(WorldIntegerHash.ChooseTerrain(4, 163), Is.EqualTo(WorldTerrainCatalog.Dirt));
        Assert.That(WorldIntegerHash.HeightAt(-3, -2, 7), Is.EqualTo(1));
        Assert.That(WorldIntegerHash.MoisturePermille(-3, -2, 7), Is.EqualTo(921));
        Assert.That(WorldIntegerHash.ChooseTerrain(1, 921), Is.EqualTo(WorldTerrainCatalog.Forest));
        Assert.That(WorldIntegerHash.HeightAt(1, -1, 1), Is.EqualTo(1));
        Assert.That(WorldIntegerHash.ChooseTerrain(1, 73), Is.EqualTo(WorldTerrainCatalog.Sand));
    }

    [Test]
    public void SameSeedGeneratesIdenticalChunksIndependently()
    {
        WorldDefinition world = WorldMapIO.CreateInfinite("hash-world", "哈希", 4242, 10);
        StageDefinition first = WorldNoiseGenerator.GenerateChunk(world, new ChunkPosition(-2, 3));
        StageDefinition second = WorldNoiseGenerator.GenerateChunk(world, new ChunkPosition(-2, 3));
        Assert.That(first.StageId, Is.EqualTo(WorldStageIds.FromChunk(new ChunkPosition(-2, 3))));
        Assert.That(ContentId.IsValid(first.StageId));
        Assert.That(first.Heights, Is.EqualTo(second.Heights));
        Assert.That(first.TerrainIds, Is.EqualTo(second.TerrainIds));
        Assert.That(first.StageType, Is.EqualTo(second.StageType));
    }

    [Test]
    public void IndependentlyGeneratedNeighborsShareSeamHeights()
    {
        WorldDefinition world = WorldMapIO.CreateInfinite("seams", "接缝", 4242, 10);
        world.GeneratorId = WorldNoiseGenerator.GeneratorId;
        StageDefinition west = WorldNoiseGenerator.GenerateChunk(world, new ChunkPosition(0, 0));
        StageDefinition east = WorldNoiseGenerator.GenerateChunk(world, new ChunkPosition(1, 0));
        StageDefinition north = WorldNoiseGenerator.GenerateChunk(world, new ChunkPosition(0, 1));
        WorldDefinition probe = new WorldDefinition
        {
            TerrainWidth = 10,
            TerrainHeight = 10,
            Stages = { west, east, north }
        };
        Assert.That(WorldTerrain.FindBorderMismatches(probe), Is.Empty);
    }

    [Test]
    public void InfiniteWorldSavesOnlySparseOverlayChunks()
    {
        string folder = Path.Combine(Path.GetTempPath(), "lofe-world-infinite-sparse");
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        WorldDefinition world = WorldMapIO.CreateInfinite("endless", "无尽", 9, 10);
        IChunkProvider provider = WorldMapIO.CreateProvider(world);
        Assert.That(provider.TryGetChunk(new ChunkPosition(0, 0), out StageDefinition start));
        Assert.That(start.StageId, Is.EqualTo("start"));
        Assert.That(provider.TryGetOrigin(new ChunkPosition(4, -3), out ChunkOrigin generated));
        Assert.That(generated, Is.EqualTo(ChunkOrigin.Generated));

        EditableChunkProvider editable = (EditableChunkProvider)provider;
        Assert.That(editable.TryGetChunk(new ChunkPosition(-1, 0), out StageDefinition overlay));
        overlay.SetTerrain(2, 2, 10, 10, WorldTerrainCatalog.Road);
        editable.Replace(new ChunkPosition(-1, 0), overlay);

        WorldMapIO.SaveChunked(world, folder);
        string[] files = Directory.GetFiles(Path.Combine(folder, "chunks"), "*.json");
        Assert.That(files.Length, Is.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(folder, "chunks", "-1_0.json")));

        WorldDefinition loaded = WorldMapIO.LoadChunked(folder);
        Assert.That(loaded.FormatVersion, Is.EqualTo(WorldDefinition.FormatVersionV2));
        Assert.That(loaded.IsInfinite);
        Assert.That(loaded.GeneratorId, Is.EqualTo(WorldIntegerHash.GeneratorId));
        Assert.That(loaded.Stages.Count, Is.EqualTo(1));
        Assert.That(loaded.Stages[0].TerrainAt(2, 2, 10, 10), Is.EqualTo(WorldTerrainCatalog.Road));

        IChunkProvider reloaded = WorldMapIO.CreateProvider(loaded);
        Assert.That(reloaded.TryGetOrigin(new ChunkPosition(-1, 0), out ChunkOrigin origin));
        Assert.That(origin, Is.EqualTo(ChunkOrigin.Overlay));
        Assert.That(reloaded.TryGetChunk(new ChunkPosition(0, 0), out StageDefinition generatedStart));
        Assert.That(generatedStart.StageId, Is.EqualTo("start"));
        Assert.That(WorldMapIO.Validate(loaded), Is.Empty);
    }

    [Test]
    public void FormatV1HeaderWithoutOptionalFieldsStillLoads()
    {
        string folder = Path.Combine(Path.GetTempPath(), "lofe-world-v1-compat");
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        WorldDefinition source = WorldMapIO.CreateBlank("legacy", "旧图", 1, 1, 4);
        source.FormatVersion = 1;
        WorldMapIO.SaveChunked(source, folder);
        File.WriteAllText(Path.Combine(folder, "world.json"),
            "{\n\"formatVersion\":1,\n\"id\":\"legacy\",\n\"displayName\":\"旧图\",\n\"mode\":\"finite\",\n" +
            "\"chunkSize\":[4,4],\n\"startChunk\":[0,0],\n\"startTile\":[2,2],\n" +
            "\"boundsMin\":[0,0],\n\"boundsMax\":[0,0],\n\"startStageId\":\"start\",\n" +
            "\"seed\":0,\n\"generatorId\":\"hand\"\n}\n");
        WorldDefinition loaded = WorldMapIO.LoadChunked(folder);
        Assert.That(loaded.FormatVersion, Is.EqualTo(1));
        Assert.That(loaded.ContentPackId, Is.EqualTo(string.Empty));
        Assert.That(loaded.GeneratorVersion, Is.EqualTo(0));
        Assert.That(loaded.IsInfinite, Is.False);
        Assert.That(loaded.Stages.Count, Is.EqualTo(1));
    }

    [Test]
    public void ProviderReleaseDoesNotDropDirtyOverlay()
    {
        WorldDefinition world = WorldMapIO.CreateInfinite("dirty", "脏块", 3, 10);
        EditableChunkProvider provider = (EditableChunkProvider)WorldMapIO.CreateProvider(world);
        Assert.That(provider.TryGetChunk(new ChunkPosition(2, 2), out StageDefinition chunk));
        chunk.SetHeight(0, 0, 10, 10, 4);
        provider.Replace(new ChunkPosition(2, 2), chunk);
        provider.Release(new ChunkPosition(2, 2));
        Assert.That(provider.IsResident(new ChunkPosition(2, 2)));
        Assert.That(provider.IsDirty(new ChunkPosition(2, 2)));
        Assert.That(provider.TryGetChunk(new ChunkPosition(2, 2), out StageDefinition kept));
        Assert.That(kept.HeightAt(0, 0, 10, 10), Is.EqualTo(4));
    }

    [Test]
    public void NegativeBoardTilesResolveThroughProvider()
    {
        WorldDefinition world = WorldMapIO.CreateInfinite("neg", "负坐标", 5, 10);
        IChunkProvider provider = WorldMapIO.CreateProvider(world);
        Assert.That(WorldLayout.TryGetStage(provider, -1, 0, out StageDefinition stage));
        Assert.That(stage.GridX, Is.EqualTo(-1));
        Assert.That(stage.GridY, Is.EqualTo(0));
    }
}
#endif
