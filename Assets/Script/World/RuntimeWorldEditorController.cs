using System;
using System.Collections.Generic;
using System.IO;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// MAST 风格交互的游戏内世界编辑器。它只编辑 WorldDefinition/Chunk JSON，
/// 绝不引用 UnityEditor，也不把地图保存成 Scene。
/// </summary>
[DefaultExecutionOrder(-10)]
public sealed class RuntimeWorldEditorController : MonoBehaviour
{
    private enum Tool { Terrain, Object, Unit, Erase, Select }

    private sealed class Tile : MonoBehaviour
    {
        [NonSerialized]
        public StageDefinition Stage;
        public int LocalX;
        public int LocalY;
    }

    private sealed class Selection
    {
        public StageDefinition Stage;
        public WorldDecorationDefinition Object;
        public WorldUnitPlacementDefinition Unit;
    }

    private const float CellSize = 1f;
    private readonly WorldEditorCommandStack commands = new WorldEditorCommandStack();
    private readonly Dictionary<Vector2Int, Tile> tiles = new Dictionary<Vector2Int, Tile>();
    private readonly List<Selection> selection = new List<Selection>();

    private WorldDefinition world;
    private Transform terrainRoot;
    private Transform objectRoot;
    private Transform overlayRoot;
    private Camera editorCamera;
    private Text status;
    private GameObject ghost;
    private GameObject boxOverlay;
    private Tool tool = Tool.Terrain;
    private string terrainId = WorldTerrainCatalog.Grass;
    private string objectId = WorldDecorationCatalog.DefaultId;
    private string unitId = string.Empty;
    private int objectWidth = 1;
    private int objectHeight = 1;
    private int objectRotation;
    private Vector2Int? boxStart;
    private Vector2Int hoverCell;
    private bool hasHover;
    private int nextObjectId;
    private int nextUnitId;

    public static void Open()
    {
        SceneManager.LoadScene("S_WorldEditor");
    }

    private void Awake()
    {
        if (!ContentRuntime.IsLoaded) ContentRuntime.EnsureLoaded();
        if (!ContentRuntime.IsLoaded)
        {
            Debug.LogError($"世界编辑器无法读取内容包：{ContentRuntime.LoadError}");
            return;
        }

        world = WorldCatalog.Default;
        if (world == null)
        {
            Debug.LogError("世界编辑器没有可编辑地图。");
            return;
        }

        EnsureCameraAndLight();
        EnsureUi();
        terrainRoot = new GameObject("WorldEditorTerrain").transform;
        objectRoot = new GameObject("WorldEditorObjects").transform;
        overlayRoot = new GameObject("WorldEditorOverlay").transform;
        CreateGhost();
        RefreshView();
        FrameWorld();
        SetStatus("已打开 “" + world.DisplayName + "”。选择资源后在网格上单击放置。");
    }

    private void Update()
    {
        if (world == null || Mouse.current == null || Keyboard.current == null) return;
        HandleShortcuts();
        PanAndZoomCamera();

        bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        Tile tile = null;
        hasHover = !overUi && TryHitTile(out tile);
        if (hasHover)
        {
            hoverCell = ToWorldCell(tile);
            UpdateGhost(tile);
        }
        else if (ghost != null) ghost.SetActive(false);

        if (overUi) return;
        if (tool == Tool.Select)
        {
            HandleBoxSelection(tile);
            return;
        }

        if (hasHover && Mouse.current.leftButton.wasPressedThisFrame)
            ApplyTool(tile);
    }

    private void HandleShortcuts()
    {
        Keyboard keyboard = Keyboard.current;
        bool control = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
        if (control && keyboard.zKey.wasPressedThisFrame) Undo();
        if (control && keyboard.yKey.wasPressedThisFrame) Redo();
        if (keyboard.rKey.wasPressedThisFrame && tool == Tool.Object)
        {
            objectRotation = (objectRotation + 90) % 360;
            SetStatus($"对象旋转：{objectRotation}°。");
        }
        if (keyboard.deleteKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame) DeleteSelection();
        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            selection.Clear();
            boxStart = null;
            RefreshOverlays();
        }
        if (keyboard.digit1Key.wasPressedThisFrame) SetTool(Tool.Terrain);
        if (keyboard.digit2Key.wasPressedThisFrame) SetTool(Tool.Object);
        if (keyboard.digit3Key.wasPressedThisFrame) SetTool(Tool.Unit);
        if (keyboard.digit4Key.wasPressedThisFrame) SetTool(Tool.Erase);
        if (keyboard.digit5Key.wasPressedThisFrame) SetTool(Tool.Select);
        if (keyboard.leftBracketKey.wasPressedThisFrame) ChangeHoveredHeight(-1);
        if (keyboard.rightBracketKey.wasPressedThisFrame) ChangeHoveredHeight(1);
    }

    private void PanAndZoomCamera()
    {
        if (editorCamera == null) return;
        Keyboard keyboard = Keyboard.current;
        Vector3 pan = Vector3.zero;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) pan += Vector3.forward;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) pan += Vector3.back;
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) pan += Vector3.left;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) pan += Vector3.right;
        if (pan != Vector3.zero) editorCamera.transform.position += pan.normalized * (12f * Time.unscaledDeltaTime);
        float scroll = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
            editorCamera.orthographicSize = Mathf.Clamp(editorCamera.orthographicSize - scroll * 0.012f, 4f, 80f);
    }

    private void HandleBoxSelection(Tile tile)
    {
        if (hasHover && Mouse.current.leftButton.wasPressedThisFrame)
            boxStart = ToWorldCell(tile);
        if (boxStart.HasValue && hasHover) DrawBox(boxStart.Value, hoverCell);
        if (!Mouse.current.leftButton.wasReleasedThisFrame || !boxStart.HasValue) return;
        if (hasHover) SelectInBox(boxStart.Value, hoverCell);
        boxStart = null;
        if (boxOverlay != null) boxOverlay.SetActive(false);
    }

    private void ApplyTool(Tile tile)
    {
        switch (tool)
        {
            case Tool.Terrain: PaintTerrain(tile); break;
            case Tool.Object: PlaceObject(tile); break;
            case Tool.Unit: PlaceUnit(tile); break;
            case Tool.Erase: EraseAt(tile); break;
        }
    }

    private void PaintTerrain(Tile tile)
    {
        string before = tile.Stage.TerrainAt(tile.LocalX, tile.LocalY, world.TerrainWidth, world.TerrainHeight);
        if (before == terrainId) return;
        commands.Execute("绘制地形",
            () => { tile.Stage.SetTerrain(tile.LocalX, tile.LocalY, world.TerrainWidth, world.TerrainHeight, terrainId); RefreshView(); },
            () => { tile.Stage.SetTerrain(tile.LocalX, tile.LocalY, world.TerrainWidth, world.TerrainHeight, before); RefreshView(); });
    }

    private void ChangeHoveredHeight(int delta)
    {
        if (!hasHover || !tiles.TryGetValue(hoverCell, out Tile tile)) return;
        int before = tile.Stage.HeightAt(tile.LocalX, tile.LocalY, world.TerrainWidth, world.TerrainHeight);
        int after = Mathf.Clamp(before + delta, WorldTerrain.MinHeight, WorldTerrain.MaxHeight);
        if (before == after) return;
        commands.Execute("调整高度",
            () => { tile.Stage.SetHeight(tile.LocalX, tile.LocalY, world.TerrainWidth, world.TerrainHeight, after); RefreshView(); },
            () => { tile.Stage.SetHeight(tile.LocalX, tile.LocalY, world.TerrainWidth, world.TerrainHeight, before); RefreshView(); });
    }

    private void PlaceObject(Tile tile)
    {
        WorldDecorationDefinition placement = new WorldDecorationDefinition
        {
            Id = $"object-{++nextObjectId}", Definition = WorldDecorationCatalog.CanonicalId(objectId),
            LocalX = tile.LocalX, LocalY = tile.LocalY, Rotation = objectRotation,
            FootprintWidth = objectWidth, FootprintHeight = objectHeight
        };
        if (!CanPlaceObject(tile.Stage, placement, out string reason))
        {
            SetStatus(reason);
            return;
        }

        commands.Execute("放置对象",
            () => { tile.Stage.Decorations.Add(placement); RefreshView(); },
            () => { tile.Stage.Decorations.Remove(placement); RefreshView(); });
    }

    private bool CanPlaceObject(StageDefinition stage, WorldDecorationDefinition candidate, out string reason)
    {
        reason = string.Empty;
        for (int y = candidate.LocalY; y < candidate.LocalY + candidate.EffectiveHeight; y++)
        for (int x = candidate.LocalX; x < candidate.LocalX + candidate.EffectiveWidth; x++)
        {
            if (x < 0 || y < 0 || x >= world.TerrainWidth || y >= world.TerrainHeight)
            {
                reason = "对象不能跨出当前区块；请在区块内选择锚点。";
                return false;
            }
            foreach (WorldDecorationDefinition existing in stage.Decorations)
                if (Covers(existing, x, y))
                {
                    reason = "对象不能和已有对象占用同一格。";
                    return false;
                }
        }
        return true;
    }

    private void PlaceUnit(Tile tile)
    {
        if (string.IsNullOrWhiteSpace(unitId) || !ContentRuntime.Registry.TryGetUnit(unitId, out UnitDefinition unit))
        {
            SetStatus("请先从单位 Palette 选择一个已启用单位。");
            return;
        }
        foreach (WorldUnitPlacementDefinition existing in tile.Stage.UnitPlacements)
            if (existing.Enabled && existing.LocalX == tile.LocalX && existing.LocalY == tile.LocalY)
            {
                SetStatus("同一格只能部署一个单位。");
                return;
            }

        WorldUnitPlacementDefinition placement = new WorldUnitPlacementDefinition
        {
            InstanceId = $"unit-{++nextUnitId}", UnitId = unit.UnitId, LocalX = tile.LocalX, LocalY = tile.LocalY,
            FactionOverride = unit.DefaultFaction, ControllerOverride = unit.Controller, DeckIdOverride = unit.DeckId,
            Enabled = true
        };
        commands.Execute("部署单位",
            () => { tile.Stage.UnitPlacements.Add(placement); RefreshView(); },
            () => { tile.Stage.UnitPlacements.Remove(placement); RefreshView(); });
    }

    private void EraseAt(Tile tile)
    {
        WorldDecorationDefinition objectToRemove = null;
        for (int i = tile.Stage.Decorations.Count - 1; i >= 0; i--)
            if (Covers(tile.Stage.Decorations[i], tile.LocalX, tile.LocalY)) { objectToRemove = tile.Stage.Decorations[i]; break; }
        if (objectToRemove != null)
        {
            int index = tile.Stage.Decorations.IndexOf(objectToRemove);
            commands.Execute("删除对象",
                () => { tile.Stage.Decorations.Remove(objectToRemove); RefreshView(); },
                () => { tile.Stage.Decorations.Insert(index, objectToRemove); RefreshView(); });
            return;
        }
        WorldUnitPlacementDefinition unitToRemove = tile.Stage.UnitPlacements.Find(item => item.Enabled &&
            item.LocalX == tile.LocalX && item.LocalY == tile.LocalY);
        if (unitToRemove == null) return;
        int unitIndex = tile.Stage.UnitPlacements.IndexOf(unitToRemove);
        commands.Execute("撤除单位",
            () => { tile.Stage.UnitPlacements.Remove(unitToRemove); RefreshView(); },
            () => { tile.Stage.UnitPlacements.Insert(unitIndex, unitToRemove); RefreshView(); });
    }

    private void SelectInBox(Vector2Int a, Vector2Int b)
    {
        selection.Clear();
        int minX = Mathf.Min(a.x, b.x); int maxX = Mathf.Max(a.x, b.x);
        int minY = Mathf.Min(a.y, b.y); int maxY = Mathf.Max(a.y, b.y);
        foreach (StageDefinition stage in world.Stages)
        {
            if (stage == null || !stage.Enabled) continue;
            foreach (WorldDecorationDefinition item in stage.Decorations)
                if (ObjectIntersects(stage, item, minX, maxX, minY, maxY)) selection.Add(new Selection { Stage = stage, Object = item });
            foreach (WorldUnitPlacementDefinition item in stage.UnitPlacements)
            {
                Vector2Int cell = WorldCoords.ToWorldTile(new ChunkPosition(stage.GridX, stage.GridY), item.LocalX, item.LocalY,
                    world.TerrainWidth, world.TerrainHeight);
                if (cell.x >= minX && cell.x <= maxX && cell.y >= minY && cell.y <= maxY)
                    selection.Add(new Selection { Stage = stage, Unit = item });
            }
        }
        RefreshOverlays();
        SetStatus(selection.Count == 0 ? "框选中没有对象或单位。" : $"已选择 {selection.Count} 项；Delete 删除，Esc 取消。" );
    }

    private bool ObjectIntersects(StageDefinition stage, WorldDecorationDefinition item, int minX, int maxX, int minY, int maxY)
    {
        for (int y = item.LocalY; y < item.LocalY + item.EffectiveHeight; y++)
        for (int x = item.LocalX; x < item.LocalX + item.EffectiveWidth; x++)
        {
            Vector2Int cell = WorldCoords.ToWorldTile(new ChunkPosition(stage.GridX, stage.GridY), x, y, world.TerrainWidth, world.TerrainHeight);
            if (cell.x >= minX && cell.x <= maxX && cell.y >= minY && cell.y <= maxY) return true;
        }
        return false;
    }

    private void DeleteSelection()
    {
        if (selection.Count == 0) return;
        List<Selection> deleted = new List<Selection>(selection);
        commands.Execute("删除选中项",
            () =>
            {
                foreach (Selection item in deleted)
                {
                    if (item.Object != null) item.Stage.Decorations.Remove(item.Object);
                    if (item.Unit != null) item.Stage.UnitPlacements.Remove(item.Unit);
                }
                selection.Clear(); RefreshView();
            },
            () =>
            {
                foreach (Selection item in deleted)
                {
                    if (item.Object != null && !item.Stage.Decorations.Contains(item.Object)) item.Stage.Decorations.Add(item.Object);
                    if (item.Unit != null && !item.Stage.UnitPlacements.Contains(item.Unit)) item.Stage.UnitPlacements.Add(item.Unit);
                }
                selection.Clear(); RefreshView();
            });
    }

    private void Undo() { if (commands.Undo()) SetStatus("已撤销。"); }
    private void Redo() { if (commands.Redo()) SetStatus("已重做。"); }

    private void Save()
    {
        List<string> issues = WorldMapIO.Validate(world);
        if (issues.Count > 0)
        {
            SetStatus("无法保存：" + issues[0]);
            return;
        }
        WorldMapIO.SaveUserWorld(world);
        WorldCatalog.Reload();
        SetStatus("已保存到可写地图覆盖层：" + Path.Combine(WorldMapIO.UserWorldsRoot, world.WorldId));
    }

    private void Reload()
    {
        WorldCatalog.Reload();
        world = WorldCatalog.Default;
        commands.Clear(); selection.Clear();
        RefreshView(); FrameWorld();
        SetStatus("已重新加载地图 JSON。未保存的编辑已丢弃。");
    }

    private void RefreshView()
    {
        if (terrainRoot == null) return;
        ClearChildren(terrainRoot); ClearChildren(objectRoot); ClearChildren(overlayRoot);
        tiles.Clear();
        for (int s = 0; s < world.Stages.Count; s++)
        {
            StageDefinition stage = world.Stages[s];
            if (stage == null || !stage.Enabled) continue;
            stage.EnsureGrids(world.TerrainWidth, world.TerrainHeight);
            for (int y = 0; y < world.TerrainHeight; y++)
            for (int x = 0; x < world.TerrainWidth; x++) CreateTile(stage, x, y);
            foreach (WorldDecorationDefinition item in stage.Decorations) RenderObject(stage, item);
            foreach (WorldUnitPlacementDefinition item in stage.UnitPlacements) RenderUnit(stage, item);
        }
        RefreshOverlays();
    }

    private void CreateTile(StageDefinition stage, int localX, int localY)
    {
        Vector2Int cell = WorldCoords.ToWorldTile(new ChunkPosition(stage.GridX, stage.GridY), localX, localY,
            world.TerrainWidth, world.TerrainHeight);
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = $"Cell_{cell.x}_{cell.y}";
        cube.transform.SetParent(terrainRoot, false);
        int height = stage.HeightAt(localX, localY, world.TerrainWidth, world.TerrainHeight);
        cube.transform.position = new Vector3(cell.x * CellSize, height * WorldTerrain.StepY, cell.y * CellSize);
        cube.transform.localScale = new Vector3(0.94f, Mathf.Max(0.12f, WorldTerrain.StepY), 0.94f);
        cube.GetComponent<Renderer>().material.color = TerrainColor(stage.TerrainAt(localX, localY, world.TerrainWidth, world.TerrainHeight));
        Tile tile = cube.AddComponent<Tile>(); tile.Stage = stage; tile.LocalX = localX; tile.LocalY = localY;
        tiles[cell] = tile;
    }

    private void RenderObject(StageDefinition stage, WorldDecorationDefinition item)
    {
        if (item == null) return;
        Vector2Int cell = WorldCoords.ToWorldTile(new ChunkPosition(stage.GridX, stage.GridY), item.LocalX, item.LocalY,
            world.TerrainWidth, world.TerrainHeight);
        if (!tiles.TryGetValue(cell, out Tile anchor)) return;
        GameObject obj = WorldDecorationCatalog.Spawn(item, anchor.transform);
        obj.transform.SetParent(objectRoot, true);
        obj.transform.position += new Vector3((item.EffectiveWidth - 1) * 0.5f, 0f, (item.EffectiveHeight - 1) * 0.5f);
        obj.name = "Object_" + item.Id;
    }

    private void RenderUnit(StageDefinition stage, WorldUnitPlacementDefinition item)
    {
        if (item == null || !item.Enabled) return;
        Vector2Int cell = WorldCoords.ToWorldTile(new ChunkPosition(stage.GridX, stage.GridY), item.LocalX, item.LocalY,
            world.TerrainWidth, world.TerrainHeight);
        if (!tiles.TryGetValue(cell, out Tile anchor)) return;
        GameObject token = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        token.name = "Unit_" + item.InstanceId;
        token.transform.SetParent(objectRoot, false);
        token.transform.position = anchor.transform.position + new Vector3(0f, 0.5f, 0f);
        token.transform.localScale = new Vector3(0.42f, 0.55f, 0.42f);
        token.GetComponent<Renderer>().material.color = item.FactionOverride == "enemy" ? new Color(0.82f, 0.25f, 0.28f) : new Color(0.25f, 0.55f, 0.95f);
        Destroy(token.GetComponent<Collider>());
    }

    private void RefreshOverlays()
    {
        if (overlayRoot == null) return;
        ClearChildren(overlayRoot);
        foreach (Selection item in selection)
        {
            if (item.Object != null) DrawObjectSelection(item.Stage, item.Object, new Color(1f, 0.8f, 0.15f, 0.7f));
            if (item.Unit != null) DrawCellSelection(item.Stage, item.Unit.LocalX, item.Unit.LocalY, new Color(0.25f, 0.9f, 1f, 0.7f));
        }
    }

    private void DrawObjectSelection(StageDefinition stage, WorldDecorationDefinition item, Color color)
    {
        for (int y = item.LocalY; y < item.LocalY + item.EffectiveHeight; y++)
        for (int x = item.LocalX; x < item.LocalX + item.EffectiveWidth; x++) DrawCellSelection(stage, x, y, color);
    }

    private void DrawCellSelection(StageDefinition stage, int localX, int localY, Color color)
    {
        Vector2Int cell = WorldCoords.ToWorldTile(new ChunkPosition(stage.GridX, stage.GridY), localX, localY, world.TerrainWidth, world.TerrainHeight);
        if (!tiles.TryGetValue(cell, out Tile tile)) return;
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.transform.SetParent(overlayRoot, false);
        marker.transform.position = tile.transform.position + new Vector3(0f, 0.32f, 0f);
        marker.transform.localScale = new Vector3(1.01f, 0.025f, 1.01f);
        marker.GetComponent<Renderer>().material.color = color;
        Destroy(marker.GetComponent<Collider>());
    }

    private void CreateGhost()
    {
        ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ghost.name = "PlacementGhost";
        Destroy(ghost.GetComponent<Collider>());
        ghost.GetComponent<Renderer>().material.color = new Color(0.2f, 0.95f, 1f, 0.38f);
        ghost.SetActive(false);
    }

    private void UpdateGhost(Tile tile)
    {
        if (ghost == null || tool == Tool.Select || tool == Tool.Erase) { if (ghost != null) ghost.SetActive(false); return; }
        ghost.SetActive(true);
        int width = tool == Tool.Object ? (objectRotation % 180 == 0 ? objectWidth : objectHeight) : 1;
        int height = tool == Tool.Object ? (objectRotation % 180 == 0 ? objectHeight : objectWidth) : 1;
        ghost.transform.position = tile.transform.position + new Vector3((width - 1) * 0.5f, 0.42f, (height - 1) * 0.5f);
        ghost.transform.localScale = new Vector3(width * 0.92f, 0.045f, height * 0.92f);
        ghost.GetComponent<Renderer>().material.color = tool == Tool.Unit ? new Color(0.35f, 0.65f, 1f, 0.42f) : new Color(0.2f, 0.95f, 1f, 0.38f);
    }

    private void DrawBox(Vector2Int a, Vector2Int b)
    {
        if (boxOverlay == null)
        {
            boxOverlay = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(boxOverlay.GetComponent<Collider>());
            boxOverlay.GetComponent<Renderer>().material.color = new Color(1f, 0.85f, 0.15f, 0.35f);
        }
        boxOverlay.SetActive(true);
        Vector2Int min = Vector2Int.Min(a, b); Vector2Int max = Vector2Int.Max(a, b);
        boxOverlay.transform.position = new Vector3((min.x + max.x) * 0.5f, 0.45f, (min.y + max.y) * 0.5f);
        boxOverlay.transform.localScale = new Vector3(max.x - min.x + 0.96f, 0.04f, max.y - min.y + 0.96f);
    }

    private bool TryHitTile(out Tile tile)
    {
        tile = null;
        if (editorCamera == null) return false;
        Ray ray = editorCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, 300f)) return false;
        tile = hit.collider.GetComponent<Tile>();
        return tile != null;
    }

    private Vector2Int ToWorldCell(Tile tile) => WorldCoords.ToWorldTile(new ChunkPosition(tile.Stage.GridX, tile.Stage.GridY),
        tile.LocalX, tile.LocalY, world.TerrainWidth, world.TerrainHeight);
    private static bool Covers(WorldDecorationDefinition item, int x, int y) => item != null && x >= item.LocalX && y >= item.LocalY &&
        x < item.LocalX + item.EffectiveWidth && y < item.LocalY + item.EffectiveHeight;

    private void SetTool(Tool next) { tool = next; SetStatus("工具：" + ToolLabel(next)); }
    private static string ToolLabel(Tool value) => value == Tool.Terrain ? "地形" : value == Tool.Object ? "对象" : value == Tool.Unit ? "单位" : value == Tool.Erase ? "删除" : "框选";

    private void EnsureCameraAndLight()
    {
        editorCamera = Camera.main;
        if (editorCamera == null)
        {
            GameObject cameraObject = new GameObject("WorldEditorCamera"); cameraObject.tag = "MainCamera";
            editorCamera = cameraObject.AddComponent<Camera>(); cameraObject.AddComponent<AudioListener>();
        }
        editorCamera.orthographic = true; editorCamera.orthographicSize = 14f;
        editorCamera.transform.rotation = Quaternion.Euler(62f, 0f, 0f);
        if (FindAnyObjectByType<Light>() == null)
        {
            Light light = new GameObject("WorldEditorLight").AddComponent<Light>();
            light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(48f, -32f, 0f); light.intensity = 1.1f;
        }
    }

    private void FrameWorld()
    {
        float centerX = (world.BoundsMinX * world.TerrainWidth + world.BoundsMaxX * world.TerrainWidth + world.TerrainWidth - 1) * 0.5f;
        float centerZ = (world.BoundsMinY * world.TerrainHeight + world.BoundsMaxY * world.TerrainHeight + world.TerrainHeight - 1) * 0.5f;
        editorCamera.transform.position = new Vector3(centerX, 24f, centerZ - 13f);
        editorCamera.transform.LookAt(new Vector3(centerX, 0f, centerZ));
        editorCamera.orthographicSize = Mathf.Max(8f, Mathf.Max(world.WorldTerrainWidth, world.WorldTerrainHeight) * 0.62f);
    }

    private void EnsureUi()
    {
        GameObject canvasObject = new GameObject("WorldEditorUI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObject.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            GameObject events = new GameObject("EventSystem", typeof(EventSystem)); events.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }
        status = CreateText(canvasObject.transform, "Status", new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(1160f, 34f), 18, TextAnchor.MiddleCenter);
        CreateButton(canvasObject.transform, "保存", new Vector2(-270, -50), Save);
        CreateButton(canvasObject.transform, "重新载入", new Vector2(-150, -50), Reload);
        CreateButton(canvasObject.transform, "撤销", new Vector2(-30, -50), Undo);
        CreateButton(canvasObject.transform, "重做", new Vector2(90, -50), Redo);
        CreateButton(canvasObject.transform, "返回菜单", new Vector2(230, -50), () => SceneManager.LoadScene("S_Menu"));
        CreateButton(canvasObject.transform, "地形 [1]", new Vector2(74, -118), () => SetTool(Tool.Terrain));
        CreateButton(canvasObject.transform, "对象 [2]", new Vector2(74, -164), () => SetTool(Tool.Object));
        CreateButton(canvasObject.transform, "单位 [3]", new Vector2(74, -210), () => SetTool(Tool.Unit));
        CreateButton(canvasObject.transform, "删除 [4]", new Vector2(74, -256), () => SetTool(Tool.Erase));
        CreateButton(canvasObject.transform, "框选 [5]", new Vector2(74, -302), () => SetTool(Tool.Select));
        BuildPalette(canvasObject.transform);
    }

    private void BuildPalette(Transform parent)
    {
        int y = -88;
        CreateText(parent, "TerrainTitle", new Vector2(0f, 1f), new Vector2(82, y), new Vector2(160, 28), 16, TextAnchor.MiddleLeft).text = "地形 Palette";
        for (int i = 0; i < WorldTerrainCatalog.Terrains.Length; i++)
        {
            WorldTerrainCatalog.TerrainBrush brush = WorldTerrainCatalog.Terrains[i]; int row = i;
            CreateButton(parent, brush.Label, new Vector2(56, y - 38 - row * 36), () => { terrainId = brush.Id; SetTool(Tool.Terrain); SetStatus("地形：" + brush.Label); }, 76, 30, new Vector2(0f, 1f));
        }
        int objectY = -88;
        CreateText(parent, "ObjectTitle", new Vector2(1f, 1f), new Vector2(-90, objectY), new Vector2(230, 28), 16, TextAnchor.MiddleRight).text = "对象 Palette（R 旋转）";
        string[] basics = { WorldTerrainCatalog.Tree, WorldTerrainCatalog.Rock, WorldTerrainCatalog.Camp };
        for (int i = 0; i < basics.Length; i++)
        {
            string id = basics[i]; int row = i;
            CreateButton(parent, WorldDecorationCatalog.GlyphOf(id), new Vector2(-176, objectY - 38 - row * 36), () => { objectId = id; SetTool(Tool.Object); }, 76, 30, new Vector2(1f, 1f));
        }
        CreateButton(parent, "宽 -", new Vector2(-215, objectY - 160), () => objectWidth = Mathf.Max(1, objectWidth - 1), 70, 30, new Vector2(1f, 1f));
        CreateButton(parent, "宽 +", new Vector2(-137, objectY - 160), () => objectWidth = Mathf.Min(8, objectWidth + 1), 70, 30, new Vector2(1f, 1f));
        CreateButton(parent, "高 -", new Vector2(-215, objectY - 196), () => objectHeight = Mathf.Max(1, objectHeight - 1), 70, 30, new Vector2(1f, 1f));
        CreateButton(parent, "高 +", new Vector2(-137, objectY - 196), () => objectHeight = Mathf.Min(8, objectHeight + 1), 70, 30, new Vector2(1f, 1f));
        CreateText(parent, "UnitTitle", new Vector2(1f, 0f), new Vector2(-90, 156), new Vector2(230, 28), 16, TextAnchor.MiddleRight).text = "单位 Palette";
        int count = 0;
        foreach (UnitDefinition item in ContentRuntime.Registry.Units)
        {
            if (count >= 5) break;
            UnitDefinition captured = item; int row = count++;
            CreateButton(parent, captured.DisplayName, new Vector2(-176, 118 - row * 36), () => { unitId = captured.UnitId; SetTool(Tool.Unit); SetStatus("单位：" + captured.DisplayName); }, 100, 30, new Vector2(1f, 0f));
        }
    }

    private static Button CreateButton(Transform parent, string label, Vector2 anchored, UnityEngine.Events.UnityAction action, float width = 108, float height = 36, Vector2? anchor = null)
    {
        GameObject obj = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button)); obj.transform.SetParent(parent, false);
        RectTransform rect = obj.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = anchor ?? new Vector2(0.5f, 1f); rect.anchoredPosition = anchored; rect.sizeDelta = new Vector2(width, height);
        obj.GetComponent<Image>().color = new Color(0.08f, 0.11f, 0.16f, 0.9f);
        CreateText(obj.transform, "Label", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, height), 15, TextAnchor.MiddleCenter).text = label;
        Button button = obj.GetComponent<Button>(); button.onClick.AddListener(action); return button;
    }

    private static Text CreateText(Transform parent, string name, Vector2 anchor, Vector2 anchored, Vector2 size, int fontSize, TextAnchor alignment)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text)); obj.transform.SetParent(parent, false);
        RectTransform rect = obj.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = anchor; rect.pivot = anchor; rect.anchoredPosition = anchored; rect.sizeDelta = size;
        Text text = obj.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = fontSize; text.alignment = alignment; text.color = Color.white; text.raycastTarget = false; return text;
    }

    private static Color TerrainColor(string id) => id == WorldTerrainCatalog.Water ? new Color(0.16f, 0.43f, 0.78f) :
        id == WorldTerrainCatalog.Stone ? new Color(0.42f, 0.43f, 0.46f) : id == WorldTerrainCatalog.Dirt ? new Color(0.46f, 0.29f, 0.16f) :
        id == WorldTerrainCatalog.Sand ? new Color(0.82f, 0.7f, 0.38f) : id == WorldTerrainCatalog.Road ? new Color(0.42f, 0.29f, 0.17f) :
        id == WorldTerrainCatalog.Forest ? new Color(0.16f, 0.42f, 0.2f) : id == WorldTerrainCatalog.Void ? new Color(0.05f, 0.05f, 0.07f) : new Color(0.26f, 0.6f, 0.28f);
    private void SetStatus(string value) { if (status != null) status.text = value; }
    private static void ClearChildren(Transform root) { for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject); }
}
