#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>幂等构建 S_WorldEditor；布局、交互控件、3D 机位、棋盘和灯光都由此自动化脚本统一生成。</summary>
public static class WorldEditorSceneSetup
{
    public const string ScenePath = "Assets/Scenes/S_WorldEditor.unity";
    private const string RootName = "WorldEditorRoot";
    private const string CameraName = "Main Camera";
    private const string LightName = "Directional Light";
    private const string VolumeName = "Global Volume";
    private const string CanvasName = "WorldEditorCanvas";
    private const string BoardName = "Board";
    private const string GridName = "GridRoot";
    private static readonly string[] LegacyWorldObjectNames =
    {
        "WorldEditorCamera", "WorldEditorBoard", "WorldEditorLight", "Grid"
    };

    [MenuItem("Tools/Legends Of Furry/Build World Editor Scene")]
    public static void Build()
    {
        BoardGenerator.TerrainPrefabBinding[] bindings =
            TerrainPrefabSetupCreate(out GameObject fallback);
        Scene scene = OpenOrCreateScene();
        DestroyLegacyWorldObjects(scene);
        CopyBattleWorldRig(scene);
        Transform root = FindOrCreate3D(RootName).transform;
        EnsureEventSystem();
        Transform grid = EnsureGridRoot(FindRoot(scene, BoardName));
        BoardGenerator board = EnsureBoard(grid, bindings, fallback);
        WireBattleCamera(scene, board);
        WorldEditorView view = EnsureCanvas(out Button backButton);
        WorldEditorInputController input = FindOrCreateComponent<WorldEditorInputController>(root.gameObject);
        RuntimeWorldEditorController controller =
            FindOrCreateComponent<RuntimeWorldEditorController>(root.gameObject);
        BindController(controller, board, view, input, backButton);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"已按 S_Battle 复制摄像机与棋盘到 {ScenePath}");
    }

    public static void BuildBatch()
    {
        Build();
        EditorApplication.Exit(0);
    }

    private const string BuildFlagPath = "Temp/BuildWorldEditorScene.flag";
    private const string BuildResultPath = "Temp/BuildWorldEditorScene.result";

    [InitializeOnLoadMethod]
    private static void BuildWhenFlagged()
    {
        if (!File.Exists(BuildFlagPath)) return;
        EditorApplication.update += PollBuildFlag;
    }

    private static void PollBuildFlag()
    {
        if (!File.Exists(BuildFlagPath))
        {
            EditorApplication.update -= PollBuildFlag;
            return;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        EditorApplication.update -= PollBuildFlag;
        try
        {
            Build();
            File.Delete(BuildFlagPath);
            File.WriteAllText(BuildResultPath, "ok");
        }
        catch (System.Exception ex)
        {
            File.WriteAllText(BuildResultPath, "fail\n" + ex);
        }
    }

    private static BoardGenerator.TerrainPrefabBinding[] TerrainPrefabSetupCreate(out GameObject fallback)
    {
        fallback = AssetDatabase.LoadAssetAtPath<GameObject>(TerrainPrefabSetup.SourceBlockPrefab);
        var list = new List<BoardGenerator.TerrainPrefabBinding>();
        for (int i = 0; i < TerrainPrefabSetup.Entries.Length; i++)
        {
            string path = $"{TerrainPrefabSetup.TerrainPrefabFolder}/{TerrainPrefabSetup.Entries[i].fileName}";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            list.Add(new BoardGenerator.TerrainPrefabBinding
            {
                terrainId = TerrainPrefabSetup.Entries[i].terrainId,
                prefab = prefab
            });
            if (fallback == null && prefab != null) fallback = prefab;
        }

        if (list.Count > 0 && list[0].prefab != null) fallback = list[0].prefab;
        return list.ToArray();
    }

    private static Scene OpenOrCreateScene()
    {
        if (File.Exists(ScenePath))
            return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        return scene;
    }

    private static void DestroyLegacyWorldObjects(Scene scene)
    {
        for (int i = 0; i < LegacyWorldObjectNames.Length; i++)
        {
            GameObject legacy = FindRoot(scene, LegacyWorldObjectNames[i]);
            if (legacy != null) Object.DestroyImmediate(legacy);
        }
    }

    private static void CopyBattleWorldRig(Scene editorScene)
    {
        if (!File.Exists(TerrainPrefabSetup.BattleScenePath))
        {
            Debug.LogError($"找不到 S_Battle：{TerrainPrefabSetup.BattleScenePath}");
            return;
        }

        Scene battle = EditorSceneManager.OpenScene(TerrainPrefabSetup.BattleScenePath, OpenSceneMode.Additive);
        try
        {
            CopyRenderSettings(battle, editorScene);
            CopyRoot(battle, editorScene, CameraName);
            CopyRoot(battle, editorScene, BoardName);
            CopyRoot(battle, editorScene, LightName);
            CopyRoot(battle, editorScene, VolumeName);
        }
        finally
        {
            EditorSceneManager.CloseScene(battle, true);
        }
    }

    private static void CopyRenderSettings(Scene from, Scene to)
    {
        SceneManager.SetActiveScene(from);
        Material skybox = RenderSettings.skybox;
        bool fog = RenderSettings.fog;
        Color fogColor = RenderSettings.fogColor;
        FogMode fogMode = RenderSettings.fogMode;
        float fogDensity = RenderSettings.fogDensity;
        Color ambientSky = RenderSettings.ambientSkyColor;
        Color ambientEquator = RenderSettings.ambientEquatorColor;
        Color ambientGround = RenderSettings.ambientGroundColor;
        AmbientMode ambientMode = RenderSettings.ambientMode;
        Color subtractive = RenderSettings.subtractiveShadowColor;
        float reflection = RenderSettings.reflectionIntensity;
        SceneManager.SetActiveScene(to);
        RenderSettings.skybox = skybox;
        RenderSettings.fog = fog;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogMode = fogMode;
        RenderSettings.fogDensity = fogDensity;
        RenderSettings.ambientSkyColor = ambientSky;
        RenderSettings.ambientEquatorColor = ambientEquator;
        RenderSettings.ambientGroundColor = ambientGround;
        RenderSettings.ambientMode = ambientMode;
        RenderSettings.subtractiveShadowColor = subtractive;
        RenderSettings.reflectionIntensity = reflection;
    }

    private static void CopyRoot(Scene from, Scene to, string name)
    {
        GameObject source = FindRoot(from, name);
        if (source == null)
        {
            Debug.LogError($"S_Battle 缺少 {name}。");
            return;
        }

        GameObject existing = FindRoot(to, name);
        if (existing != null) Object.DestroyImmediate(existing);
        GameObject clone = Object.Instantiate(source);
        clone.name = name;
        EditorSceneManager.MoveGameObjectToScene(clone, to);
    }

    private static void WireBattleCamera(Scene scene, BoardGenerator board)
    {
        GameObject cameraObject = FindRoot(scene, CameraName);
        if (cameraObject == null || board == null) return;
        Camera camera = cameraObject.GetComponent<Camera>();
        BoardCameraController rig = FindOrCreateComponent<BoardCameraController>(cameraObject);
        SerializedObject so = new SerializedObject(rig);
        so.FindProperty("targetCamera").objectReferenceValue = camera;
        so.FindProperty("board").objectReferenceValue = board;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(rig);
    }

    private static Transform EnsureGridRoot(GameObject boardObject)
    {
        if (boardObject == null) return FindOrCreate3D(GridName).transform;
        Transform grid = boardObject.transform.Find(GridName);
        if (grid != null) return grid;
        GameObject created = new GameObject(GridName);
        created.transform.SetParent(boardObject.transform, false);
        created.transform.localPosition = new Vector3(0f, 0.1f, 0f);
        return created.transform;
    }

    private static GameObject FindRoot(Scene scene, string name)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] != null && roots[i].name == name) return roots[i];
        }

        return null;
    }

    private static void EnsureEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() != null) return;
        GameObject events = FindOrCreate("EventSystem");
        FindOrCreateComponent<EventSystem>(events);
        FindOrCreateComponent<InputSystemUIInputModule>(events);
    }

    private static BoardGenerator EnsureBoard(Transform grid,
        BoardGenerator.TerrainPrefabBinding[] bindings, GameObject fallback)
    {
        GameObject boardObject = FindRoot(SceneManager.GetActiveScene(), BoardName) ?? FindOrCreate3D(BoardName);
        BoardGenerator board = FindOrCreateComponent<BoardGenerator>(boardObject);
        FindOrCreateComponent<WorldTerrainInstanceRenderer>(boardObject);
        SerializedObject so = new SerializedObject(board);
        so.FindProperty("gridRoot").objectReferenceValue = grid;
        so.FindProperty("fallbackCellPrefab").objectReferenceValue = fallback;
        SerializedProperty array = so.FindProperty("terrainPrefabs");
        array.arraySize = bindings.Length;
        for (int i = 0; i < bindings.Length; i++)
        {
            SerializedProperty element = array.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("terrainId").stringValue = bindings[i].terrainId;
            element.FindPropertyRelative("prefab").objectReferenceValue = bindings[i].prefab;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(board);
        return board;
    }

    private static WorldEditorView EnsureCanvas(out Button backButton)
    {
        GameObject canvasObject = FindOrCreate(CanvasName);
        Canvas canvas = FindOrCreateComponent<Canvas>(canvasObject);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = FindOrCreateComponent<CanvasScaler>(canvasObject);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        FindOrCreateComponent<GraphicRaycaster>(canvasObject);
        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.SetParent(null, false);

        TMP_FontAsset font = TMP_Settings.defaultFontAsset;
        Transform canvasRoot = canvasObject.transform;
        CreatePanel(canvasRoot, "WorldPanel", new Vector2(0f, 1f), new Vector2(16f, -12f), new Vector2(285f, 430f));
        CreatePanel(canvasRoot, "BrushPanel", new Vector2(0f, 1f), new Vector2(16f, -450f), new Vector2(140f, 620f));
        CreatePanel(canvasRoot, "RightInspectorPanel", new Vector2(1f, 1f), new Vector2(-16f, -108f), new Vector2(320f, 705f));
        CreatePanel(canvasRoot, "ToolbarPanel", new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(1240f, 56f));
        TMP_Text title = CreateTmp(canvasRoot, "WorldTitle", new Vector2(0f, 1f), new Vector2(312f, -24f),
            new Vector2(165f, 40f), 19, TextAlignmentOptions.MidlineLeft, font);
        title.text = "世界编辑器";
        TMP_Text source = CreateTmp(canvasRoot, "SourceHint", new Vector2(0f, 0f), new Vector2(330f, 76f),
            new Vector2(1240f, 24f), 14, TextAlignmentOptions.MidlineLeft, font);
        TMP_Text status = CreateTmp(canvasRoot, "Status", new Vector2(0.5f, 0f), new Vector2(0f, 22f),
            new Vector2(1400f, 36f), 16, TextAlignmentOptions.Center, font);
        TMP_Text hover = CreateTmp(canvasRoot, "Hover", new Vector2(1f, 0f), new Vector2(-180f, 22f),
            new Vector2(280f, 36f), 14, TextAlignmentOptions.MidlineRight, font);
        TMP_Text selection = CreateTmp(canvasRoot, "Selection", new Vector2(1f, 1f), new Vector2(-180f, -28f),
            new Vector2(320f, 28f), 14, TextAlignmentOptions.MidlineRight, font);

        Button save = CreateButton(canvasRoot, "SaveButton", "保存", new Vector2(0.5f, 1f), new Vector2(-420f, -36f));
        Button reload = CreateButton(canvasRoot, "ReloadButton", "重载", new Vector2(0.5f, 1f), new Vector2(-300f, -36f));
        Button undo = CreateButton(canvasRoot, "UndoButton", "撤销", new Vector2(0.5f, 1f), new Vector2(-180f, -36f));
        Button redo = CreateButton(canvasRoot, "RedoButton", "重做", new Vector2(0.5f, 1f), new Vector2(-60f, -36f));
        Button duplicate = CreateButton(canvasRoot, "DuplicateButton", "复制可写", new Vector2(0.5f, 1f), new Vector2(80f, -36f));
        Button newFinite = CreateButton(canvasRoot, "NewFiniteButton", "新建有限", new Vector2(0.5f, 1f), new Vector2(220f, -36f));
        Button newInfinite = CreateButton(canvasRoot, "NewInfiniteButton", "新建无限", new Vector2(0.5f, 1f), new Vector2(360f, -36f));
        Button validate = CreateButton(canvasRoot, "ValidateButton", "校验", new Vector2(0.5f, 1f), new Vector2(480f, -36f));
        Button generate = CreateButton(canvasRoot, "GenerateVisibleButton", "柏林生成", new Vector2(0.5f, 1f), new Vector2(620f, -36f));
        Button randomSeed = CreateButton(canvasRoot, "RandomSeedButton", "随机种子", new Vector2(0.5f, 1f), new Vector2(760f, -36f));
        backButton = CreateButton(canvasRoot, "BackButton", "返回", new Vector2(1f, 1f), new Vector2(-70f, -36f));

        // 新建、导入与噪声参数均是场景内 uGUI，运行时代码只读取和绑定。
        TMP_InputField newId = CreateInput(canvasRoot, "NewWorldIdInput", "地图 ID", new Vector2(0f, 1f), new Vector2(158f, -76f), 248f, "user-map");
        TMP_InputField newName = CreateInput(canvasRoot, "NewWorldNameInput", "地图名称", new Vector2(0f, 1f), new Vector2(158f, -112f), 248f, "新地图");
        TMP_InputField chunksX = CreateInput(canvasRoot, "ChunksXInput", "列", new Vector2(0f, 1f), new Vector2(58f, -148f), 82f, "3");
        TMP_InputField chunksY = CreateInput(canvasRoot, "ChunksYInput", "行", new Vector2(0f, 1f), new Vector2(150f, -148f), 82f, "3");
        TMP_InputField chunkSize = CreateInput(canvasRoot, "ChunkSizeInput", "尺寸", new Vector2(0f, 1f), new Vector2(242f, -148f), 82f, "10");
        TMP_InputField seed = CreateInput(canvasRoot, "SeedInput", "种子", new Vector2(0f, 1f), new Vector2(158f, -184f), 248f, "1");
        TMP_InputField heightFrequency = CreateInput(canvasRoot, "HeightFrequencyInput", "高度频率", new Vector2(0f, 1f), new Vector2(158f, -220f), 248f, "0.075");
        TMP_InputField decorDensity = CreateInput(canvasRoot, "DecorationDensityInput", "装饰密度", new Vector2(0f, 1f), new Vector2(158f, -256f), 248f, "0.16");
        TMP_InputField enemyDensity = CreateInput(canvasRoot, "EnemyDensityInput", "敌人密度", new Vector2(0f, 1f), new Vector2(158f, -292f), 248f, "0.14");
        TMP_InputField importPath = CreateInput(canvasRoot, "ImportPathInput", "导入目录", new Vector2(0f, 1f), new Vector2(158f, -328f), 248f, "地图文件夹路径");
        Button import = CreateButton(canvasRoot, "ImportWorldButton", "导入目录", new Vector2(0f, 1f), new Vector2(82f, -366f), 120f, 30f);
        Button refreshWorlds = CreateButton(canvasRoot, "RefreshWorldsButton", "刷新世界", new Vector2(0f, 1f), new Vector2(216f, -366f), 120f, 30f);
        Button deleteWorld = CreateButton(canvasRoot, "DeleteWorldButton", "删除地图", new Vector2(0f, 1f), new Vector2(82f, -402f), 120f, 30f);
        TMP_InputField worldName = CreateInput(canvasRoot, "WorldNameInput", "名称", new Vector2(0f, 1f), new Vector2(196f, -402f), 92f, "地图名称");
        Button applyWorld = CreateButton(canvasRoot, "ApplyWorldSettingsButton", "应用", new Vector2(0f, 1f), new Vector2(270f, -402f), 48f, 30f);

        Button terrain = CreateButton(canvasRoot, "TerrainTool", "地形", new Vector2(0f, 1f), new Vector2(86f, -474f), 128f);
        Button heightUp = CreateButton(canvasRoot, "HeightUpTool", "抬高", new Vector2(0f, 1f), new Vector2(86f, -510f), 128f, 30f);
        Button heightDown = CreateButton(canvasRoot, "HeightDownTool", "挖低", new Vector2(0f, 1f), new Vector2(86f, -546f), 128f, 30f);
        Button obj = CreateButton(canvasRoot, "ObjectTool", "装饰", new Vector2(0f, 1f), new Vector2(86f, -582f), 128f, 30f);
        Button unit = CreateButton(canvasRoot, "UnitTool", "单位", new Vector2(0f, 1f), new Vector2(86f, -618f), 128f, 30f);
        Button erase = CreateButton(canvasRoot, "EraseTool", "删除", new Vector2(0f, 1f), new Vector2(86f, -654f), 128f, 30f);
        Button eraseDeco = CreateButton(canvasRoot, "EraseDecorationTool", "清装饰", new Vector2(0f, 1f), new Vector2(86f, -690f), 128f, 30f);
        Button eraseUnit = CreateButton(canvasRoot, "EraseUnitTool", "清单位", new Vector2(0f, 1f), new Vector2(86f, -726f), 128f, 30f);
        Button select = CreateButton(canvasRoot, "SelectTool", "框选", new Vector2(0f, 1f), new Vector2(86f, -762f), 128f, 30f);

        Button[] terrainPalette = new Button[WorldTerrainCatalog.Terrains.Length];
        for (int i = 0; i < terrainPalette.Length; i++)
        {
            terrainPalette[i] = CreateButton(canvasRoot, "TerrainBrush" + i, WorldTerrainCatalog.Terrains[i].Label,
                new Vector2(0f, 1f), new Vector2(86f, -810f - i * 34f), 128f, 30f);
        }

        string[] decoLabels = { "树", "石", "营" };
        Button[] decoPalette = new Button[decoLabels.Length];
        for (int i = 0; i < decoPalette.Length; i++)
        {
            decoPalette[i] = CreateButton(canvasRoot, "DecoBrush" + i, decoLabels[i],
                new Vector2(1f, 1f), new Vector2(-165f, -148f - i * 36f), 290f, 30f);
        }

        Button[] unitPalette = new Button[5];
        for (int i = 0; i < unitPalette.Length; i++)
        {
            unitPalette[i] = CreateButton(canvasRoot, "UnitBrush" + i, "单位 " + (i + 1),
                new Vector2(1f, 1f), new Vector2(-165f, -306f - i * 34f), 290f, 30f);
        }

        TMP_Dropdown dropdown = EnsureDropdown(canvasRoot, font, "WorldList", new Vector2(16f, -16f), new Vector2(260f, 36f));
        TMP_Dropdown decorationList = EnsureDropdown(canvasRoot, font, "DecorationList", new Vector2(-165f, -266f), new Vector2(290f, 32f), new Vector2(1f, 1f));
        TMP_Dropdown unitList = EnsureDropdown(canvasRoot, font, "UnitList", new Vector2(-165f, -476f), new Vector2(290f, 32f), new Vector2(1f, 1f));
        TMP_Dropdown stageType = EnsureDropdown(canvasRoot, font, "StageTypeList", new Vector2(-165f, -552f), new Vector2(290f, 32f), new Vector2(1f, 1f));
        Toggle contour = CreateToggle(canvasRoot, "ContourToggle", "等高线", new Vector2(0f, 0f), new Vector2(90f, 64f), font);
        Toggle bounds = CreateToggle(canvasRoot, "ChunkBoundsToggle", "区块边界", new Vector2(0f, 0f), new Vector2(230f, 64f), font);
        bounds.SetIsOnWithoutNotify(true);

        TMP_Text stageTitle = CreateTmp(canvasRoot, "StageTitle", new Vector2(1f, 1f), new Vector2(-165f, -512f),
            new Vector2(290f, 26f), 17, TextAlignmentOptions.MidlineLeft, font);
        stageTitle.text = "关卡：选择地图格";
        TMP_InputField stageId = CreateInput(canvasRoot, "StageIdInput", "稳定 ID", new Vector2(1f, 1f), new Vector2(-165f, -588f), 290f, "stage-id");
        TMP_InputField stageName = CreateInput(canvasRoot, "StageNameInput", "关卡名称", new Vector2(1f, 1f), new Vector2(-165f, -624f), 290f, "关卡名称");
        TMP_InputField stageReward = CreateInput(canvasRoot, "StageRewardInput", "奖励池", new Vector2(1f, 1f), new Vector2(-165f, -660f), 290f, "奖励池 ID");
        TMP_InputField stageRequired = CreateInput(canvasRoot, "StageRequiredKeyInput", "需要钥匙", new Vector2(1f, 1f), new Vector2(-165f, -696f), 290f, "钥匙 ID");
        TMP_InputField stageDrop = CreateInput(canvasRoot, "StageDropKeyInput", "掉落钥匙", new Vector2(1f, 1f), new Vector2(-165f, -732f), 290f, "钥匙 ID");
        Button applyStage = CreateButton(canvasRoot, "ApplyStageButton", "应用关卡", new Vector2(1f, 1f), new Vector2(-220f, -774f), 110f, 30f);
        Button startTile = CreateButton(canvasRoot, "SetStartTileButton", "设出生点", new Vector2(1f, 1f), new Vector2(-96f, -774f), 110f, 30f);

        GameObject viewHost = FindOrCreate("WorldEditorViewHost", canvasRoot);
        WorldEditorView view = FindOrCreateComponent<WorldEditorView>(viewHost);
        SerializedObject so = new SerializedObject(view);
        so.FindProperty("statusText").objectReferenceValue = status;
        so.FindProperty("hoverText").objectReferenceValue = hover;
        so.FindProperty("selectionText").objectReferenceValue = selection;
        so.FindProperty("sourceText").objectReferenceValue = source;
        so.FindProperty("worldTitleText").objectReferenceValue = title;
        so.FindProperty("saveButton").objectReferenceValue = save;
        so.FindProperty("reloadButton").objectReferenceValue = reload;
        so.FindProperty("undoButton").objectReferenceValue = undo;
        so.FindProperty("redoButton").objectReferenceValue = redo;
        so.FindProperty("duplicateButton").objectReferenceValue = duplicate;
        so.FindProperty("newFiniteButton").objectReferenceValue = newFinite;
        so.FindProperty("newInfiniteButton").objectReferenceValue = newInfinite;
        so.FindProperty("validateButton").objectReferenceValue = validate;
        so.FindProperty("generateVisibleButton").objectReferenceValue = generate;
        so.FindProperty("randomSeedButton").objectReferenceValue = randomSeed;
        so.FindProperty("refreshWorldsButton").objectReferenceValue = refreshWorlds;
        so.FindProperty("importWorldButton").objectReferenceValue = import;
        so.FindProperty("deleteWorldButton").objectReferenceValue = deleteWorld;
        so.FindProperty("applyWorldSettingsButton").objectReferenceValue = applyWorld;
        so.FindProperty("applyStageButton").objectReferenceValue = applyStage;
        so.FindProperty("setStartTileButton").objectReferenceValue = startTile;
        so.FindProperty("terrainToolButton").objectReferenceValue = terrain;
        so.FindProperty("heightUpToolButton").objectReferenceValue = heightUp;
        so.FindProperty("heightDownToolButton").objectReferenceValue = heightDown;
        so.FindProperty("objectToolButton").objectReferenceValue = obj;
        so.FindProperty("unitToolButton").objectReferenceValue = unit;
        so.FindProperty("eraseToolButton").objectReferenceValue = erase;
        so.FindProperty("eraseDecorationToolButton").objectReferenceValue = eraseDeco;
        so.FindProperty("eraseUnitToolButton").objectReferenceValue = eraseUnit;
        so.FindProperty("selectToolButton").objectReferenceValue = select;
        AssignArray(so.FindProperty("terrainPaletteButtons"), terrainPalette);
        AssignArray(so.FindProperty("decorationPaletteButtons"), decoPalette);
        AssignArray(so.FindProperty("unitPaletteButtons"), unitPalette);
        so.FindProperty("worldList").objectReferenceValue = dropdown;
        so.FindProperty("decorationList").objectReferenceValue = decorationList;
        so.FindProperty("unitList").objectReferenceValue = unitList;
        so.FindProperty("stageTypeList").objectReferenceValue = stageType;
        so.FindProperty("newWorldIdInput").objectReferenceValue = newId;
        so.FindProperty("newWorldNameInput").objectReferenceValue = newName;
        so.FindProperty("chunksXInput").objectReferenceValue = chunksX;
        so.FindProperty("chunksYInput").objectReferenceValue = chunksY;
        so.FindProperty("chunkSizeInput").objectReferenceValue = chunkSize;
        so.FindProperty("seedInput").objectReferenceValue = seed;
        so.FindProperty("heightFrequencyInput").objectReferenceValue = heightFrequency;
        so.FindProperty("decorationDensityInput").objectReferenceValue = decorDensity;
        so.FindProperty("enemyDensityInput").objectReferenceValue = enemyDensity;
        so.FindProperty("importPathInput").objectReferenceValue = importPath;
        so.FindProperty("worldNameInput").objectReferenceValue = worldName;
        so.FindProperty("stageIdInput").objectReferenceValue = stageId;
        so.FindProperty("stageNameInput").objectReferenceValue = stageName;
        so.FindProperty("stageRewardInput").objectReferenceValue = stageReward;
        so.FindProperty("stageRequiredKeyInput").objectReferenceValue = stageRequired;
        so.FindProperty("stageDropKeyInput").objectReferenceValue = stageDrop;
        so.FindProperty("stageTitleText").objectReferenceValue = stageTitle;
        so.FindProperty("contourToggle").objectReferenceValue = contour;
        so.FindProperty("chunkBoundsToggle").objectReferenceValue = bounds;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(view);
        return view;
    }

    private static void BindController(RuntimeWorldEditorController controller, BoardGenerator board,
        WorldEditorView view, WorldEditorInputController input, Button backButton)
    {
        SerializedObject so = new SerializedObject(controller);
        so.FindProperty("board").objectReferenceValue = board;
        so.FindProperty("view").objectReferenceValue = view;
        so.FindProperty("input").objectReferenceValue = input;
        so.FindProperty("backButton").objectReferenceValue = backButton;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(controller);
    }

    private static TMP_Dropdown EnsureDropdown(Transform canvas, TMP_FontAsset font, string name, Vector2 position,
        Vector2 size, Vector2? anchorOverride = null)
    {
        GameObject dropdownObject = FindOrCreate(name, canvas);
        RectTransform rect = dropdownObject.GetComponent<RectTransform>() ?? dropdownObject.AddComponent<RectTransform>();
        Vector2 anchor = anchorOverride ?? new Vector2(0f, 1f);
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchorOverride.HasValue ? new Vector2(0.5f, 0.5f) : anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        Image image = FindOrCreateComponent<Image>(dropdownObject);
        image.color = new Color(0.12f, 0.16f, 0.22f, 0.95f);
        TMP_Dropdown dropdown = FindOrCreateComponent<TMP_Dropdown>(dropdownObject);
        dropdown.targetGraphic = image;
        TMP_Text label = CreateTmp(dropdownObject.transform, "Label", new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(size.x - 20f, size.y - 4f), 16, TextAlignmentOptions.MidlineLeft, font);
        dropdown.captionText = label;
        Transform oldTemplate = dropdownObject.transform.Find("Template");
        if (oldTemplate != null) Object.DestroyImmediate(oldTemplate.gameObject);

        GameObject template = FindOrCreate("Template", dropdownObject.transform);
        RectTransform templateRect = template.GetComponent<RectTransform>() ?? template.AddComponent<RectTransform>();
        templateRect.anchorMin = new Vector2(0f, 0f);
        templateRect.anchorMax = new Vector2(1f, 0f);
        templateRect.pivot = new Vector2(0.5f, 1f);
        templateRect.anchoredPosition = new Vector2(0f, -2f);
        templateRect.sizeDelta = new Vector2(0f, 174f);
        Image templateImage = FindOrCreateComponent<Image>(template);
        templateImage.color = new Color(0.045f, 0.065f, 0.1f, 0.99f);
        ScrollRect scroll = FindOrCreateComponent<ScrollRect>(template);
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        GameObject viewport = FindOrCreate("Viewport", template.transform);
        RectTransform viewportRect = viewport.GetComponent<RectTransform>() ?? viewport.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(2f, 2f);
        viewportRect.offsetMax = new Vector2(-2f, -2f);
        Image viewportImage = FindOrCreateComponent<Image>(viewport);
        viewportImage.color = Color.white;
        viewportImage.raycastTarget = true;
        Mask viewportMask = FindOrCreateComponent<Mask>(viewport);
        viewportMask.showMaskGraphic = false;

        GameObject content = FindOrCreate("Content", viewport.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>() ?? content.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0f, 30f);

        GameObject item = FindOrCreate("Item", content.transform);
        RectTransform itemRect = item.GetComponent<RectTransform>() ?? item.AddComponent<RectTransform>();
        itemRect.anchorMin = new Vector2(0f, 0.5f);
        itemRect.anchorMax = new Vector2(1f, 0.5f);
        itemRect.pivot = new Vector2(0.5f, 0.5f);
        itemRect.anchoredPosition = Vector2.zero;
        itemRect.sizeDelta = new Vector2(0f, 30f);
        Image itemBackground = FindOrCreateComponent<Image>(item);
        itemBackground.color = new Color(0.08f, 0.11f, 0.16f, 0.98f);
        Toggle itemToggle = FindOrCreateComponent<Toggle>(item);
        itemToggle.targetGraphic = itemBackground;
        TMP_Text itemLabel = CreateTmp(item.transform, "Item Label", new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(size.x - 16f, 28f), 14, TextAlignmentOptions.MidlineLeft, font);
        itemLabel.raycastTarget = false;

        scroll.viewport = viewportRect;
        scroll.content = contentRect;
        dropdown.itemText = itemLabel;
        dropdown.template = templateRect;
        template.SetActive(false);

        return dropdown;
    }

    private static TMP_InputField CreateInput(Transform parent, string name, string label, Vector2 anchor,
        Vector2 pos, float width, string initialValue)
    {
        GameObject obj = FindOrCreate(name, parent);
        RectTransform rect = obj.GetComponent<RectTransform>() ?? obj.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = new Vector2(width, 32f);
        Image image = FindOrCreateComponent<Image>(obj);
        image.color = new Color(0.08f, 0.11f, 0.16f, 0.94f);
        TMP_InputField input = FindOrCreateComponent<TMP_InputField>(obj);
        input.targetGraphic = image;
        bool hasInlineLabel = width >= 120f;
        if (hasInlineLabel)
        {
            TMP_Text fieldLabel = CreateTmp(obj.transform, "FieldLabel", new Vector2(0f, 0.5f), new Vector2(8f, 0f),
                new Vector2(62f, 26f), 12, TextAlignmentOptions.MidlineLeft, TMP_Settings.defaultFontAsset);
            fieldLabel.text = label;
            fieldLabel.color = new Color(0.72f, 0.78f, 0.88f, 0.95f);
        }
        GameObject textArea = FindOrCreate("Text Area", obj.transform);
        RectTransform areaRect = textArea.GetComponent<RectTransform>() ?? textArea.AddComponent<RectTransform>();
        areaRect.anchorMin = Vector2.zero;
        areaRect.anchorMax = Vector2.one;
        areaRect.offsetMin = new Vector2(hasInlineLabel ? 72f : 8f, 2f);
        areaRect.offsetMax = new Vector2(-8f, -2f);
        RectMask2D mask = FindOrCreateComponent<RectMask2D>(textArea);
        TMP_Text text = CreateTmp(textArea.transform, "Text", new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(width - 16f, 28f), 14, TextAlignmentOptions.MidlineLeft, TMP_Settings.defaultFontAsset);
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = Vector2.zero;
        text.raycastTarget = false;
        TMP_Text placeholder = CreateTmp(textArea.transform, "Placeholder", new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(width - (hasInlineLabel ? 80f : 16f), 28f), 13, TextAlignmentOptions.MidlineLeft, TMP_Settings.defaultFontAsset);
        RectTransform placeholderRect = placeholder.rectTransform;
        placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
        placeholderRect.pivot = new Vector2(0.5f, 0.5f);
        placeholderRect.anchoredPosition = Vector2.zero;
        placeholderRect.sizeDelta = Vector2.zero;
        placeholder.text = hasInlineLabel ? string.Empty : label;
        placeholder.color = new Color(0.7f, 0.74f, 0.8f, 0.65f);
        input.textComponent = text;
        input.placeholder = placeholder;
        input.text = initialValue;
        return input;
    }

    private static void AssignArray(SerializedProperty array, Button[] buttons)
    {
        array.arraySize = buttons.Length;
        for (int i = 0; i < buttons.Length; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];
    }

    private static Button CreateButton(Transform parent, string name, string label, Vector2 anchor, Vector2 pos,
        float width = 108f, float height = 36f)
    {
        GameObject obj = FindOrCreate(name, parent);
        RectTransform rect = obj.GetComponent<RectTransform>() ?? obj.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = new Vector2(width, height);
        Image image = FindOrCreateComponent<Image>(obj);
        image.color = new Color(0.08f, 0.11f, 0.16f, 0.92f);
        Button button = FindOrCreateComponent<Button>(obj);
        TMP_Text text = CreateTmp(obj.transform, "Label", new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(width, height), 15, TextAlignmentOptions.Center, TMP_Settings.defaultFontAsset);
        text.text = label;
        return button;
    }

    private static Image CreatePanel(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        GameObject obj = FindOrCreate(name, parent);
        RectTransform rect = obj.GetComponent<RectTransform>() ?? obj.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;
        Image image = FindOrCreateComponent<Image>(obj);
        image.color = new Color(0.025f, 0.04f, 0.07f, 0.82f);
        image.raycastTarget = false;
        obj.transform.SetAsFirstSibling();
        return image;
    }

    private static Toggle CreateToggle(Transform parent, string name, string label, Vector2 anchor, Vector2 pos,
        TMP_FontAsset font)
    {
        GameObject obj = FindOrCreate(name, parent);
        RectTransform rect = obj.GetComponent<RectTransform>() ?? obj.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = pos;
        rect.sizeDelta = new Vector2(140f, 28f);
        Toggle toggle = FindOrCreateComponent<Toggle>(obj);
        Image background = FindOrCreateComponent<Image>(obj);
        background.color = new Color(0.08f, 0.11f, 0.16f, 0.96f);
        background.raycastTarget = true;
        GameObject checkObject = FindOrCreate("Checkmark", obj.transform);
        RectTransform checkRect = checkObject.GetComponent<RectTransform>() ?? checkObject.AddComponent<RectTransform>();
        checkRect.anchorMin = checkRect.anchorMax = new Vector2(0f, 0.5f);
        checkRect.pivot = new Vector2(0f, 0.5f);
        checkRect.anchoredPosition = new Vector2(5f, 0f);
        checkRect.sizeDelta = new Vector2(20f, 20f);
        Image checkmark = FindOrCreateComponent<Image>(checkObject);
        checkmark.color = new Color(0.2f, 0.85f, 1f, 1f);
        checkmark.raycastTarget = false;
        toggle.targetGraphic = background;
        toggle.graphic = checkmark;
        CreateTmp(obj.transform, "Label", new Vector2(0.5f, 0.5f), new Vector2(18f, 0f),
            new Vector2(106f, 24f), 14, TextAlignmentOptions.MidlineLeft, font).text = label;
        return toggle;
    }

    private static TMP_Text CreateTmp(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size,
        int fontSize, TextAlignmentOptions align, TMP_FontAsset font)
    {
        GameObject obj = FindOrCreate(name, parent);
        RectTransform rect = obj.GetComponent<RectTransform>() ?? obj.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;
        TextMeshProUGUI text = FindOrCreateComponent<TextMeshProUGUI>(obj);
        text.font = font;
        text.fontSize = fontSize;
        text.alignment = align;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static GameObject FindOrCreate(string name, Transform parent = null)
    {
        if (parent != null)
        {
            Transform existing = parent.Find(name);
            if (existing != null) return existing.gameObject;
        }
        else
        {
            GameObject found = GameObject.Find(name);
            if (found != null) return found;
        }

        GameObject created = new GameObject(name, typeof(RectTransform));
        if (parent != null) created.transform.SetParent(parent, false);
        return created;
    }

    private static GameObject FindOrCreate3D(string name)
    {
        GameObject found = GameObject.Find(name);
        if (found != null)
        {
            if (found.GetComponent<RectTransform>() == null) return found;
            Object.DestroyImmediate(found);
        }

        return new GameObject(name);
    }

    private static T FindOrCreateComponent<T>(GameObject obj) where T : Component
    {
        T component = obj.GetComponent<T>();
        return component != null ? component : obj.AddComponent<T>();
    }
}
#endif
