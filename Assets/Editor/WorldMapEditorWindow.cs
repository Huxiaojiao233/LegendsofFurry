#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 大世界地图编辑器：左边选地形和预制件，右边看世界信息、关卡缩略图和大地图。
/// </summary>
public sealed class WorldMapEditorWindow : EditorWindow
{
    private enum BrushKind { Terrain, HeightUp, HeightDown, Decor, EraseDecor, DeployUnit, EraseUnit }

    private WorldDefinition world;
    private StageDefinition selected;
    private Vector2 worldScroll;
    private Vector2 inspectorScroll;
    private BrushKind brush = BrushKind.Terrain;
    private string terrainId = WorldTerrainCatalog.Grass;
    private string decorId = WorldDecorationCatalog.DefaultId;
    private string decorCategory = "Trees_Deciduous";
    private string decorSearch = "";
    private readonly List<UnitDefinition> deployableUnits = new List<UnitDefinition>();
    private string unitId = string.Empty;
    private string stageType = ContentStageTypeKeys.Battle;
    private string status = "打开或新建一张世界地图，点格子就能画。";
    private bool painting;
    private readonly HashSet<(int gx, int gy, int x, int y)> paintedThisStroke =
        new HashSet<(int gx, int gy, int x, int y)>();
    private string newWorldId = "greenland";
    private string newDisplayName = "新世界";
    private int newWidth = 5;
    private int newHeight = 5;
    private float mapTileSize = 14f;
    private Vector2 paletteScroll;
    private Vector2 infoScroll;
    private bool showNewWorld;
    private static Texture2D white;

    [MenuItem("Tools/Legends Of Furry/世界地图编辑器")]
    public static void Open()
    {
        WorldMapEditorWindow window = GetWindow<WorldMapEditorWindow>("世界地图");
        window.minSize = new Vector2(1080, 720);
        window.LoadDefault();
    }

    [MenuItem("Tools/Legends Of Furry/把旧版世界转成分块")]
    public static void ConvertLegacyWorlds()
    {
        string root = WorldMapIO.WorldsRoot;
        if (!Directory.Exists(root))
        {
            EditorUtility.DisplayDialog("世界地图", "没有找到 Worlds 目录。", "好");
            return;
        }

        int count = 0;
        foreach (string path in Directory.GetFiles(root, "*.json"))
        {
            string worldId = Path.GetFileNameWithoutExtension(path);
            if (File.Exists(Path.Combine(root, worldId, "world.json")))
                continue;
            WorldDefinition converted = WorldMapIO.LoadLegacy(path);
            WorldMapIO.SaveChunked(converted);
            count++;
        }

        WorldCatalog.Reload();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("世界地图", $"已转换 {count} 张旧版世界。", "好");
    }

    private void OnEnable()
    {
        WorldDecorationLibraryBuilder.RebuildIfNeeded();
        WorldDecorationCatalog.Refresh();
        AssetPreview.SetPreviewTextureCacheSize(256);
        LoadDefault();
        LoadDeployableUnits();
    }

    private void LoadDefault()
    {
        WorldCatalog.Reload();
        world = WorldCatalog.Default;
        if (world == null) return;
        WorldCatalog.TryGetStage(world, world.StartStageId, out selected);
        status = $"已打开 {world.DisplayName}。左边选资源，右边看整张图。";
    }

    private bool requestRepaint;

    private void OnGUI()
    {
        HandlePaintRelease();
        EnsureTexture();
        DrawToolbar();
        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        DrawPalettePanel();
        DrawWorkspacePanel();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox(status, MessageType.Info);
        if (!requestRepaint) return;
        requestRepaint = false;
        Repaint();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (world != null)
            GUILayout.Label($"{world.DisplayName}  ({world.WorldId})", EditorStyles.boldLabel, GUILayout.Width(220));
        else
            GUILayout.Label("世界地图", EditorStyles.boldLabel, GUILayout.Width(220));
        if (GUILayout.Button("打开默认", EditorStyles.toolbarButton, GUILayout.Width(80))) LoadDefault();
        if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(60))) Save();
        if (GUILayout.Button("校验", EditorStyles.toolbarButton, GUILayout.Width(60))) Validate();
        GUILayout.FlexibleSpace();
        GUILayout.Label(brush == BrushKind.Terrain ? $"地形 · {WorldTerrainCatalog.GlyphOf(terrainId)}"
            : brush == BrushKind.HeightUp ? "抬高"
            : brush == BrushKind.HeightDown ? "挖低"
            : brush == BrushKind.EraseDecor ? "清饰"
            : brush == BrushKind.EraseUnit ? "清单位"
            : brush == BrushKind.DeployUnit ? $"部署 · {unitId}"
            : ShortDecorName(decorId), EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawPalettePanel()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(300), GUILayout.ExpandHeight(true));
        paletteScroll = EditorGUILayout.BeginScrollView(paletteScroll);
        GUILayout.Label("地形", EditorStyles.boldLabel);
        DrawTerrainBrushes();
        EditorGUILayout.Space(6);
        GUILayout.Label("高度", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (BrushButton(brush == BrushKind.HeightUp, "抬高", 64)) brush = BrushKind.HeightUp;
        if (BrushButton(brush == BrushKind.HeightDown, "挖低", 64)) brush = BrushKind.HeightDown;
        GUILayout.Label($"一格 = {WorldTerrain.StepY:0.#} Y", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(8);
        DrawUnitPalette();
        EditorGUILayout.Space(8);
        DrawDecorationPalette();
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawTerrainBrushes()
    {
        WorldTerrainCatalog.TerrainBrush[] terrains = WorldTerrainCatalog.Terrains;
        const int perRow = 4;
        for (int i = 0; i < terrains.Length; i++)
        {
            if (i % perRow == 0) EditorGUILayout.BeginHorizontal();
            WorldTerrainCatalog.TerrainBrush item = terrains[i];
            Color old = GUI.backgroundColor;
            GUI.backgroundColor = brush == BrushKind.Terrain && terrainId == item.Id
                ? Color.Lerp(item.Color, Color.white, 0.4f)
                : item.Color;
            if (GUILayout.Button(item.Label, GUILayout.Height(28), GUILayout.MinWidth(64)))
            {
                brush = BrushKind.Terrain;
                terrainId = item.Id;
                status = $"画笔：{item.Label}";
            }

            GUI.backgroundColor = old;
            if (i % perRow == perRow - 1 || i == terrains.Length - 1)
                EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawWorkspacePanel()
    {
        EditorGUILayout.BeginVertical();
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(252), GUILayout.ExpandHeight(true));
        infoScroll = EditorGUILayout.BeginScrollView(infoScroll);
        DrawWorldAndStageInfo();
        EditorGUILayout.Space(8);
        DrawStageThumbnail();
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
        DrawMapPanel();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private void DrawWorldAndStageInfo()
    {
        GUILayout.Label("世界", EditorStyles.boldLabel);
        if (world == null)
        {
            EditorGUILayout.HelpBox("还没有世界。下面可以新建。", MessageType.Info);
        }
        else
        {
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField("ID", world.WorldId);
            EditorGUI.EndDisabledGroup();
            world.DisplayName = EditorGUILayout.TextField("名称", world.DisplayName);
            EditorGUILayout.LabelField("关卡网格", $"{world.StageGridWidth} × {world.StageGridHeight}");
            EditorGUILayout.LabelField("地形格子", $"{world.WorldTerrainWidth} × {world.WorldTerrainHeight}");
        }

        EditorGUILayout.Space(6);
        GUILayout.Label("当前关卡", EditorStyles.boldLabel);
        if (world == null)
        {
            EditorGUILayout.LabelField("先打开或新建世界。");
        }
        else if (selected == null)
        {
            EditorGUILayout.HelpBox("点缩略图或大地图选关卡。空块会新建。", MessageType.Info);
        }
        else
        {
            selected.DisplayName = EditorGUILayout.TextField("名称", selected.DisplayName);
            selected.StageId = EditorGUILayout.TextField("稳定 ID", selected.StageId);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("类型", GUILayout.Width(36));
            DrawStageTypeButton(ContentStageTypeKeys.Start, "营");
            DrawStageTypeButton(ContentStageTypeKeys.Battle, "战");
            DrawStageTypeButton(ContentStageTypeKeys.Rest, "憩");
            DrawStageTypeButton(ContentStageTypeKeys.Reward, "赏");
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("", GUILayout.Width(36));
            DrawStageTypeButton(ContentStageTypeKeys.Shop, "店");
            DrawStageTypeButton(ContentStageTypeKeys.Boss, "王");
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            selected.RewardPoolId = EditorGUILayout.TextField("奖励卡池", selected.RewardPoolId);
            selected.RequiredKeyId = EditorGUILayout.TextField("需要钥匙", selected.RequiredKeyId);
            selected.DropKeyId = EditorGUILayout.TextField("掉落钥匙", selected.DropKeyId);
            bool isStart = world.StartStageId == selected.StageId;
            bool nextStart = EditorGUILayout.Toggle("出生关卡", isStart);
            if (nextStart && !isStart) world.StartStageId = selected.StageId;
            EditorGUILayout.LabelField("坐标", $"{selected.GridX}, {selected.GridY}");
            EditorGUILayout.LabelField("已部署敌方单位", selected.UnitPlacements.Count.ToString());
        }

        EditorGUILayout.Space(6);
        showNewWorld = EditorGUILayout.Foldout(showNewWorld, "新建世界", true);
        if (showNewWorld)
        {
            newWorldId = EditorGUILayout.TextField("新世界 ID", newWorldId);
            newDisplayName = EditorGUILayout.TextField("显示名", newDisplayName);
            newWidth = EditorGUILayout.IntField("宽", newWidth);
            newHeight = EditorGUILayout.IntField("高", newHeight);
            if (GUILayout.Button("新建并保存")) CreateNew();
        }
    }

    private void DrawStageThumbnail()
    {
        GUILayout.Label("关卡层", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("左键选中，Ctrl+点改类型，右键删除", EditorStyles.miniLabel);
        if (world == null) return;
        worldScroll = EditorGUILayout.BeginScrollView(worldScroll, GUILayout.Height(200f));
        float cell = 40f;
        Rect grid = GUILayoutUtility.GetRect(world.StageGridWidth * cell + 8, world.StageGridHeight * cell + 8);
        for (int gy = world.BoundsMaxY; gy >= world.BoundsMinY; gy--)
        {
            for (int gx = world.BoundsMinX; gx <= world.BoundsMaxX; gx++)
            {
                int col = gx - world.BoundsMinX;
                int row = world.BoundsMaxY - gy;
                Rect rect = new Rect(grid.x + 4 + col * cell, grid.y + 4 + row * cell, cell - 4, cell - 4);
                WorldCatalog.TryGetStageAt(world, gx, gy, out StageDefinition stage);
                Color fill = stage == null
                    ? new Color(0.16f, 0.17f, 0.19f)
                    : StageColor(stage);
                bool on = selected != null && stage != null && selected.StageId == stage.StageId;
                if (on) fill = Color.Lerp(fill, Color.white, 0.35f);
                if (Event.current.type == EventType.Repaint)
                {
                    DrawRect(rect, fill);
                    GUI.Label(rect, stage == null ? "+" : ShortName(stage), CenteredLabel());
                    if (on) DrawOutline(rect, new Color(1f, 0.9f, 0.2f, 1f), 2f);
                }

                HandleWorldCell(rect, gx, gy, stage);
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawMapPanel()
    {
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        if (world == null)
        {
            GUILayout.Label("大地图", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("先打开或新建一张世界。", MessageType.Info);
            EditorGUILayout.EndVertical();
            return;
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(
            $"大地图 · {world.WorldTerrainWidth}×{world.WorldTerrainHeight} 格",
            EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        GUILayout.Label("格子", GUILayout.Width(28));
        mapTileSize = EditorGUILayout.Slider(mapTileSize, 8f, 22f, GUILayout.Width(140));
        EditorGUILayout.EndHorizontal();
        inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
        DrawWorldTerrainMap();
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawWorldTerrainMap()
    {
        int tw = Mathf.Max(1, world.TerrainWidth);
        int th = Mathf.Max(1, world.TerrainHeight);
        float size = Mathf.Round(mapTileSize);
        float mapW = world.StageGridWidth * tw * size + 8f;
        float mapH = world.StageGridHeight * th * size + 8f;
        Rect grid = GUILayoutUtility.GetRect(mapW, mapH);
        Rect selectedRect = Rect.zero;
        bool hasSelected = false;
        EventType eventType = Event.current.type;

        for (int gy = world.BoundsMaxY; gy >= world.BoundsMinY; gy--)
        {
            for (int gx = world.BoundsMinX; gx <= world.BoundsMaxX; gx++)
            {
                WorldCatalog.TryGetStageAt(world, gx, gy, out StageDefinition stage);
                if (stage != null) stage.EnsureGrids(tw, th);
                Rect stageRect = StageRect(grid, gx, gy, tw, th, size);
                if (selected != null && stage != null && selected.StageId == stage.StageId)
                {
                    selectedRect = stageRect;
                    hasSelected = true;
                }

                for (int y = th - 1; y >= 0; y--)
                {
                    for (int x = 0; x < tw; x++)
                    {
                        int col = (gx - world.BoundsMinX) * tw + x;
                        int row = (world.BoundsMaxY - gy) * th + (th - 1 - y);
                        Rect rect = new Rect(
                            grid.x + 4f + col * size,
                            grid.y + 4f + row * size,
                            size,
                            size);
                        if (eventType == EventType.Repaint)
                            DrawTerrainTile(rect, stage, x, y, tw, th, size);
                        HandleWorldTerrainCell(rect, stage, gx, gy, x, y);
                    }
                }

                if (eventType == EventType.Repaint && stage == null)
                    GUI.Label(stageRect, "+", CenteredLabel());
            }
        }

        if (eventType != EventType.Repaint) return;
        for (int gy = world.BoundsMaxY; gy >= world.BoundsMinY; gy--)
        {
            for (int gx = world.BoundsMinX; gx <= world.BoundsMaxX; gx++)
            {
                WorldCatalog.TryGetStageAt(world, gx, gy, out StageDefinition stage);
                Rect stageRect = StageRect(grid, gx, gy, tw, th, size);
                DrawOutline(stageRect, stage == null
                    ? new Color(1f, 1f, 1f, 0.12f)
                    : new Color(0f, 0f, 0f, 0.55f), 1f);
                if (stage != null)
                    GUI.Label(new Rect(stageRect.x + 2f, stageRect.y + 1f, 28f, 16f), ShortName(stage), CenteredMini());
            }
        }

        if (hasSelected)
            DrawOutline(selectedRect, new Color(1f, 0.9f, 0.2f, 1f), 3f);
    }

    private void DrawTerrainTile(Rect rect, StageDefinition stage, int x, int y, int tw, int th, float size)
    {
        Color fill;
        string text = "";
        if (stage == null)
        {
            fill = new Color(0.12f, 0.13f, 0.15f);
        }
        else
        {
            string id = stage.TerrainAt(x, y, tw, th);
            int height = stage.HeightAt(x, y, tw, th);
            fill = WorldTerrainCatalog.ColorOf(id);
            fill = Color.Lerp(fill, Color.white, (height + 1) * 0.08f);
            WorldUnitPlacementDefinition unit = FindUnitPlacement(stage, x, y);
            WorldDecorationDefinition deco = FindDecoration(stage, x, y);
            if (unit != null) text = UnitGlyph(unit.UnitId);
            else if (deco != null) text = WorldDecorationCatalog.GlyphOf(deco.Definition);
            else if (size >= 16f) text = HeightMark(height);
        }

        DrawFill(rect, fill);
        if (!string.IsNullOrEmpty(text))
            GUI.Label(rect, text, CenteredMini());
    }

    private Rect StageRect(Rect grid, int gx, int gy, int tw, int th, float size)
    {
        int col = (gx - world.BoundsMinX) * tw;
        int row = (world.BoundsMaxY - gy) * th;
        return new Rect(grid.x + 4f + col * size, grid.y + 4f + row * size, tw * size, th * size);
    }

    private void HandleWorldCell(Rect rect, int gx, int gy, StageDefinition stage)
    {
        Event current = Event.current;
        if (current.type != EventType.MouseDown || !rect.Contains(current.mousePosition))
            return;
        if (current.button == 1)
        {
            if (stage != null && stage.StageId != world.StartStageId)
            {
                world.Stages.Remove(stage);
                if (selected == stage) selected = null;
                status = $"已拿掉 ({gx},{gy})。保存时这里会写成空块。";
            }

            current.Use();
            requestRepaint = true;
            return;
        }

        if (current.button != 0) return;
        if (stage == null)
        {
            stage = WorldMapIO.CreateEmptyChunk(world, gx, gy, true);
            stage.StageType = stageType;
            stage.DisplayName = TypeLabel(stageType) + $" {gx},{gy}";
            world.Stages.Add(stage);
            status = $"已在 ({gx},{gy}) 放上新关卡。";
        }
        else if (current.control || current.command)
        {
            stage.StageType = stageType;
            status = $"把 {stage.DisplayName} 改成 {TypeLabel(stageType)}。";
        }

        selected = stage;
        current.Use();
        requestRepaint = true;
    }

    private void HandleWorldTerrainCell(Rect rect, StageDefinition stage, int gx, int gy, int x, int y)
    {
        Event current = Event.current;
        bool press = current.type == EventType.MouseDown && current.button == 0;
        bool drag = current.type == EventType.MouseDrag && current.button == 0;
        if ((!press && !drag) || !rect.Contains(current.mousePosition) || world == null)
            return;
        if (press)
        {
            painting = true;
            paintedThisStroke.Clear();
            if (stage == null) stage = CreateStageAt(gx, gy);
            selected = stage;
        }

        if (!painting) return;
        if (stage == null) stage = CreateStageAt(gx, gy);
        if (stage == null) return;
        if (!paintedThisStroke.Add((gx, gy, x, y)))
        {
            current.Use();
            return;
        }

        PaintTile(stage, x, y);
        current.Use();
        requestRepaint = true;
    }

    private StageDefinition CreateStageAt(int gx, int gy)
    {
        if (WorldCatalog.TryGetStageAt(world, gx, gy, out StageDefinition existing))
            return existing;
        StageDefinition stage = WorldMapIO.CreateEmptyChunk(world, gx, gy, true);
        stage.StageType = stageType;
        stage.DisplayName = TypeLabel(stageType) + $" {gx},{gy}";
        world.Stages.Add(stage);
        status = $"已在 ({gx},{gy}) 放上新关卡。";
        return stage;
    }

    private void HandlePaintRelease()
    {
        EventType raw = Event.current.rawType;
        if (raw != EventType.MouseUp && raw != EventType.MouseLeaveWindow) return;
        painting = false;
        paintedThisStroke.Clear();
    }

    private void PaintTile(StageDefinition stage, int x, int y)
    {
        if (stage == null || world == null) return;
        int tw = world.TerrainWidth;
        int th = world.TerrainHeight;
        stage.EnsureGrids(tw, th);
        switch (brush)
        {
            case BrushKind.Terrain:
                stage.SetTerrain(x, y, tw, th, terrainId);
                status = $"刷了 {terrainId}。";
                break;
            case BrushKind.HeightUp:
                stage.SetHeight(x, y, tw, th, stage.HeightAt(x, y, tw, th) + 1);
                status = $"抬高一格（世界 Y +{WorldTerrain.StepY:0.#}）。";
                break;
            case BrushKind.HeightDown:
                stage.SetHeight(x, y, tw, th, stage.HeightAt(x, y, tw, th) - 1);
                status = $"挖低一格（世界 Y -{WorldTerrain.StepY:0.#}）。";
                break;
            case BrushKind.Decor:
                UpsertDecoration(stage, x, y, decorId);
                status = $"放了 {WorldDecorationCatalog.GlyphOf(decorId)} {ShortDecorName(decorId)}。";
                break;
            case BrushKind.EraseDecor:
                RemoveDecoration(stage, x, y);
                status = "清掉装饰。";
                break;
            case BrushKind.DeployUnit:
                UpsertUnitPlacement(stage, x, y, unitId);
                status = $"部署了 {unitId}。";
                break;
            case BrushKind.EraseUnit:
                RemoveUnitPlacement(stage, x, y);
                status = "清掉部署单位。";
                break;
        }
    }

    private void Save()
    {
        if (world == null) return;
        List<string> issues = WorldMapIO.Validate(world);
        if (issues.Count > 0 && !EditorUtility.DisplayDialog(
                "保存地图",
                "校验未通过，仍要保存吗？\n\n" + FormatIssues(issues),
                "仍然保存",
                "取消"))
        {
            status = "已取消保存：" + issues[0];
            return;
        }

        string selectedId = selected != null ? selected.StageId : world.StartStageId;
        WorldMapIO.SaveChunked(world);
        WorldCatalog.Reload();
        WorldCatalog.TryGet(world.WorldId, out world);
        if (world != null) WorldCatalog.TryGetStage(world, selectedId, out selected);
        if (world != null)
            status = $"已保存到 {WorldMapIO.WorldFolder(world.WorldId)}。Play 会读这份图。";
        EditorApplication.delayCall += () => AssetDatabase.Refresh();
        requestRepaint = true;
    }

    private void Validate()
    {
        if (world == null) return;
        List<string> issues = WorldMapIO.Validate(world);
        status = issues.Count == 0 ? "校验通过。" : issues[0] + (issues.Count > 1 ? $" 等 {issues.Count} 条" : "");
    }

    private static string FormatIssues(List<string> issues)
    {
        const int maxLines = 8;
        int count = issues.Count < maxLines ? issues.Count : maxLines;
        string text = string.Join("\n", issues.GetRange(0, count));
        if (issues.Count > maxLines) text += $"\n…共 {issues.Count} 条";
        return text;
    }

    private void CreateNew()
    {
        if (!ContentId.IsValid(newWorldId))
        {
            status = "新世界 ID 必须小写字母开头，只能含字母数字 . _ -。";
            return;
        }

        world = WorldMapIO.CreateBlank(newWorldId, newDisplayName, newWidth, newHeight, 10);
        selected = world.Stages[0];
        Save();
        status = $"已创建 {world.DisplayName}。左边选资源，右边看整张图。";
    }

    private void DrawStageTypeButton(string type, string label)
    {
        Color old = GUI.backgroundColor;
        GUI.backgroundColor = StageColor(type);
        if (BrushButton(stageType == type, label, 36))
        {
            stageType = type;
            if (selected != null)
            {
                selected.StageType = type;
                status = $"当前关卡改成 {TypeLabel(type)}。缩略图 Ctrl+点可把类型刷到别的关卡。";
            }
        }

        GUI.backgroundColor = old;
    }

    private static bool BrushButton(bool active, string label, float width)
    {
        Color old = GUI.backgroundColor;
        if (active) GUI.backgroundColor = Color.Lerp(GUI.backgroundColor, Color.white, 0.45f);
        bool clicked = GUILayout.Button(label, GUILayout.Width(width), GUILayout.Height(24));
        GUI.backgroundColor = old;
        return clicked;
    }

    private static bool ToolToggle(bool on, string label, float width) => BrushButton(on, label, width);

    private static string TypeLabel(string type) => type switch
    {
        ContentStageTypeKeys.Start => "营地",
        ContentStageTypeKeys.Battle => "战斗",
        ContentStageTypeKeys.Rest => "休息",
        ContentStageTypeKeys.Reward => "奖励",
        ContentStageTypeKeys.Shop => "商店",
        ContentStageTypeKeys.Boss => "魔王",
        _ => type
    };

    private static WorldDecorationDefinition FindDecoration(StageDefinition stage, int x, int y)
    {
        if (stage.Decorations == null) return null;
        for (int i = 0; i < stage.Decorations.Count; i++)
        {
            WorldDecorationDefinition deco = stage.Decorations[i];
            if (deco != null && deco.LocalX == x && deco.LocalY == y) return deco;
        }

        return null;
    }

    private static void UpsertDecoration(StageDefinition stage, int x, int y, string definition)
    {
        WorldDecorationDefinition existing = FindDecoration(stage, x, y);
        if (existing != null)
        {
            existing.Definition = definition;
            return;
        }

        stage.Decorations.Add(new WorldDecorationDefinition
        {
            Id = $"deco-{stage.StageId}-{x}-{y}",
            Definition = definition,
            LocalX = x,
            LocalY = y
        });
    }

    private static void RemoveDecoration(StageDefinition stage, int x, int y)
    {
        if (stage.Decorations == null) return;
        stage.Decorations.RemoveAll(item => item != null && item.LocalX == x && item.LocalY == y);
    }

    private void LoadDeployableUnits()
    {
        deployableUnits.Clear();
        try
        {
            ContentRegistry registry = new ContentRegistry(ContentRuntime.LoadComposedSnapshot().Package);
            deployableUnits.AddRange(registry.Units
                .Where(item => item.IsBoss || item.DefaultFaction == "enemy" || item.Controller == "ai")
                .OrderBy(item => item.SortOrder).ThenBy(item => item.UnitId, System.StringComparer.Ordinal));
            if (deployableUnits.Count > 0 && string.IsNullOrWhiteSpace(unitId))
                unitId = deployableUnits[0].UnitId;
        }
        catch (System.Exception exception)
        {
            status = "单位列表读取失败：" + exception.Message;
        }
    }

    private void DrawUnitPalette()
    {
        GUILayout.Label("敌对单位部署", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (BrushButton(brush == BrushKind.EraseUnit, "清单位", 64)) brush = BrushKind.EraseUnit;
        if (GUILayout.Button("刷新", GUILayout.Width(48))) LoadDeployableUnits();
        EditorGUILayout.EndHorizontal();
        if (deployableUnits.Count == 0)
        {
            EditorGUILayout.HelpBox("内容包里没有可部署的敌方/AI/Boss 单位。", MessageType.Info);
            return;
        }
        foreach (UnitDefinition unit in deployableUnits)
        {
            Color old = GUI.backgroundColor;
            if (brush == BrushKind.DeployUnit && unitId == unit.UnitId)
                GUI.backgroundColor = Color.Lerp(Color.red, Color.white, 0.45f);
            if (GUILayout.Button($"{UnitGlyph(unit)}  {unit.DisplayName}  [{unit.UnitId}]"))
            {
                brush = BrushKind.DeployUnit;
                unitId = unit.UnitId;
                status = $"单位画笔：{unit.DisplayName}";
            }
            GUI.backgroundColor = old;
        }
    }

    private static WorldUnitPlacementDefinition FindUnitPlacement(StageDefinition stage, int x, int y)
    {
        return stage?.UnitPlacements?.Find(item => item != null && item.LocalX == x && item.LocalY == y);
    }

    private static void UpsertUnitPlacement(StageDefinition stage, int x, int y, string definition)
    {
        if (stage == null || string.IsNullOrWhiteSpace(definition)) return;
        WorldUnitPlacementDefinition existing = FindUnitPlacement(stage, x, y);
        if (existing != null)
        {
            existing.UnitId = definition;
            existing.Enabled = true;
            return;
        }
        stage.UnitPlacements.Add(new WorldUnitPlacementDefinition
        {
            InstanceId = $"{stage.StageId}-{definition}-{x}-{y}",
            UnitId = definition,
            LocalX = x,
            LocalY = y,
            FactionOverride = "enemy",
            ControllerOverride = "ai",
            Enabled = true
        });
    }

    private static void RemoveUnitPlacement(StageDefinition stage, int x, int y)
    {
        stage?.UnitPlacements?.RemoveAll(item => item != null && item.LocalX == x && item.LocalY == y);
    }

    private string UnitGlyph(string unitDefinitionId)
    {
        UnitDefinition definition = deployableUnits.Find(item => item.UnitId == unitDefinitionId);
        return UnitGlyph(definition);
    }

    private static string UnitGlyph(UnitDefinition definition) => definition?.IsBoss == true ? "王" : "敌";

    private void DrawDecorationPalette()
    {
        GUILayout.Label("预制件", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (BrushButton(brush == BrushKind.EraseDecor, "清饰", 52)) brush = BrushKind.EraseDecor;
        GUILayout.Label(ShortDecorName(decorId), EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
        decorSearch = EditorGUILayout.TextField("搜索", decorSearch);
        int categoryIndex = Mathf.Max(0, System.Array.IndexOf(WorldDecorationCatalog.CategoryOrder, decorCategory));
        string[] categoryLabels = new string[WorldDecorationCatalog.CategoryOrder.Length];
        for (int i = 0; i < categoryLabels.Length; i++)
            categoryLabels[i] = WorldDecorationCatalog.CategoryLabel(WorldDecorationCatalog.CategoryOrder[i]);
        int nextCategory = EditorGUILayout.Popup("分类", categoryIndex, categoryLabels);
        if (nextCategory != categoryIndex)
        {
            decorCategory = WorldDecorationCatalog.CategoryOrder[nextCategory];
            decorSearch = "";
        }

        WorldDecorationEntry[] list = string.IsNullOrWhiteSpace(decorSearch)
            ? WorldDecorationCatalog.InCategory(decorCategory)
            : WorldDecorationCatalog.Search(decorSearch);
        const float cell = 68f;
        const int columns = 4;
        int rows = Mathf.Max(1, Mathf.CeilToInt(list.Length / (float)columns));
        Rect grid = GUILayoutUtility.GetRect(columns * cell, rows * cell);
        for (int i = 0; i < list.Length; i++)
        {
            int col = i % columns;
            int row = i / columns;
            Rect rect = new Rect(grid.x + col * cell, grid.y + row * cell, cell - 4f, cell - 4f);
            DrawDecorPreview(rect, list[i]);
        }
    }

    private void DrawDecorPreview(Rect rect, WorldDecorationEntry entry)
    {
        bool on = brush == BrushKind.Decor && decorId == entry.Id;
        GameObject prefab = WorldDecorationCatalog.LoadPrefab(entry.Id);
        Texture preview = prefab != null ? AssetPreview.GetAssetPreview(prefab) : null;
        if (preview == null && prefab != null) preview = AssetPreview.GetMiniThumbnail(prefab);
        if (prefab != null && AssetPreview.IsLoadingAssetPreview(prefab.GetEntityId()))
            requestRepaint = true;

        Color old = GUI.backgroundColor;
        if (on) GUI.backgroundColor = new Color(0.45f, 0.72f, 1f);
        GUIContent content = preview != null
            ? new GUIContent(preview, entry.Label)
            : new GUIContent(entry.Glyph, entry.Label);
        if (GUI.Button(rect, content))
        {
            brush = BrushKind.Decor;
            decorId = entry.Id;
            status = $"画笔：{entry.Label}";
        }

        GUI.backgroundColor = old;
        GUI.Label(new Rect(rect.x + 2f, rect.yMax - 14f, rect.width - 4f, 14f), entry.Glyph, CenteredMini());
    }

    private static string ShortDecorName(string definition)
    {
        if (WorldDecorationCatalog.TryGet(definition, out WorldDecorationEntry entry))
            return entry.Label;
        return definition;
    }

    private static string HeightMark(int height) => height == 0 ? "" : height.ToString();

    private static string ShortName(StageDefinition stage)
    {
        if (stage == null) return "";
        if (!string.IsNullOrEmpty(stage.DisplayName) && stage.DisplayName.Length <= 4) return stage.DisplayName;
        return TypeGlyph(stage.StageType);
    }

    private static string TypeGlyph(string type) => type switch
    {
        ContentStageTypeKeys.Start => "营",
        ContentStageTypeKeys.Battle => "战",
        ContentStageTypeKeys.Rest => "憩",
        ContentStageTypeKeys.Reward => "赏",
        ContentStageTypeKeys.Shop => "店",
        ContentStageTypeKeys.Boss => "王",
        _ => "·"
    };

    private static Color StageColor(StageDefinition stage) => StageColor(stage.StageType);

    private static Color StageColor(string type) => type switch
    {
        ContentStageTypeKeys.Start => new Color(0.45f, 0.78f, 0.4f),
        ContentStageTypeKeys.Battle => new Color(0.78f, 0.38f, 0.32f),
        ContentStageTypeKeys.Reward => new Color(0.86f, 0.74f, 0.28f),
        ContentStageTypeKeys.Shop => new Color(0.36f, 0.56f, 0.82f),
        ContentStageTypeKeys.Rest => new Color(0.38f, 0.72f, 0.72f),
        ContentStageTypeKeys.Boss => new Color(0.62f, 0.28f, 0.7f),
        _ => new Color(0.4f, 0.4f, 0.4f)
    };

    private static void DrawFill(Rect rect, Color color)
    {
        Color old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, white);
        GUI.color = old;
    }

    private static void DrawOutline(Rect rect, Color color, float thickness)
    {
        DrawFill(new Rect(rect.x, rect.y, rect.width, thickness), color);
        DrawFill(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        DrawFill(new Rect(rect.x, rect.y, thickness, rect.height), color);
        DrawFill(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }

    private static void DrawRect(Rect rect, Color color)
    {
        DrawFill(rect, color);
        DrawOutline(rect, new Color(0f, 0f, 0f, 0.35f), 1f);
    }

    private static GUIStyle CenteredLabel()
    {
        GUIStyle style = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            wordWrap = true
        };
        style.normal.textColor = Color.white;
        return style;
    }

    private static GUIStyle CenteredMini()
    {
        GUIStyle style = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
        style.normal.textColor = Color.white;
        return style;
    }

    private static void EnsureTexture()
    {
        if (white != null) return;
        white = new Texture2D(1, 1);
        white.SetPixel(0, 0, Color.white);
        white.Apply();
    }
}
#endif
