using System.Collections.Generic;
using System.Text;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 旧的独立大地图场景入口。正式玩法已合并进 S_Battle 的 WorldPlaySession。
/// </summary>
public sealed class WorldMapController : MonoBehaviour
{
    private const float CellSize = 1f;
    private const float WorldOverviewSize = 22f;
    private const float StageViewSize = 7.5f;

    private WorldDefinition world;
    private readonly Dictionary<string, Transform> chunks = new Dictionary<string, Transform>();
    private Transform worldRoot;
    private Camera worldCamera;
    private Canvas hud;
    private TMP_Text statusText;
    private bool overview;
    private string pendingMessage = string.Empty;

    private void Start()
    {
        if (!RunSession.HasActive && !RunSession.TryLoad())
        {
            SceneManager.LoadScene("S_ClassSelect");
            return;
        }

        if (!WorldCatalog.TryGet(RunSession.Current.worldId, out world))
            world = WorldCatalog.Default;
        if (world == null)
        {
            Debug.LogError("没有可用的世界地图。");
            return;
        }

        HideMenuUi();
        EnsureCamera();
        EnsureHud();
        worldRoot = new GameObject("WorldTerrain").transform;
        RebuildExploredChunks();
        FrameCamera();
        RefreshHud();
    }

    private void Update()
    {
        if (RunSession.HasActive && Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame)
        {
            RunSession.GrantKey(RunSession.BossKeyId);
            pendingMessage = "测试：已获得魔王钥匙（K）。";
            RefreshHud();
        }

        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        Ray ray = worldCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, 200f)) return;
        WorldMapTile tile = hit.collider.GetComponent<WorldMapTile>();
        if (tile == null) return;
        HandleTile(tile);
    }

    private void HandleTile(WorldMapTile tile)
    {
        if (!WorldCatalog.TryGetStage(world, tile.StageId, out StageDefinition stage)) return;
        if (tile.IsExit && !string.IsNullOrEmpty(tile.NeighborStageId))
        {
            TryTravel(tile.NeighborStageId);
            return;
        }

        if (stage.StageId != RunSession.Current.currentStageId)
        {
            if (RunSession.IsExplored(stage.StageId)) TryTravel(stage.StageId);
            return;
        }

        if (stage.StageType == ContentStageTypeKeys.Battle || stage.StageType == ContentStageTypeKeys.Boss)
            TryEnterBattle(stage);
    }

    private void TryTravel(string stageId)
    {
        if (!WorldCatalog.TryGetStage(world, stageId, out StageDefinition stage)) return;
        if (!IsAdjacentToCurrent(stage))
        {
            pendingMessage = "只能走向相邻的关卡格子。";
            RefreshHud();
            return;
        }

        RunSession.MoveTo(stage.StageId);
        RebuildExploredChunks();
        FrameCamera();
        RefreshHud();
        AutoResolveNonCombat(stage);
    }

    private bool IsAdjacentToCurrent(StageDefinition target)
    {
        if (!WorldCatalog.TryGetStage(world, RunSession.Current.currentStageId, out StageDefinition currentStage))
            return false;
        return Mathf.Abs(currentStage.GridX - target.GridX) + Mathf.Abs(currentStage.GridY - target.GridY) == 1;
    }

    private void AutoResolveNonCombat(StageDefinition stage)
    {
        if (RunSession.IsCompleted(stage.StageId)) return;
        if (stage.StageType == ContentStageTypeKeys.Rest || stage.StageType == ContentStageTypeKeys.Start)
        {
            RunSession.HealToFull();
            if (stage.StageType == ContentStageTypeKeys.Rest)
                RunSession.CompleteCurrentStage(RunSession.Current.health, RunSession.Current.maxHealth,
                    RunSession.Current.deckCardIds, stage);
            pendingMessage = "在营地休整，生命已恢复。";
        }
        RefreshHud();
    }

    private void TryEnterBattle(StageDefinition stage)
    {
        if (RunSession.IsCompleted(stage.StageId))
        {
            pendingMessage = "这里已经打过了。";
            RefreshHud();
            return;
        }

        if (RunSession.IsLocked(stage))
        {
            pendingMessage = "没有钥匙，无法挑战魔王。";
            RefreshHud();
            return;
        }

        SceneManager.LoadScene("S_Battle");
    }

    private void OnReward()
    {
        if (!TryCurrentStage(out StageDefinition stage) || stage.StageType != ContentStageTypeKeys.Reward) return;
        if (RunSession.IsCompleted(stage.StageId))
        {
            pendingMessage = "奖励已经拿走了。";
            RefreshHud();
            return;
        }

        if (!TryPickRewardCard(stage, out string cardId, out string cardName))
        {
            pendingMessage = "没有可领取的奖励牌。";
            RefreshHud();
            return;
        }

        RunSession.AddCard(cardId);
        RunSession.CompleteCurrentStage(RunSession.Current.health, RunSession.Current.maxHealth,
            RunSession.Current.deckCardIds, stage);
        pendingMessage = $"获得卡牌：{cardName}";
        RefreshHud();
    }

    private void OnShop()
    {
        if (!TryCurrentStage(out StageDefinition stage) || stage.StageType != ContentStageTypeKeys.Shop) return;
        RunSession.HealToFull();
        RunSession.Current.gold += 5;
        if (!RunSession.IsCompleted(stage.StageId))
            RunSession.CompleteCurrentStage(RunSession.Current.health, RunSession.Current.maxHealth,
                RunSession.Current.deckCardIds, stage);
        else
            RunSession.Persist();
        pendingMessage = "商人帮你包扎伤口，并塞来几枚金币。";
        RefreshHud();
    }

    private void OnUseKey()
    {
        if (!TryCurrentStage(out StageDefinition currentStage)) return;
        StageDefinition boss = null;
        for (int i = 0; i < world.Stages.Count; i++)
            if (world.Stages[i].StageType == ContentStageTypeKeys.Boss)
                boss = world.Stages[i];
        if (boss == null)
        {
            pendingMessage = "这张地图没有魔王关。";
            RefreshHud();
            return;
        }

        if (!RunSession.HasKey(boss.RequiredKeyId))
        {
            pendingMessage = "还没有魔王钥匙。";
            RefreshHud();
            return;
        }

        if (!IsAdjacentToCurrent(boss) && currentStage.StageId != boss.StageId)
        {
            pendingMessage = "走到魔王关旁边再使用钥匙。";
            RefreshHud();
            return;
        }

        RunSession.TryUnlockWithKey(boss);
        pendingMessage = "钥匙转动，魔王关卡的封禁散开了。";
        RefreshHud();
    }

    private void ToggleOverview()
    {
        overview = !overview;
        FrameCamera();
        RefreshHud();
    }

    private void RebuildExploredChunks()
    {
        foreach (Transform child in chunks.Values)
            if (child != null) Destroy(child.gameObject);
        chunks.Clear();
        foreach (StageDefinition stage in world.Stages)
        {
            if (!stage.Enabled || !RunSession.IsExplored(stage.StageId)) continue;
            chunks[stage.StageId] = BuildChunk(stage);
        }
    }

    private Transform BuildChunk(StageDefinition stage)
    {
        Transform root = new GameObject("Stage_" + stage.StageId).transform;
        root.SetParent(worldRoot, false);
        int tw = world.TerrainWidth;
        int th = world.TerrainHeight;
        bool current = stage.StageId == RunSession.Current.currentStageId;
        for (int y = 0; y < th; y++)
        {
            for (int x = 0; x < tw; x++)
            {
                int height = stage.HeightAt(x, y, tw, th);
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"{stage.StageId}_{x}_{y}";
                cube.transform.SetParent(root, false);
                float worldX = (stage.GridX * tw + x) * CellSize;
                float worldZ = (stage.GridY * th + y) * CellSize;
                float worldY = height * WorldTerrain.StepY;
                cube.transform.position = new Vector3(worldX, worldY, worldZ);
                cube.transform.localScale = new Vector3(0.92f, Mathf.Max(0.2f, WorldTerrain.StepY), 0.92f);
                cube.GetComponent<MeshRenderer>().material.color = TileColor(stage, height, current);
                WorldMapTile tile = cube.AddComponent<WorldMapTile>();
                tile.StageId = stage.StageId;
                tile.LocalX = x;
                tile.LocalY = y;
                AssignExit(stage, x, y, tile);
            }
        }

        return root;
    }

    private void AssignExit(StageDefinition stage, int x, int y, WorldMapTile tile)
    {
        int tw = world.TerrainWidth;
        int th = world.TerrainHeight;
        int nx = stage.GridX;
        int ny = stage.GridY;
        if (x == 0) nx -= 1;
        else if (x == tw - 1) nx += 1;
        else if (y == 0) ny -= 1;
        else if (y == th - 1) ny += 1;
        else return;
        if (nx == stage.GridX && ny == stage.GridY) return;
        if (!WorldCatalog.TryGetStageAt(world, nx, ny, out StageDefinition neighbor)) return;
        tile.IsExit = true;
        tile.NeighborStageId = neighbor.StageId;
    }

    private static Color TileColor(StageDefinition stage, int height, bool current)
    {
        Color baseColor = stage.StageType switch
        {
            ContentStageTypeKeys.Start => new Color(0.45f, 0.72f, 0.42f),
            ContentStageTypeKeys.Battle => new Color(0.72f, 0.38f, 0.32f),
            ContentStageTypeKeys.Reward => new Color(0.82f, 0.7f, 0.28f),
            ContentStageTypeKeys.Shop => new Color(0.35f, 0.55f, 0.78f),
            ContentStageTypeKeys.Rest => new Color(0.4f, 0.7f, 0.7f),
            ContentStageTypeKeys.Boss => new Color(0.45f, 0.22f, 0.55f),
            _ => new Color(0.5f, 0.5f, 0.48f)
        };
        float shade = 0.75f + (height + 1) * 0.08f;
        Color color = baseColor * shade;
        if (current) color = Color.Lerp(color, Color.white, 0.18f);
        color.a = 1f;
        return color;
    }

    private void FrameCamera()
    {
        if (!WorldCatalog.TryGetStage(world, RunSession.Current.currentStageId, out StageDefinition stage))
            return;
        Vector3 center = StageCenter(stage);
        worldCamera.orthographic = true;
        worldCamera.orthographicSize = overview ? WorldOverviewSize : StageViewSize;
        worldCamera.transform.position = center + new Vector3(0f, 18f, -14f);
        worldCamera.transform.LookAt(center);
    }

    private Vector3 StageCenter(StageDefinition stage)
    {
        float x = (stage.GridX * world.TerrainWidth + (world.TerrainWidth - 1) * 0.5f) * CellSize;
        float z = (stage.GridY * world.TerrainHeight + (world.TerrainHeight - 1) * 0.5f) * CellSize;
        return new Vector3(x, 0f, z);
    }

    private void EnsureCamera()
    {
        worldCamera = Camera.main;
        if (worldCamera == null)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            worldCamera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
        }

        if (FindAnyObjectByType<Light>() == null)
        {
            GameObject lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }
    }

    private void HideMenuUi()
    {
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude);
        for (int i = 0; i < canvases.Length; i++)
            canvases[i].gameObject.SetActive(false);
    }

    private void EnsureHud()
    {
        GameObject canvasObject = new GameObject("WorldHud", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        hud = canvasObject.GetComponent<Canvas>();
        hud.renderMode = RenderMode.ScreenSpaceOverlay;
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem));
            eventSystem.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        statusText = CreateLabel("Status", canvasObject.transform, new Vector2(0.5f, 1f), new Vector2(0f, -24f),
            new Vector2(1200f, 96f), 26f);
        CreateButton("开战 / 进入", new Vector2(-360f, 36f), TryPrimaryAction);
        CreateButton("领取奖励", new Vector2(-120f, 36f), OnReward);
        CreateButton("商店", new Vector2(120f, 36f), OnShop);
        CreateButton("使用钥匙", new Vector2(360f, 36f), OnUseKey);
        CreateButton("大地图", new Vector2(-360f, 110f), ToggleOverview);
        CreateButton("返回菜单", new Vector2(360f, 110f), () => SceneManager.LoadScene("S_Menu"));
    }

    private void TryPrimaryAction()
    {
        if (!TryCurrentStage(out StageDefinition stage)) return;
        if (stage.StageType == ContentStageTypeKeys.Battle || stage.StageType == ContentStageTypeKeys.Boss)
            TryEnterBattle(stage);
        else if (stage.StageType == ContentStageTypeKeys.Reward) OnReward();
        else if (stage.StageType == ContentStageTypeKeys.Shop) OnShop();
        else
        {
            pendingMessage = "点亮色块边缘可以走进相邻关卡。";
            RefreshHud();
        }
    }

    private void RefreshHud()
    {
        if (statusText == null || !RunSession.HasActive) return;
        TryCurrentStage(out StageDefinition stage);
        StringBuilder builder = new StringBuilder();
        builder.Append(stage != null ? stage.DisplayName : RunSession.Current.currentStageId);
        builder.Append(" · ").Append(TypeLabel(stage));
        builder.Append("    生命 ").Append(RunSession.Current.health).Append("/").Append(RunSession.Current.maxHealth);
        builder.Append("    金币 ").Append(RunSession.Current.gold);
        builder.Append("    牌 ").Append(RunSession.Current.deckCardIds.Length);
        if (RunSession.HasKey(RunSession.BossKeyId)) builder.Append("    钥匙已入手");
        else builder.Append("    K 测试钥匙");
        if (RunSession.IsLocked(stage)) builder.Append("    [锁定]");
        if (!string.IsNullOrEmpty(pendingMessage)) builder.Append("\n").Append(pendingMessage);
        statusText.text = builder.ToString();
    }

    private static string TypeLabel(StageDefinition stage)
    {
        if (stage == null) return "";
        return stage.StageType switch
        {
            ContentStageTypeKeys.Start => "初始",
            ContentStageTypeKeys.Battle => "战斗",
            ContentStageTypeKeys.Reward => "奖励",
            ContentStageTypeKeys.Shop => "商店",
            ContentStageTypeKeys.Rest => "休息",
            ContentStageTypeKeys.Boss => "魔王",
            _ => stage.StageType
        };
    }

    private bool TryCurrentStage(out StageDefinition stage) =>
        WorldCatalog.TryGetStage(world, RunSession.Current.currentStageId, out stage);

    private static bool TryPickRewardCard(StageDefinition stage, out string cardId, out string cardName)
    {
        cardId = string.Empty;
        cardName = string.Empty;
        if (!ContentRuntime.IsLoaded) return false;
        IReadOnlyList<CardDefinition> pool = !string.IsNullOrWhiteSpace(stage.RewardPoolId)
            ? ContentRuntime.Registry.GetCardsInPool(stage.RewardPoolId)
            : new List<CardDefinition>(ContentRuntime.Registry.Cards);
        if (pool.Count == 0) return false;
        CardDefinition card = pool[Random.Range(0, pool.Count)];
        cardId = card.CardId;
        cardName = card.DisplayName;
        return true;
    }

    private Button CreateButton(string label, Vector2 anchored, UnityEngine.Events.UnityAction click)
    {
        GameObject obj = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        obj.transform.SetParent(hud.transform, false);
        RectTransform rect = (RectTransform)obj.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(200f, 52f);
        rect.anchoredPosition = anchored;
        obj.GetComponent<Image>().color = new Color(0.16f, 0.18f, 0.22f, 0.92f);
        TMP_Text text = CreateLabel("Label", obj.transform, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, 22f);
        text.text = label;
        Button button = obj.GetComponent<Button>();
        button.onClick.AddListener(click);
        return button;
    }

    private static TMP_Text CreateLabel(string name, Transform parent, Vector2 anchor, Vector2 anchored, Vector2 size,
        float font)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)obj.transform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.sizeDelta = size == Vector2.zero ? new Vector2(200f, 52f) : size;
        rect.anchoredPosition = anchored;
        TextMeshProUGUI text = obj.GetComponent<TextMeshProUGUI>();
        text.fontSize = font;
        text.color = Color.white;
        text.alignment = size.x > 400f ? TextAlignmentOptions.Top : TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
    }
}
