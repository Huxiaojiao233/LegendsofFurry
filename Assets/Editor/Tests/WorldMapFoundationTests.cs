#if UNITY_EDITOR
using System.Collections.Generic;
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

    private static void AssertPath(WorldDefinition world, string fromId, string toId)
    {
        StageDefinition from = world.Stages.Single(item => item.StageId == fromId);
        StageDefinition to = world.Stages.Single(item => item.StageId == toId);
        Assert.That(System.Math.Abs(from.GridX - to.GridX) + System.Math.Abs(from.GridY - to.GridY),
            Is.EqualTo(1), $"{fromId} 应与 {toId} 相邻。");
    }
}
#endif
