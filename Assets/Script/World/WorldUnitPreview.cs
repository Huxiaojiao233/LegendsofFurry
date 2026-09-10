using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;
using UnityEngine;

/// <summary>把关卡 UnitPlacements 生成到格子上的战斗棋子外观，不含战斗逻辑。</summary>
public static class WorldUnitPreview
{
    public static void SpawnAll(BoardGenerator board, StageDefinition stage)
    {
        if (board == null || stage?.UnitPlacements == null || board.WorldSource == null) return;
        for (int i = 0; i < stage.UnitPlacements.Count; i++)
            SpawnOnBoard(board, stage, stage.UnitPlacements[i]);
    }

    public static GameObject SpawnOnBoard(BoardGenerator board, StageDefinition stage,
        WorldUnitPlacementDefinition placement)
    {
        if (board == null || stage == null || placement == null || !placement.Enabled) return null;
        if (string.IsNullOrWhiteSpace(placement.UnitId) || board.WorldSource == null) return null;
        Vector2Int world = WorldLayout.ToBoard(stage, placement.LocalX, placement.LocalY, board.WorldSource);
        if (!board.TryGetCell(world.x, world.y, out BoardCell host) || host == null) return null;
        return Spawn(placement, host.transform);
    }

    public static GameObject Spawn(WorldUnitPlacementDefinition placement, Transform parent)
    {
        if (placement == null || !placement.Enabled || string.IsNullOrWhiteSpace(placement.UnitId))
            return null;

        UnitDefinition definition = null;
        if (ContentRuntime.IsLoaded)
            ContentRuntime.Registry.TryGetUnit(placement.UnitId, out definition);

        string objectName = string.IsNullOrWhiteSpace(placement.InstanceId)
            ? placement.UnitId
            : placement.InstanceId;
        GameObject instance = CombatantTokenFactory.SpawnPreview(definition, objectName);
        instance.transform.SetParent(parent, false);
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;
        instance.transform.localPosition = new Vector3(0f, WorldTerrain.StepY, 0f);

        WorldUnitPreviewMarker marker = instance.GetComponent<WorldUnitPreviewMarker>() ??
                                        instance.AddComponent<WorldUnitPreviewMarker>();
        marker.InstanceId = placement.InstanceId ?? string.Empty;
        marker.UnitId = placement.UnitId;
        return instance;
    }

    public static void ClearOn(BoardCell cell)
    {
        if (cell == null) return;
        WorldUnitPreviewMarker[] markers = cell.GetComponentsInChildren<WorldUnitPreviewMarker>(true);
        for (int i = 0; i < markers.Length; i++)
        {
            if (markers[i] == null) continue;
            GameObject go = markers[i].gameObject;
            if (Application.isPlaying) Object.Destroy(go);
            else Object.DestroyImmediate(go);
        }
    }

    public static void ClearAll(BoardGenerator board, StageDefinition stage)
    {
        if (board == null || stage?.UnitPlacements == null || board.WorldSource == null) return;
        for (int i = 0; i < stage.UnitPlacements.Count; i++)
        {
            WorldUnitPlacementDefinition placement = stage.UnitPlacements[i];
            if (placement == null) continue;
            Vector2Int world = WorldLayout.ToBoard(stage, placement.LocalX, placement.LocalY, board.WorldSource);
            if (board.TryGetCell(world.x, world.y, out BoardCell host))
                ClearOn(host);
        }
    }
}

/// <summary>编辑预览标记，方便查找且与战斗 Unit 区分。</summary>
public sealed class WorldUnitPreviewMarker : MonoBehaviour
{
    public string InstanceId;
    public string UnitId;
}
