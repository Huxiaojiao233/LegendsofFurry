using System.Collections.Generic;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>世界编辑器输入：连续笔划和框选。摄像机由场景里的 BoardCameraController 负责。</summary>
public sealed class WorldEditorInputController : MonoBehaviour
{
    public enum Tool { Terrain, HeightUp, HeightDown, Object, Unit, Erase, EraseDecoration, EraseUnit, Select }

    private WorldEditorSession session;
    private BoardGenerator board;
    private WorldEditorView view;
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
    private bool stroking;
    private BoardCell lastHover;
    private int nextObjectId;
    private int nextUnitId;
    private readonly List<BoardCell> selectionCells = new List<BoardCell>();
    private readonly HashSet<Vector2Int> paintedCells = new HashSet<Vector2Int>();

    public Tool CurrentTool => tool;
    public Vector2Int HoverCell => hoverCell;
    public bool HasHover => hasHover;
    public ChunkPosition SelectedChunk { get; private set; }
    public event System.Action<ChunkPosition> ChunkSelected;

    public void Bind(WorldEditorSession editorSession, BoardGenerator boardGenerator, WorldEditorView editorView)
    {
        session = editorSession;
        board = boardGenerator;
        view = editorView;
    }

    public void SetTool(Tool next)
    {
        EndStrokeIfNeeded();
        tool = next;
        view?.SetStatus("工具：" + ToolLabel(next));
    }

    public void SetTerrain(string id)
    {
        terrainId = string.IsNullOrWhiteSpace(id) ? WorldTerrainCatalog.Grass : id;
        SetTool(Tool.Terrain);
        view?.SetStatus("地形：" + terrainId);
    }

    public void SetDecoration(string id)
    {
        objectId = string.IsNullOrWhiteSpace(id) ? WorldDecorationCatalog.DefaultId : id;
        SetTool(Tool.Object);
        view?.SetStatus("对象：" + objectId);
    }

    public void SetUnit(string id)
    {
        unitId = id ?? string.Empty;
        SetTool(Tool.Unit);
        view?.SetStatus("单位：" + unitId);
    }

    public void RotateObject()
    {
        objectRotation = (objectRotation + 90) % 360;
        view?.SetStatus($"对象旋转：{objectRotation}°。");
    }

    private void Update()
    {
        if (session?.World == null || Mouse.current == null || Keyboard.current == null) return;
        HandleShortcuts();
        bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        BoardCell cell = null;
        hasHover = !overUi && TryHitCell(out cell);
        if (cell != null)
        {
            hoverCell = cell.Coordinate;
            HighlightHover(cell);
            view?.SetHover($"格 {hoverCell.x},{hoverCell.y}  块 {ChunkOf(hoverCell)}");
        }
        else
        {
            HighlightHover(null);
            view?.SetHover(string.Empty);
        }

        if (overUi)
        {
            EndStrokeIfNeeded();
            return;
        }

        if (tool == Tool.Select)
        {
            HandleBoxSelection();
            return;
        }

        if (hasHover && Mouse.current.leftButton.wasPressedThisFrame)
        {
            session.Commands.BeginStroke(ToolLabel(tool));
            stroking = true;
            paintedCells.Clear();
            ApplyTool(hoverCell);
        }
        else if (stroking && hasHover && Mouse.current.leftButton.isPressed)
        {
            ApplyTool(hoverCell);
        }

        if (stroking && Mouse.current.leftButton.wasReleasedThisFrame)
            EndStrokeIfNeeded();
    }

    private void HandleShortcuts()
    {
        Keyboard keyboard = Keyboard.current;
        bool control = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
        if (control && keyboard.zKey.wasPressedThisFrame) session.Commands.Undo();
        if (control && keyboard.yKey.wasPressedThisFrame) session.Commands.Redo();
        if (keyboard.tKey.wasPressedThisFrame && tool == Tool.Object) RotateObject();
        if (keyboard.digit1Key.wasPressedThisFrame) SetTool(Tool.Terrain);
        if (keyboard.digit2Key.wasPressedThisFrame) SetTool(Tool.Object);
        if (keyboard.digit3Key.wasPressedThisFrame) SetTool(Tool.Unit);
        if (keyboard.digit4Key.wasPressedThisFrame) SetTool(Tool.Erase);
        if (keyboard.digit5Key.wasPressedThisFrame) SetTool(Tool.Select);
        if (keyboard.deleteKey.wasPressedThisFrame) DeleteSelection();
        if (keyboard.escapeKey.wasPressedThisFrame) ClearSelectionHighlight();
        if (hasHover && keyboard.leftBracketKey.wasPressedThisFrame)
            PaintRelativeHeight(hoverCell, -1);
        if (hasHover && keyboard.rightBracketKey.wasPressedThisFrame)
            PaintRelativeHeight(hoverCell, 1);
        if (control && keyboard.sKey.wasPressedThisFrame) session.Save();
    }

    private void HandleBoxSelection()
    {
        if (hasHover && Mouse.current.leftButton.wasPressedThisFrame)
            boxStart = hoverCell;
        if (!Mouse.current.leftButton.wasReleasedThisFrame || !boxStart.HasValue) return;
        SelectInBox(boxStart.Value, hoverCell);
        boxStart = null;
    }

    private void SelectInBox(Vector2Int a, Vector2Int b)
    {
        ClearSelectionHighlight();
        int minX = Mathf.Min(a.x, b.x);
        int maxX = Mathf.Max(a.x, b.x);
        int minY = Mathf.Min(a.y, b.y);
        int maxY = Mathf.Max(a.y, b.y);
        int count = 0;
        for (int z = minY; z <= maxY; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (!board.TryGetCell(x, z, out BoardCell cell) || cell == null) continue;
                cell.SetMoveHighlight(true, new Color(1f, 0.85f, 0.2f, 0.55f));
                selectionCells.Add(cell);
                count++;
            }
        }

        view?.SetSelection(count == 0 ? "框选中没有格子。" : $"已选择 {count} 格。");
        if (count > 0) SelectChunk(ChunkOf(a));
    }

    public void DeleteSelection()
    {
        for (int i = 0; i < selectionCells.Count; i++)
        {
            BoardCell cell = selectionCells[i];
            if (cell == null) continue;
            session.EraseAt(cell.Coordinate.x, cell.Coordinate.y);
        }

        ClearSelectionHighlight();
    }

    private void ApplyTool(Vector2Int cell)
    {
        if (!paintedCells.Add(cell)) return;
        SelectChunk(ChunkOf(cell));
        switch (tool)
        {
            case Tool.Terrain:
                session.PaintTerrain(cell.x, cell.y, terrainId);
                break;
            case Tool.HeightUp:
                PaintRelativeHeight(cell, 1);
                break;
            case Tool.HeightDown:
                PaintRelativeHeight(cell, -1);
                break;
            case Tool.Object:
                if (!session.TryGetStage(cell.x, cell.y, out _, out int lx, out int ly, out _))
                    break;
                var deco = new WorldDecorationDefinition
                {
                    Id = $"object-{++nextObjectId}",
                    Definition = WorldDecorationCatalog.CanonicalId(objectId),
                    LocalX = lx,
                    LocalY = ly,
                    Rotation = objectRotation,
                    FootprintWidth = objectWidth,
                    FootprintHeight = objectHeight
                };
                if (!session.TryPlaceDecoration(cell.x, cell.y, deco, out string decoReason))
                    view?.SetStatus(decoReason);
                break;
            case Tool.Unit:
                if (string.IsNullOrWhiteSpace(unitId) ||
                    !ContentRuntime.Registry.TryGetUnit(unitId, out UnitDefinition unit))
                {
                    view?.SetStatus("请先选择一个已启用单位。");
                    break;
                }

                if (!session.TryGetStage(cell.x, cell.y, out _, out int ulx, out int uly, out _))
                    break;
                var placement = new WorldUnitPlacementDefinition
                {
                    InstanceId = $"unit-{++nextUnitId}",
                    UnitId = unit.UnitId,
                    LocalX = ulx,
                    LocalY = uly,
                    FactionOverride = unit.DefaultFaction,
                    ControllerOverride = unit.Controller,
                    DeckIdOverride = unit.DeckId,
                    Enabled = true
                };
                if (!session.TryPlaceUnit(cell.x, cell.y, placement, out string unitReason))
                    view?.SetStatus(unitReason);
                break;
            case Tool.Erase:
                session.EraseAt(cell.x, cell.y);
                break;
            case Tool.EraseDecoration:
                session.EraseDecorationAt(cell.x, cell.y);
                break;
            case Tool.EraseUnit:
                session.EraseUnitAt(cell.x, cell.y);
                break;
        }
    }

    private void PaintRelativeHeight(Vector2Int cell, int delta)
    {
        if (board == null) return;
        SelectChunk(ChunkOf(cell));
        session.PaintHeight(cell.x, cell.y, board.GetHeight(cell.x, cell.y) + delta);
    }

    private void SelectChunk(ChunkPosition chunk)
    {
        if (chunk.Equals(SelectedChunk)) return;
        SelectedChunk = chunk;
        ChunkSelected?.Invoke(chunk);
    }

    private void EndStrokeIfNeeded()
    {
        if (!stroking) return;
        stroking = false;
        paintedCells.Clear();
        session?.Commands.EndStroke();
    }

    private void HighlightHover(BoardCell cell)
    {
        if (lastHover != null && lastHover != cell)
            lastHover.SetHovered(false);
        lastHover = cell;
        if (cell != null) cell.SetHovered(true);
    }

    private void ClearSelectionHighlight()
    {
        for (int i = 0; i < selectionCells.Count; i++)
        {
            if (selectionCells[i] != null)
                selectionCells[i].SetMoveHighlight(false, Color.clear);
        }

        selectionCells.Clear();
    }

    private bool TryHitCell(out BoardCell cell)
    {
        cell = null;
        Camera camera = Camera.main;
        if (camera == null) return false;
        Ray ray = camera.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, 400f)) return false;
        cell = hit.collider.GetComponentInParent<BoardCell>();
        return cell != null;
    }

    private ChunkPosition ChunkOf(Vector2Int cell)
    {
        int tw = Mathf.Max(1, session.World.TerrainWidth);
        int th = Mathf.Max(1, session.World.TerrainHeight);
        WorldCoords.ToChunk(cell.x, cell.y, tw, th, out ChunkPosition chunk, out _, out _);
        return chunk;
    }

    private static string ToolLabel(Tool value) =>
        value == Tool.Terrain ? "地形" :
        value == Tool.HeightUp ? "抬高" :
        value == Tool.HeightDown ? "挖低" :
        value == Tool.Object ? "对象" :
        value == Tool.Unit ? "单位" :
        value == Tool.Erase ? "删除" :
        value == Tool.EraseDecoration ? "清饰物" :
        value == Tool.EraseUnit ? "清单位" : "框选";
}
