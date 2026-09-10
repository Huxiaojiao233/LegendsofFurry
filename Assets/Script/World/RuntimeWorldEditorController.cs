using System;
using System.Collections.Generic;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 游戏内世界编辑器入口。只绑定场景里的 uGUI 与共享 Board/Streamer，不再动态拼 Canvas。
/// </summary>
[DefaultExecutionOrder(-10)]
public sealed class RuntimeWorldEditorController : MonoBehaviour
{
    [SerializeField] private BoardGenerator board;
    [SerializeField] private WorldEditorView view;
    [SerializeField] private WorldEditorInputController input;
    [SerializeField] private Button backButton;

    private WorldEditorSession session;
    private readonly HashSet<(int, int)> pendingChunks = new HashSet<(int, int)>();
    private readonly List<WorldListEntry> worldEntries = new List<WorldListEntry>();
    private ChunkPosition selectedChunk;
    private bool suppressWorldDropdown;
    private bool deleteArmed;
    private WorldChunkStreamer streamer;

    public WorldEditorSession Session => session;
    public BoardGenerator Board => board;

    public static void Open()
    {
        SceneManager.LoadScene("S_WorldEditor");
    }

    private void Awake()
    {
        if (board == null) board = FindAnyObjectByType<BoardGenerator>();
        if (view == null) view = FindAnyObjectByType<WorldEditorView>();
        if (input == null) input = FindAnyObjectByType<WorldEditorInputController>();
        if (board == null)
        {
            view?.SetStatus("场景缺少 BoardGenerator。请运行 WorldEditorSceneSetup。");
            BindUi();
            return;
        }

        if (!ContentRuntime.IsLoaded) ContentRuntime.EnsureLoaded();
        BindUi();
        OpenDefault();
    }

    private void LateUpdate()
    {
        if (pendingChunks.Count == 0 || board == null) return;
        WorldChunkStreamer streamer = board.GetComponent<WorldChunkStreamer>();
        foreach ((int x, int y) in pendingChunks)
        {
            var chunk = new ChunkPosition(x, y);
            if (streamer != null)
            {
                streamer.Reload(chunk);
                ReloadNeighbor(streamer, new ChunkPosition(x + 1, y));
                ReloadNeighbor(streamer, new ChunkPosition(x - 1, y));
                ReloadNeighbor(streamer, new ChunkPosition(x, y + 1));
                ReloadNeighbor(streamer, new ChunkPosition(x, y - 1));
            }
            else
            {
                board.EnsureChunkData(chunk, out _);
                board.GenerateBoard();
            }
        }

        streamer?.Flush();
        pendingChunks.Clear();
    }

    public void OpenWorld(string worldId)
    {
        session = WorldEditorSession.Open(worldId);
        AttachSession();
    }

    public void DuplicateWritable()
    {
        if (session?.World == null) return;
        string id = session.World.WorldId + "-edit";
        session = session.DuplicateToWritable(id, session.World.DisplayName + " 副本");
        AttachSession();
        view?.SetStatus(session.Status);
    }

    public void CreateFiniteWorld()
    {
        CreateWorld(false);
    }

    public void CreateInfiniteWorld()
    {
        CreateWorld(true);
    }

    private void OpenDefault()
    {
        WorldDefinition world = WorldCatalog.Default;
        if (world == null)
        {
            view?.SetStatus("没有可编辑地图。");
            return;
        }

        OpenWorld(world.WorldId);
    }

    private void DetachSession()
    {
        if (session == null) return;
        session.ChunkChanged -= OnChunkChanged;
        session.StatusChanged -= RefreshStatus;
        if (input != null) input.ChunkSelected -= ShowStageInspector;
        if (streamer != null) streamer.ChunkLoaded -= ApplyEditorOverlays;
    }

    private void AttachSession()
    {
        DetachSession();
        if (session == null || board == null) return;
        session.ChunkChanged += OnChunkChanged;
        session.StatusChanged += RefreshStatus;
        board.BindChunkProvider(session.Provider);
        board.BuildWorldBoard(session.World, useStreamer: true);
        streamer = board.GetComponent<WorldChunkStreamer>();
        if (streamer != null)
        {
            streamer.AutoCollectFromCamera = true;
            streamer.Bind(board, session.Provider);
            streamer.ChunkLoaded += ApplyEditorOverlays;
        }

        if (input == null) input = gameObject.AddComponent<WorldEditorInputController>();
        input.Bind(session, board, view);
        input.ChunkSelected += ShowStageInspector;
        selectedChunk = new ChunkPosition(session.World.StartChunkX, session.World.StartChunkY);
        RefreshStatus();
        RefreshWorldList();
        view?.SetWorldTitle(session.World.DisplayName);
        view?.SetSourceHint(session.IsReadOnly ? "只读来源，保存前请复制为可写副本。" : "可写副本：" + session.Folder);
        if (view != null)
        {
            if (view.WorldNameInput != null) view.WorldNameInput.text = session.World.DisplayName;
            if (view.SeedInput != null) view.SeedInput.text = session.World.Seed.ToString();
        }
        ShowStageInspector(selectedChunk);
        ApplyEditorOverlaysToResidents();
    }

    private void BindUi()
    {
        Bind(view?.SaveButton, () => session?.Save());
        Bind(view?.ReloadButton, () =>
        {
            session?.Reload();
            AttachSession();
        });
        Bind(view?.UndoButton, () => session?.Commands.Undo());
        Bind(view?.RedoButton, () => session?.Commands.Redo());
        Bind(view?.DuplicateButton, DuplicateWritable);
        Bind(view?.NewFiniteButton, CreateFiniteWorld);
        Bind(view?.NewInfiniteButton, CreateInfiniteWorld);
        Bind(view?.ValidateButton, () =>
        {
            List<string> issues = session?.Validate() ?? new List<string> { "没有打开的地图。" };
            view?.SetStatus(issues.Count == 0 ? "校验通过。" : issues[0]);
        });
        Bind(view?.GenerateVisibleButton, GenerateVisible);
        Bind(view?.RandomSeedButton, () =>
        {
            if (view?.SeedInput != null)
                view.SeedInput.text = UnityEngine.Random.Range(1, int.MaxValue).ToString();
        });
        Bind(view?.RefreshWorldsButton, RefreshWorldList);
        Bind(view?.ImportWorldButton, ImportWorld);
        Bind(view?.DeleteWorldButton, DeleteCurrentWorld);
        Bind(view?.ApplyWorldSettingsButton, ApplyWorldSettings);
        Bind(view?.ApplyStageButton, ApplyStageSettings);
        Bind(view?.SetStartTileButton, SetStartTile);
        Bind(view?.TerrainToolButton, () => input?.SetTool(WorldEditorInputController.Tool.Terrain));
        Bind(view?.HeightUpToolButton, () => input?.SetTool(WorldEditorInputController.Tool.HeightUp));
        Bind(view?.HeightDownToolButton, () => input?.SetTool(WorldEditorInputController.Tool.HeightDown));
        Bind(view?.ObjectToolButton, () => input?.SetTool(WorldEditorInputController.Tool.Object));
        Bind(view?.UnitToolButton, () => input?.SetTool(WorldEditorInputController.Tool.Unit));
        Bind(view?.EraseToolButton, () => input?.SetTool(WorldEditorInputController.Tool.Erase));
        Bind(view?.EraseDecorationToolButton, () => input?.SetTool(WorldEditorInputController.Tool.EraseDecoration));
        Bind(view?.EraseUnitToolButton, () => input?.SetTool(WorldEditorInputController.Tool.EraseUnit));
        Bind(view?.SelectToolButton, () => input?.SetTool(WorldEditorInputController.Tool.Select));
        Bind(backButton, () => SceneManager.LoadScene("S_Menu"));
        if (view?.ContourToggle != null)
        {
            view.ContourToggle.onValueChanged.RemoveAllListeners();
            view.ContourToggle.onValueChanged.AddListener(enabled =>
            {
                ApplyEditorOverlaysToResidents();
                view.SetStatus(enabled ? "等高线已开启。" : "等高线已关闭。");
            });
        }
        if (view?.ChunkBoundsToggle != null)
        {
            view.ChunkBoundsToggle.onValueChanged.RemoveAllListeners();
            view.ChunkBoundsToggle.onValueChanged.AddListener(enabled =>
            {
                ApplyEditorOverlaysToResidents();
                view.SetStatus(enabled ? "区块边界已开启。" : "区块边界已关闭。");
            });
        }
        BindPalette();
    }

    private void BindPalette()
    {
        Button[] terrains = view != null ? view.TerrainPaletteButtons : null;
        if (terrains != null)
        {
            int count = Mathf.Min(terrains.Length, WorldTerrainCatalog.Terrains.Length);
            for (int i = 0; i < count; i++)
            {
                string id = WorldTerrainCatalog.Terrains[i].Id;
                Bind(terrains[i], () => input?.SetTerrain(id));
            }
        }

        Button[] decos = view != null ? view.DecorationPaletteButtons : null;
        if (decos != null)
        {
            var decorations = WorldDecorationCatalog.All;
            int count = Mathf.Min(decos.Length, decorations.Count);
            for (int i = 0; i < count; i++)
            {
                string id = decorations[i].Id;
                Bind(decos[i], () => input?.SetDecoration(id));
                SetButtonLabel(decos[i], decorations[i].Label);
            }
        }

        if (view?.DecorationList != null)
        {
            view.DecorationList.ClearOptions();
            var options = new List<string>();
            var ids = new List<string>();
            foreach (WorldDecorationEntry entry in WorldDecorationCatalog.All)
            {
                ids.Add(entry.Id);
                options.Add(entry.Label + " (" + entry.Id + ")");
            }
            view.DecorationList.AddOptions(options);
            view.DecorationList.onValueChanged.RemoveAllListeners();
            view.DecorationList.onValueChanged.AddListener(index =>
            {
                if (index >= 0 && index < ids.Count) input?.SetDecoration(ids[index]);
            });
            if (ids.Count > 0) input?.SetDecoration(ids[0]);
        }

        Button[] units = view != null ? view.UnitPaletteButtons : null;
        if (units != null && ContentRuntime.IsLoaded)
        {
            int index = 0;
            foreach (UnitDefinition unit in ContentRuntime.Registry.Units)
            {
                if (index >= units.Length) break;
                if (!IsWorldDeployable(unit)) continue;
                string id = unit.UnitId;
                Bind(units[index], () => input?.SetUnit(id));
                SetButtonLabel(units[index], unit.DisplayName);
                index++;
            }
        }

        if (view?.UnitList != null)
        {
            view.UnitList.ClearOptions();
            var options = new List<string>();
            var ids = new List<string>();
            if (ContentRuntime.IsLoaded)
            {
                foreach (UnitDefinition unit in ContentRuntime.Registry.Units)
                {
                    if (!IsWorldDeployable(unit)) continue;
                    ids.Add(unit.UnitId);
                    options.Add(string.IsNullOrWhiteSpace(unit.DisplayName) ? unit.UnitId : unit.DisplayName + " (" + unit.UnitId + ")");
                }
            }
            view.UnitList.AddOptions(options);
            view.UnitList.onValueChanged.RemoveAllListeners();
            view.UnitList.onValueChanged.AddListener(index =>
            {
                if (index >= 0 && index < ids.Count) input?.SetUnit(ids[index]);
            });
            if (ids.Count > 0) input?.SetUnit(ids[0]);
        }

        if (view?.StageTypeList != null)
        {
            view.StageTypeList.ClearOptions();
            view.StageTypeList.AddOptions(new List<string>
            {
                ContentStageTypeKeys.Start, ContentStageTypeKeys.Battle, ContentStageTypeKeys.Rest,
                ContentStageTypeKeys.Reward, ContentStageTypeKeys.Shop, ContentStageTypeKeys.Boss
            });
        }
    }

    private void RefreshWorldList()
    {
        if (view?.WorldList == null) return;
        worldEntries.Clear();
        worldEntries.AddRange(WorldEditorSession.ListWorlds());
        suppressWorldDropdown = true;
        view.WorldList.ClearOptions();
        var options = new List<string>(worldEntries.Count);
        int selected = 0;
        for (int i = 0; i < worldEntries.Count; i++)
        {
            WorldListEntry entry = worldEntries[i];
            options.Add($"{entry.DisplayName}{(entry.Writable ? "" : "（只读）")}");
            if (session != null && entry.WorldId == session.World.WorldId) selected = i;
        }

        view.WorldList.AddOptions(options);
        view.WorldList.onValueChanged.RemoveAllListeners();
        view.WorldList.value = selected;
        suppressWorldDropdown = false;
        view.WorldList.onValueChanged.AddListener(index =>
        {
            if (suppressWorldDropdown || index < 0 || index >= worldEntries.Count) return;
            if (session != null && session.Commands.IsDirty)
            {
                view.SetStatus("有未保存修改。请先保存或重载，再切换地图。");
                suppressWorldDropdown = true;
                view.WorldList.value = FindCurrentWorldIndex();
                suppressWorldDropdown = false;
            }
            else
                OpenWorld(worldEntries[index].WorldId);
        });
    }

    private void GenerateVisible()
    {
        if (session == null || board == null) return;
        WorldChunkStreamer streamer = board.GetComponent<WorldChunkStreamer>();
        var chunks = new List<ChunkPosition>();
        if (streamer != null)
        {
            // 驻留块即当前可见/预取集。
            WorldChunkView[] views = board.GetComponentsInChildren<WorldChunkView>(true);
            for (int i = 0; i < views.Length; i++)
                if (views[i] != null) chunks.Add(views[i].Position);
        }
        else if (session.World.Stages != null)
        {
            for (int i = 0; i < session.World.Stages.Count; i++)
            {
                StageDefinition stage = session.World.Stages[i];
                if (stage != null) chunks.Add(new ChunkPosition(stage.GridX, stage.GridY));
            }
        }

        if (!session.World.IsInfinite)
        {
            chunks.Clear();
            for (int y = session.World.BoundsMinY; y <= session.World.BoundsMaxY; y++)
                for (int x = session.World.BoundsMinX; x <= session.World.BoundsMaxX; x++)
                    chunks.Add(new ChunkPosition(x, y));
        }
        if (chunks.Count == 0) chunks.Add(new ChunkPosition(session.World.StartChunkX, session.World.StartChunkY));
        session.GenerateVisible(chunks, CreateNoiseSettings());
    }

    private void CreateWorld(bool infinite)
    {
        string stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        string fallbackId = (infinite ? "endless-" : "user-") + stamp;
        string id = ReadText(view?.NewWorldIdInput, fallbackId).ToLowerInvariant();
        string name = ReadText(view?.NewWorldNameInput, infinite ? "无限地图" : "新地图");
        int chunkSize = Mathf.Clamp(ReadInt(view?.ChunkSizeInput, 10), 2, 64);
        int seed = ReadInt(view?.SeedInput, UnityEngine.Random.Range(1, int.MaxValue));
        try
        {
            session = infinite
                ? WorldEditorSession.CreateInfinite(id, name, seed, chunkSize)
                : WorldEditorSession.CreateFinite(id, name,
                    Mathf.Clamp(ReadInt(view?.ChunksXInput, 3), 1, 64),
                    Mathf.Clamp(ReadInt(view?.ChunksYInput, 3), 1, 64), chunkSize);
            session.UpdateWorldSettings(name, seed);
            AttachSession();
        }
        catch (Exception ex)
        {
            view?.SetStatus("无法新建地图：" + ex.Message);
        }
    }

    private void ImportWorld()
    {
        string path = ReadText(view?.ImportPathInput, string.Empty);
        if (string.IsNullOrWhiteSpace(path))
        {
            view?.SetStatus("请输入待导入地图目录。");
            return;
        }
        try
        {
            session = WorldEditorSession.ImportFolder(path);
            AttachSession();
        }
        catch (Exception ex)
        {
            view?.SetStatus("导入失败：" + ex.Message);
        }
    }

    private void DeleteCurrentWorld()
    {
        if (session?.World == null) return;
        if (!deleteArmed)
        {
            deleteArmed = true;
            view?.SetStatus("再次点击“删除”确认删除当前可写地图。");
            return;
        }
        deleteArmed = false;
        string id = session.World.WorldId;
        if (!WorldEditorSession.TryDeleteUserWorld(id, out string error))
        {
            view?.SetStatus(error);
            return;
        }
        OpenDefault();
        RefreshWorldList();
    }

    private void ApplyWorldSettings()
    {
        if (session == null) return;
        session.UpdateWorldSettings(ReadText(view?.WorldNameInput, session.World.DisplayName),
            ReadInt(view?.SeedInput, session.World.Seed));
        RefreshStatus();
    }

    private void ShowStageInspector(ChunkPosition chunk)
    {
        selectedChunk = chunk;
        if (session == null || !session.Provider.TryGetChunk(chunk, out StageDefinition stage) || stage == null)
        {
            view?.SetStageTitle("关卡：未加载");
            return;
        }
        view?.SetStageTitle($"关卡 {chunk.X},{chunk.Y}");
        if (view == null) return;
        if (view.StageIdInput != null) view.StageIdInput.text = stage.StageId;
        if (view.StageNameInput != null) view.StageNameInput.text = stage.DisplayName;
        if (view.StageRewardInput != null) view.StageRewardInput.text = stage.RewardPoolId;
        if (view.StageRequiredKeyInput != null) view.StageRequiredKeyInput.text = stage.RequiredKeyId;
        if (view.StageDropKeyInput != null) view.StageDropKeyInput.text = stage.DropKeyId;
        if (view.StageTypeList != null)
        {
            int type = view.StageTypeList.options.FindIndex(item => item.text == stage.StageType);
            view.StageTypeList.SetValueWithoutNotify(Mathf.Max(0, type));
        }
    }

    private void ApplyStageSettings()
    {
        if (session == null || view == null) return;
        string stageType = view.StageTypeList != null && view.StageTypeList.options.Count > 0
            ? view.StageTypeList.options[view.StageTypeList.value].text : ContentStageTypeKeys.Battle;
        if (!session.TryUpdateStage(selectedChunk, ReadText(view.StageIdInput, string.Empty),
                ReadText(view.StageNameInput, string.Empty), stageType, ReadText(view.StageRewardInput, string.Empty),
                ReadText(view.StageRequiredKeyInput, string.Empty), ReadText(view.StageDropKeyInput, string.Empty),
                out string reason))
            view.SetStatus(reason);
        else
            ShowStageInspector(selectedChunk);
    }

    private void SetStartTile()
    {
        if (session == null || input == null) return;
        int tw = Mathf.Max(1, session.World.TerrainWidth);
        int th = Mathf.Max(1, session.World.TerrainHeight);
        Vector2Int tile = input.HasHover ? input.HoverCell :
            new Vector2Int(selectedChunk.X * tw + tw / 2, selectedChunk.Y * th + th / 2);
        session.SetStartTile(tile.x, tile.y);
    }

    private void ApplyEditorOverlays(ChunkPosition _, WorldChunkView chunk) =>
        chunk?.SetEditorOverlays(board, view != null && view.ContourToggle != null && view.ContourToggle.isOn,
            view != null && view.ChunkBoundsToggle != null && view.ChunkBoundsToggle.isOn);

    private void ApplyEditorOverlaysToResidents()
    {
        if (board == null) return;
        WorldChunkView[] chunks = board.GetComponentsInChildren<WorldChunkView>(true);
        for (int i = 0; i < chunks.Length; i++) ApplyEditorOverlays(default, chunks[i]);
    }

    private int FindCurrentWorldIndex()
    {
        if (session?.World == null) return 0;
        for (int i = 0; i < worldEntries.Count; i++)
            if (worldEntries[i].WorldId == session.World.WorldId) return i;
        return 0;
    }

    // 地图编辑器要展示已加载内容包的全部启用单位；阵营和控制器从定义带入落点，
    // 不再因为筛选条件而出现“单位列表为空”。
    private static bool IsWorldDeployable(UnitDefinition unit) => unit != null && unit.Enabled &&
        !string.IsNullOrWhiteSpace(unit.UnitId);

    private static int ReadInt(TMPro.TMP_InputField input, int fallback) =>
        input != null && int.TryParse(input.text, out int value) ? value : fallback;

    private static string ReadText(TMPro.TMP_InputField input, string fallback) =>
        input == null || string.IsNullOrWhiteSpace(input.text) ? fallback : input.text.Trim();

    private WorldNoiseSettings CreateNoiseSettings() => new WorldNoiseSettings
    {
        Seed = ReadInt(view?.SeedInput, session != null ? session.World.Seed : 1),
        HeightFrequency = ReadFloat(view?.HeightFrequencyInput, 0.075f),
        DecorDensity = ReadFloat(view?.DecorationDensityInput, 0.16f),
        EnemyDensity = ReadFloat(view?.EnemyDensityInput, 0.14f)
    };

    private static float ReadFloat(TMPro.TMP_InputField input, float fallback) =>
        input != null && float.TryParse(input.text, out float value) ? Mathf.Max(0.001f, value) : fallback;

    private static void SetButtonLabel(Button button, string value)
    {
        TMPro.TMP_Text text = button != null ? button.GetComponentInChildren<TMPro.TMP_Text>(true) : null;
        if (text != null) text.text = value;
    }

    private void OnChunkChanged(ChunkPosition chunk) => pendingChunks.Add((chunk.X, chunk.Y));

    private static void ReloadNeighbor(WorldChunkStreamer streamer, ChunkPosition chunk)
    {
        if (streamer.IsResident(chunk)) streamer.Reload(chunk);
    }

    private void RefreshStatus()
    {
        if (session == null)
        {
            view?.SetStatus(string.Empty);
            return;
        }

        view?.SetStatus(session.Status);
        view?.SetWorldTitle(session.World != null ? session.World.DisplayName : string.Empty);
    }

    private static void Bind(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null || action == null) return;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }
}
