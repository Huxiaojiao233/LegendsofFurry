#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using LegendsOfFurry.Content.Contracts;
using NUnit.Framework;
using UnityEngine;

public sealed class WorldEditorSessionTests
{
    private readonly List<string> folders = new List<string>();

    [TearDown]
    public void TearDown()
    {
        for (int i = 0; i < folders.Count; i++)
        {
            if (Directory.Exists(folders[i]))
                Directory.Delete(folders[i], true);
        }

        folders.Clear();
    }

    [Test]
    public void StrokeMergesIntoSingleUndo()
    {
        WorldEditorSession session = CreateTempFinite();
        session.Commands.BeginStroke("绘制地形");
        session.PaintTerrain(0, 0, WorldTerrainCatalog.Stone);
        session.PaintTerrain(1, 0, WorldTerrainCatalog.Stone);
        session.PaintTerrain(2, 0, WorldTerrainCatalog.Water);
        session.Commands.EndStroke();
        Assert.That(session.Commands.UndoCount, Is.EqualTo(1));
        Assert.That(session.Commands.IsDirty, Is.True);
        Assert.That(TerrainAt(session, 0, 0), Is.EqualTo(WorldTerrainCatalog.Stone));
        Assert.That(TerrainAt(session, 2, 0), Is.EqualTo(WorldTerrainCatalog.Water));

        session.Commands.Undo();
        Assert.That(TerrainAt(session, 0, 0), Is.EqualTo(WorldTerrainCatalog.Grass));
        Assert.That(TerrainAt(session, 2, 0), Is.EqualTo(WorldTerrainCatalog.Grass));
        session.Commands.Redo();
        Assert.That(TerrainAt(session, 0, 0), Is.EqualTo(WorldTerrainCatalog.Stone));
        Assert.That(TerrainAt(session, 2, 0), Is.EqualTo(WorldTerrainCatalog.Water));
    }

    [Test]
    public void ReadOnlySessionRejectsPaintUntilDuplicated()
    {
        WorldDefinition world = WorldMapIO.CreateBlank("readonly-edit", "只读", 2, 1, 4);
        WorldEditorSession readOnly = WorldEditorSession.Wrap(world, writable: false);
        readOnly.PaintTerrain(0, 0, WorldTerrainCatalog.Road);
        Assert.That(TerrainAt(readOnly, 0, 0), Is.EqualTo(WorldTerrainCatalog.Grass));
        Assert.That(readOnly.Status, Does.Contain("只读"));

        WorldEditorSession writable = readOnly.DuplicateToWritable("readonly-edit-copy", "可写副本");
        folders.Add(WorldEditorSession.WritableFolder("readonly-edit-copy"));
        writable.PaintTerrain(0, 0, WorldTerrainCatalog.Road);
        Assert.That(TerrainAt(writable, 0, 0), Is.EqualTo(WorldTerrainCatalog.Road));
        Assert.That(writable.IsReadOnly, Is.False);
    }

    [Test]
    public void FinitePaintKeepsNeighborChunks()
    {
        WorldEditorSession session = CreateTempFinite();
        Assert.That(session.World.Stages.Count, Is.EqualTo(4));
        session.PaintTerrain(0, 0, WorldTerrainCatalog.Dirt);
        Assert.That(session.World.Stages.Count, Is.EqualTo(4));
        Assert.That(session.Provider.IsDirty(new ChunkPosition(0, 0)), Is.True);
        Assert.That(session.Provider.TryGetChunk(new ChunkPosition(1, 0), out StageDefinition east));
        Assert.That(east.TerrainAt(0, 0, 4, 4), Is.EqualTo(WorldTerrainCatalog.Grass));
    }

    [Test]
    public void StageInspectorAndStartTileUseActualStageId()
    {
        WorldEditorSession session = CreateTempFinite();
        ChunkPosition chunk = new ChunkPosition(1, 0);
        Assert.That(session.TryUpdateStage(chunk, "custom-stage", "自定义关卡", ContentStageTypeKeys.Reward,
            "reward-a", "key-a", "key-b", out string reason), Is.True, reason);
        session.SetStartTile(4, 0);
        Assert.That(session.World.StartStageId, Is.EqualTo("custom-stage"));
        Assert.That(session.World.StartChunkX, Is.EqualTo(1));
        Assert.That(session.Provider.TryGetChunk(chunk, out StageDefinition stage), Is.True);
        Assert.That(stage.StageType, Is.EqualTo(ContentStageTypeKeys.Reward));
        session.Commands.Undo();
        session.Commands.Undo();
        Assert.That(stage.StageId, Is.EqualTo("cell-1-0"));
    }

    [Test]
    public void SeparateEraseBrushesOnlyEraseTheirOwnKind()
    {
        WorldEditorSession session = CreateTempFinite();
        Assert.That(session.TryPlaceDecoration(0, 0, new WorldDecorationDefinition
        {
            Id = "deco", Definition = WorldDecorationCatalog.DefaultId, LocalX = 0, LocalY = 0
        }, out string decorationReason), Is.True, decorationReason);
        Assert.That(session.TryPlaceUnit(1, 0, new WorldUnitPlacementDefinition
        {
            InstanceId = "unit", UnitId = "slime", LocalX = 1, LocalY = 0, Enabled = true
        }, out string unitReason), Is.True, unitReason);
        Assert.That(session.EraseDecorationAt(0, 0), Is.True);
        Assert.That(session.EraseUnitAt(1, 0), Is.True);
    }

    [Test]
    public void ControllerDoesNotSpawnCanvas()
    {
        int canvasesBefore = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude).Length;
        GameObject root = new GameObject("EditorRoot");
        try
        {
            root.AddComponent<WorldEditorView>();
            root.AddComponent<WorldEditorInputController>();
            RuntimeWorldEditorController controller = root.AddComponent<RuntimeWorldEditorController>();
            Assert.That(controller.Session, Is.Null);
            Assert.That(Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude).Length,
                Is.EqualTo(canvasesBefore));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private WorldEditorSession CreateTempFinite()
    {
        string id = "edit-test-" + Path.GetRandomFileName().Replace(".", "");
        WorldEditorSession session = WorldEditorSession.CreateFinite(id, "测试图", 2, 2, 4);
        folders.Add(WorldEditorSession.WritableFolder(id));
        return session;
    }

    private static string TerrainAt(WorldEditorSession session, int x, int z)
    {
        Assert.That(session.TryGetStage(x, z, out StageDefinition stage, out int lx, out int ly, out _));
        return stage.TerrainAt(lx, ly, session.World.TerrainWidth, session.World.TerrainHeight);
    }
}
#endif
