using System.Collections;
using System.Collections.Generic;
using System.Text;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 在 S_Battle 里把大地图和战斗合成同一套格子。探索默认正交 Size 6、走完居中；
/// 全览 28 看整张图；开战时邻关坍塌，打完再拼回并点亮相邻关。
/// </summary>
public sealed class WorldPlaySession : MonoBehaviour
{
    private static readonly Color ExploredTint = new Color(0.82f, 0.84f, 0.86f, 1f);
    private static readonly Color CurrentTint = Color.white;
    private static readonly Color FogTint = new Color(0.18f, 0.2f, 0.24f, 1f);
    private static readonly Color CombatHiddenTint = new Color(0.07f, 0.08f, 0.1f, 1f);
    private static readonly Color PathDotColor = new Color(0.25f, 0.72f, 1f, 0.95f);
    private static readonly Color CurrentOutlineColor = new Color(1f, 1f, 1f, 1f);

    public const float PlaySize = 6f;
    public const float OverviewSize = 28f;

    public static WorldPlaySession Instance { get; private set; }

    private WorldDefinition world;
    private BoardGenerator board;
    private BoardCameraController cameraController;
    private readonly List<Vector2Int> walkPath = new List<Vector2Int>();
    private readonly List<GameObject> pathDots = new List<GameObject>();
    private readonly Dictionary<string, TextMeshPro> stageIcons = new Dictionary<string, TextMeshPro>();
    private readonly Dictionary<string, List<BoardCell>> cellsByStage = new Dictionary<string, List<BoardCell>>();
    private readonly List<BoardCell> limitCells = new List<BoardCell>();
    private GameObject cardControlRoot;
    private Vector3 cardControlRestScale = Vector3.one;
    private bool cardControlScaleCaptured;
    private GameObject endRoundRoot;
    private Vector3 endRoundRestScale = Vector3.one;
    private bool endRoundScaleCaptured;
    private GameObject overviewButton;
    private GameObject restoreButton;
    private Transform iconRoot;
    private Transform pathRoot;
    private Transform linkRoot;
    private GameObject currentFence;
    private Canvas hud;
    private TMP_Text statusText;
    private WorldCloudCover cloudCover;
    private bool walking;
    private bool transitioning;
    private string pendingMessage = string.Empty;
    private Camera mainCamera;
    private Material lineMaterial;
    private Material wallMaterial;
    private Material glowMaterial;

    public bool IsExploring { get; private set; } = true;
    public bool IsCombat => !IsExploring;
    public Vector2Int AllySpawnCell { get; private set; }
    public bool ShouldStartCombat { get; private set; }

    public void Bind(BoardGenerator worldBoard)
    {
        Instance = this;
        board = worldBoard;
        if (!WorldCatalog.TryGet(RunSession.Current.worldId, out world))
            world = WorldCatalog.Default;
        if (world == null || board == null)
        {
            Debug.LogError("冒险棋盘无法绑定世界数据。", this);
            return;
        }

        RefreshSpawnCells();
        ShouldStartCombat = NeedsCombat(CurrentStage);
        IsExploring = !ShouldStartCombat;
        board.SetMovementFilter(CanWalkBoardCell);
        CacheCells();
        RevealAdjacent(CurrentStage);
        EnsureCloudCover();
        BuildStageIcons();
        BuildStageLinks();
        ApplyFog();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (lineMaterial != null) Destroy(lineMaterial);
        if (wallMaterial != null) Destroy(wallMaterial);
        if (glowMaterial != null) Destroy(glowMaterial);
    }

    private IEnumerator Start()
    {
        mainCamera = Camera.main;
        cameraController = mainCamera != null ? mainCamera.GetComponent<BoardCameraController>() : null;
        cameraController?.ConfigureDistanceRange(5f, 30f);
        EnsureHud();
        SetCombatUiVisible(IsCombat);
        cameraController?.RecalculateFocusLimits();
        CenterOnPlayer(PlaySize, true);
        if (IsExploring) AutoResolveNonCombat(CurrentStage);
        RefreshHud();
        if (!IsCombat) yield break;
        FitCombatCamera();
        yield return CollapseForeignStages();
    }

    private void Update()
    {
        if (!IsExploring || walking || transitioning || !RunSession.HasActive) return;
        UpdateHoverStatus();
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        if (mainCamera == null) return;
        Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit)) return;
        WorldCloudPatch cloud = WorldCloudCover.FromHit(hit);
        if (cloud != null)
        {
            TryWalkIntoCloudPatch(cloud);
            return;
        }

        BoardCell cell = hit.collider.GetComponentInParent<BoardCell>();
        if (cell == null) return;
        TryWalkTo(cell.Coordinate);
    }

    private void TryWalkIntoCloudPatch(WorldCloudPatch cloud)
    {
        if (cloud == null || !cloud.HasStage || world == null) return;
        if (!WorldCatalog.TryGetStage(world, cloud.StageId, out StageDefinition stage) || stage == null)
            return;
        TryWalkTo(WorldLayout.StageCenterCell(stage, world));
    }

    private void LateUpdate()
    {
        if (mainCamera == null) return;
        foreach (TextMeshPro icon in stageIcons.Values)
        {
            if (icon == null) continue;
            icon.transform.rotation = Quaternion.LookRotation(icon.transform.position - mainCamera.transform.position);
        }
    }

    public void FinishCombatVictory()
    {
        if (transitioning) return;
        StartCoroutine(ExitCombatRoutine());
    }

    private IEnumerator ExitCombatRoutine()
    {
        transitioning = true;
        BattleRoster roster = BattleRoster.Instance;
        roster?.ClearEnemies();
        FindAnyObjectByType<HandCardSystem>()?.RecycleAllIntoDrawPile();
        BattleFlow.Instance?.EnterExplorationPhase();
        IsExploring = true;
        ShouldStartCombat = false;
        RevealAdjacent(CurrentStage);
        board.SetMovementFilter(CanWalkBoardCell);
        yield return RestoreForeignStages();
        ApplyFog();
        SetCombatUiVisible(false);
        cameraController?.RecalculateFocusLimits();
        CenterOnPlayer(PlaySize);
        pendingMessage = "战斗结束，相邻关卡已经点亮。";
        RefreshHud();
        transitioning = false;
    }

    public void BeginCombatOnCurrentStage()
    {
        StageDefinition stage = CurrentStage;
        if (!NeedsCombat(stage) || transitioning) return;
        StartCoroutine(EnterCombatRoutine());
    }

    private IEnumerator EnterCombatRoutine()
    {
        transitioning = true;
        IsExploring = false;
        walking = false;
        ClearPathDots();
        RefreshSpawnCells();
        board.SetMovementFilter(CanWalkBoardCell);
        ApplyFog();
        SetCombatUiVisible(true);
        FitCombatCamera();
        CenterOnPlayer(PlaySize);
        yield return CollapseForeignStages();

        BattleRoster roster = BattleRoster.Instance;
        roster?.SpawnDeployedEnemies(CurrentStage);
        CardEffectResolver resolver = FindAnyObjectByType<CardEffectResolver>();
        resolver?.Bind(FindAnyObjectByType<BoardClickController>(), roster?.PrimaryAlly, roster?.PrimaryEnemy);
        BattleFlow.Instance?.BeginWorldCombat();
        SetSceneEnemyLabel();
        pendingMessage = "进入战斗。";
        RefreshHud();
        transitioning = false;
    }

    private void TryWalkTo(Vector2Int destination)
    {
        Unit player = BattleRoster.Instance != null ? BattleRoster.Instance.PrimaryAlly : null;
        if (player == null || player.IsMoving) return;
        if (WorldLayout.TryGetStage(world, destination.x, destination.y, out StageDefinition targetStage) &&
            RunSession.IsLocked(targetStage) && !RunSession.HasKey(targetStage.RequiredKeyId))
        {
            pendingMessage = "没有钥匙，无法进入这座关卡。";
            RefreshHud();
            return;
        }

        if (!board.TryFindPath(player.Position, destination, player, walkPath) || walkPath.Count <= 1)
        {
            pendingMessage = "走不到那里。";
            RefreshHud();
            return;
        }

        ShowPathDots(walkPath);
        StartCoroutine(WalkPathRoutine(player));
    }

    private IEnumerator WalkPathRoutine(Unit player)
    {
        walking = true;
        for (int i = 1; i < walkPath.Count; i++)
        {
            Vector2Int next = walkPath[i];
            if (!player.MoveToAnimated(next.x, next.y)) break;
            while (player.IsMoving) yield return null;
            if (!TryEnterCell(next)) continue;
            ClearPathDots();
            walking = false;
            if (!IsCombat) CenterOnPlayer(PlaySize);
            yield break;
        }

        ClearPathDots();
        walking = false;
        CenterOnPlayer(PlaySize);
        RefreshHud();
    }

    /// <summary>踩上另一关的第一格才算进入；开战则立刻停下走路。</summary>
    private bool TryEnterCell(Vector2Int cell)
    {
        if (!WorldLayout.TryGetStage(world, cell.x, cell.y, out StageDefinition stage)) return false;
        if (stage.StageId == RunSession.Current.currentStageId) return false;
        if (RunSession.IsLocked(stage))
        {
            if (!RunSession.TryUnlockWithKey(stage))
            {
                pendingMessage = "关卡仍被锁住。";
                return true;
            }

            pendingMessage = "钥匙转动，封禁散开了。";
        }

        RunSession.MoveTo(stage.StageId);
        RevealAdjacent(stage);
        RefreshSpawnCells();
        ApplyFog();
        RefreshStageLinks();
        AutoResolveNonCombat(stage);
        if (!NeedsCombat(stage)) return false;
        BeginCombatOnCurrentStage();
        return true;
    }

    private void AutoResolveNonCombat(StageDefinition stage)
    {
        if (stage == null || RunSession.IsCompleted(stage.StageId)) return;
        if (stage.StageType == ContentStageTypeKeys.Rest || stage.StageType == ContentStageTypeKeys.Start)
        {
            RunSession.HealToFull();
            Unit ally = BattleRoster.Instance != null ? BattleRoster.Instance.PrimaryAlly : null;
            if (ally != null) ally.Heal(ally.MaxHealth);
            if (stage.StageType == ContentStageTypeKeys.Rest)
            {
                CompleteStage(stage);
                pendingMessage = "在营地休整，生命已恢复。";
            }
        }
    }

    private void OnReward()
    {
        StageDefinition stage = CurrentStage;
        if (stage == null || stage.StageType != ContentStageTypeKeys.Reward) return;
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
        FindAnyObjectByType<HandCardSystem>()?.GainCard(cardId);
        CompleteStage(stage);
        pendingMessage = $"获得卡牌：{cardName}";
        RefreshHud();
    }

    private void OnShop()
    {
        StageDefinition stage = CurrentStage;
        if (stage == null || stage.StageType != ContentStageTypeKeys.Shop) return;
        RunSession.HealToFull();
        Unit ally = BattleRoster.Instance != null ? BattleRoster.Instance.PrimaryAlly : null;
        if (ally != null) ally.Heal(ally.MaxHealth);
        RunSession.Current.gold += 5;
        if (!RunSession.IsCompleted(stage.StageId)) CompleteStage(stage);
        else RunSession.Persist();
        pendingMessage = "商人帮你包扎伤口，并塞来几枚金币。";
        RefreshHud();
    }

    private void OnUseKey()
    {
        StageDefinition current = CurrentStage;
        StageDefinition boss = FindBoss();
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

        if (current == null || (current.StageId != boss.StageId && !WorldLayout.IsOrthogonalNeighbor(current, boss)))
        {
            pendingMessage = "走到魔王关旁边再使用钥匙。";
            RefreshHud();
            return;
        }

        RunSession.TryUnlockWithKey(boss);
        pendingMessage = "钥匙转动，魔王关卡的封禁散开了。";
        ApplyFog();
        RefreshHud();
    }

    private void CompleteStage(StageDefinition stage)
    {
        Unit ally = BattleRoster.Instance != null ? BattleRoster.Instance.PrimaryAlly : null;
        HandCardSystem hand = FindAnyObjectByType<HandCardSystem>();
        List<string> deck = hand != null ? hand.ExportOwnedCardIds() : new List<string>(RunSession.Current.deckCardIds);
        int health = ally != null ? ally.CurrentHealth : RunSession.Current.health;
        int maxHealth = ally != null ? ally.MaxHealth : RunSession.Current.maxHealth;
        RunSession.CompleteCurrentStage(health, maxHealth, deck, stage);
    }

    private bool CanWalkBoardCell(int x, int z)
    {
        if (!WorldLayout.TryGetStage(world, x, z, out StageDefinition stage)) return false;
        if (IsCombat) return WorldLayout.ContainsCell(CurrentStage, world, x, z);
        if (RunSession.IsLocked(stage) && !RunSession.HasKey(stage.RequiredKeyId)) return false;
        if (RunSession.IsExplored(stage.StageId)) return true;
        return WorldLayout.IsOrthogonalNeighbor(CurrentStage, stage);
    }

    private static bool NeedsCombat(StageDefinition stage)
    {
        if (stage == null || RunSession.IsCompleted(stage.StageId)) return false;
        if (stage.StageType != ContentStageTypeKeys.Battle && stage.StageType != ContentStageTypeKeys.Boss)
            return false;
        return !RunSession.IsLocked(stage) && stage.UnitPlacements.Exists(item => item != null && item.Enabled);
    }

    private void RefreshSpawnCells()
    {
        StageDefinition stage = CurrentStage;
        if (stage == null || world == null) return;
        AllySpawnCell = WorldLayout.StageCenterCell(stage, world);
    }

    private void ApplyFog()
    {
        if (board == null) return;
        StageDefinition current = CurrentStage;
        BoardCell[] cells = board.GetComponentsInChildren<BoardCell>(true);
        for (int i = 0; i < cells.Length; i++)
        {
            BoardCell cell = cells[i];
            if (!WorldLayout.TryGetStage(world, cell.Coordinate.x, cell.Coordinate.y, out StageDefinition stage))
            {
                cell.SetTerrainTint(FogTint);
                continue;
            }

            bool explored = RunSession.IsExplored(stage.StageId);
            if (IsCombat && (current == null || stage.StageId != current.StageId))
                cell.SetTerrainTint(CombatHiddenTint);
            else if (current != null && stage.StageId == current.StageId)
                cell.SetTerrainTint(CurrentTint);
            else if (explored)
                cell.SetTerrainTint(ExploredTint);
            else
                cell.SetTerrainTint(FogTint);

            if (stageIcons.TryGetValue(stage.StageId, out TextMeshPro icon) && icon != null)
            {
                bool cloudy = WorldCloudRules.HasCloudOver(current, stage, explored, IsCombat);
                Color color = IconColor(stage);
                if (!explored) color = Color.Lerp(color, cloudy ? Color.white : Color.black, cloudy ? 0.08f : 0.65f);
                bool currentStage = current != null && stage.StageId == current.StageId;
                if (IsCombat && !currentStage)
                    color.a = 0.15f;
                else
                    color.a = 1f;
                icon.color = color;
                icon.transform.localScale = Vector3.one * (currentStage ? 1.35f : 1f);
                Vector3 p = icon.transform.position;
                p.y = cloudy && cloudCover != null
                    ? cloudCover.DeckY + 0.22f
                    : cell.transform.position.y + 1.8f;
                icon.transform.position = p;
            }
        }

        cloudCover?.Refresh(world, current, IsCombat);
    }

    private void EnsureCloudCover()
    {
        if (cloudCover == null)
            cloudCover = gameObject.GetComponent<WorldCloudCover>() ?? gameObject.AddComponent<WorldCloudCover>();
        cloudCover.Bind(world, board);
    }

    private void BuildStageIcons()
    {
        if (iconRoot != null) Destroy(iconRoot.gameObject);
        stageIcons.Clear();
        iconRoot = new GameObject("StageIcons").transform;
        iconRoot.SetParent(transform, false);
        for (int i = 0; i < world.Stages.Count; i++)
        {
            StageDefinition stage = world.Stages[i];
            if (stage == null || !stage.Enabled) continue;
            Vector2Int center = WorldLayout.StageCenterCell(stage, world);
            if (!board.TryGetCell(center.x, center.y, out BoardCell cell)) continue;
            GameObject iconObject = new GameObject("Icon_" + stage.StageId);
            iconObject.transform.SetParent(iconRoot, false);
            iconObject.transform.position = cell.transform.position + Vector3.up * 1.8f;
            TextMeshPro text = iconObject.AddComponent<TextMeshPro>();
            text.text = IconGlyph(stage);
            text.fontSize = 8f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = IconColor(stage);
            text.raycastTarget = false;
            stageIcons[stage.StageId] = text;
        }

        RefreshStageLinks();
    }

    private void CacheCells()
    {
        cellsByStage.Clear();
        if (board == null) return;
        BoardCell[] cells = board.GetComponentsInChildren<BoardCell>(true);
        for (int i = 0; i < cells.Length; i++)
        {
            BoardCell cell = cells[i];
            if (cell == null || string.IsNullOrEmpty(cell.StageId)) continue;
            if (!cellsByStage.TryGetValue(cell.StageId, out List<BoardCell> list))
            {
                list = new List<BoardCell>();
                cellsByStage[cell.StageId] = list;
            }

            list.Add(cell);
        }
    }

    private void BuildStageLinks()
    {
        if (linkRoot != null) Destroy(linkRoot.gameObject);
        currentFence = null;
        linkRoot = new GameObject("StageLinks").transform;
        linkRoot.SetParent(transform, false);
        EnsureFenceMaterials();
        RefreshStageLinks();
    }

    private void RefreshStageLinks()
    {
        if (currentFence != null) Destroy(currentFence);
        currentFence = null;
        if (linkRoot == null) return;
        linkRoot.gameObject.SetActive(!IsCombat);
        if (IsCombat) return;
        StageDefinition current = CurrentStage;
        if (current == null || !TryGetStageBounds(current, out Bounds bounds)) return;
        currentFence = BuildStageFence(current.StageId, bounds);
    }

    /// <summary>透明墙绕关卡转 45° 包住；顶边细线比最高地形再高一格，光沿墙向下只轻轻收一点。</summary>
    private GameObject BuildStageFence(string stageId, Bounds bounds)
    {
        float cellHeight = OneTerrainCellHeight();
        float bottom = bounds.min.y;
        float top = bounds.max.y + cellHeight;
        float glowBottom = Mathf.Max(bottom, top - cellHeight * 0.2f);
        Vector3[] floor = StageFenceFloor(bounds);

        GameObject fence = new GameObject("Fence_" + stageId);
        fence.transform.SetParent(linkRoot, false);

        MeshFilter wallFilter = fence.AddComponent<MeshFilter>();
        MeshRenderer wallRenderer = fence.AddComponent<MeshRenderer>();
        wallFilter.sharedMesh = BuildWallMesh(floor, bottom, top, new Color(1f, 1f, 1f, 0.08f), new Color(1f, 1f, 1f, 0.16f));
        wallRenderer.sharedMaterial = wallMaterial;
        wallRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        wallRenderer.receiveShadows = false;

        GameObject glow = new GameObject("Glow");
        glow.transform.SetParent(fence.transform, false);
        MeshFilter glowFilter = glow.AddComponent<MeshFilter>();
        MeshRenderer glowRenderer = glow.AddComponent<MeshRenderer>();
        glowFilter.sharedMesh = BuildWallMesh(
            floor,
            glowBottom,
            top,
            new Color(1f, 1f, 1f, 0.42f),
            new Color(1f, 1f, 1f, 0.7f),
            0.1f);
        glowRenderer.sharedMaterial = glowMaterial;
        glowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        glowRenderer.receiveShadows = false;

        LineRenderer rim = CreateLine("Rim", 5, 0.045f, CurrentOutlineColor);
        rim.transform.SetParent(fence.transform, false);
        rim.SetPositions(new[]
        {
            new Vector3(floor[0].x, top, floor[0].z),
            new Vector3(floor[1].x, top, floor[1].z),
            new Vector3(floor[2].x, top, floor[2].z),
            new Vector3(floor[3].x, top, floor[3].z),
            new Vector3(floor[0].x, top, floor[0].z)
        });
        return fence;
    }

    /// <summary>把轴对齐包围盒绕中心转 45°，并放大到仍包住原来的关卡范围。</summary>
    private static Vector3[] StageFenceFloor(Bounds bounds)
    {
        const float cos = 0.70710678f;
        const float sin = 0.70710678f;
        float extent = (bounds.extents.x + bounds.extents.z) / Mathf.Sqrt(2f) * 0.51f;
        Vector3 center = bounds.center;
        Vector3 Corner(float x, float z)
        {
            return new Vector3(center.x + x * cos - z * sin, 0f, center.z + x * sin + z * cos);
        }

        return new[]
        {
            Corner(-extent, -extent),
            Corner(extent, -extent),
            Corner(extent, extent),
            Corner(-extent, extent)
        };
    }

    private static Mesh BuildWallMesh(
        Vector3[] floor,
        float bottom,
        float top,
        Color bottomColor,
        Color topColor,
        float bottomInset = 0f)
    {
        Vector3 centroid = (floor[0] + floor[1] + floor[2] + floor[3]) * 0.25f;
        Vector3[] vertices = new Vector3[16];
        Color[] colors = new Color[16];
        int[] triangles = new int[48];
        int vertex = 0;
        int triangle = 0;
        for (int i = 0; i < 4; i++)
        {
            Vector3 a = floor[i];
            Vector3 b = floor[(i + 1) % 4];
            Vector3 bl = InsetPoint(a, centroid, bottomInset, bottom);
            Vector3 br = InsetPoint(b, centroid, bottomInset, bottom);
            Vector3 tr = new Vector3(b.x, top, b.z);
            Vector3 tl = new Vector3(a.x, top, a.z);
            vertices[vertex] = bl;
            vertices[vertex + 1] = br;
            vertices[vertex + 2] = tr;
            vertices[vertex + 3] = tl;
            colors[vertex] = bottomColor;
            colors[vertex + 1] = bottomColor;
            colors[vertex + 2] = topColor;
            colors[vertex + 3] = topColor;
            triangles[triangle] = vertex;
            triangles[triangle + 1] = vertex + 2;
            triangles[triangle + 2] = vertex + 1;
            triangles[triangle + 3] = vertex;
            triangles[triangle + 4] = vertex + 3;
            triangles[triangle + 5] = vertex + 2;
            triangles[triangle + 6] = vertex;
            triangles[triangle + 7] = vertex + 1;
            triangles[triangle + 8] = vertex + 2;
            triangles[triangle + 9] = vertex;
            triangles[triangle + 10] = vertex + 2;
            triangles[triangle + 11] = vertex + 3;
            vertex += 4;
            triangle += 12;
        }

        Mesh mesh = new Mesh { name = "StageFenceWall" };
        mesh.vertices = vertices;
        mesh.colors = colors;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Vector3 InsetPoint(Vector3 floorPoint, Vector3 centroid, float inset, float y)
    {
        Vector3 offset = new Vector3(floorPoint.x - centroid.x, 0f, floorPoint.z - centroid.z);
        float length = offset.magnitude;
        if (inset > 0f && length > 0.001f) offset *= 1f - Mathf.Min(0.35f, inset / length);
        return new Vector3(centroid.x + offset.x, y, centroid.z + offset.z);
    }

    private LineRenderer CreateLine(string objectName, int points, float width, Color color)
    {
        GameObject obj = new GameObject(objectName);
        obj.transform.SetParent(linkRoot, false);
        LineRenderer line = obj.AddComponent<LineRenderer>();
        line.material = new Material(lineMaterial);
        line.positionCount = points;
        line.startWidth = line.endWidth = width;
        line.useWorldSpace = true;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.numCapVertices = 2;
        line.numCornerVertices = 2;
        line.alignment = LineAlignment.View;
        line.startColor = line.endColor = color;
        line.material.color = color;
        return line;
    }

    private void EnsureFenceMaterials()
    {
        if (lineMaterial == null)
        {
            Shader lineShader = Shader.Find("Sprites/Default") ??
                                Shader.Find("Universal Render Pipeline/Unlit") ??
                                Shader.Find("Hidden/Internal-Colored");
            lineMaterial = lineShader != null ? new Material(lineShader) : new Material(Shader.Find("Standard"));
        }

        if (wallMaterial == null) wallMaterial = CreateTransparentMaterial("StageFenceWall", new Color(1f, 1f, 1f, 0.12f));
        if (glowMaterial == null) glowMaterial = CreateTransparentMaterial("StageFenceGlow", Color.white);
    }

    private static Material CreateTransparentMaterial(string materialName, Color color)
    {
        Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
        Material material = shader != null ? new Material(shader) : new Material(Shader.Find("Standard"));
        material.name = materialName;
        material.color = color;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.renderQueue = 3000;
        material.SetInt("_Cull", 0);
        return material;
    }

    private float OneTerrainCellHeight()
    {
        if (board != null && board.HeightStep > 0.01f) return board.HeightStep;
        return WorldTerrain.StepY;
    }

    private bool TryGetStageBounds(StageDefinition stage, out Bounds bounds)
    {
        bounds = default;
        if (stage == null || !cellsByStage.TryGetValue(stage.StageId, out List<BoardCell> cells) || cells == null ||
            cells.Count == 0)
            return false;

        bool started = false;
        for (int i = 0; i < cells.Count; i++)
        {
            BoardCell cell = cells[i];
            if (cell == null) continue;
            Renderer[] renderers = cell.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer renderer = renderers[r];
                if (renderer == null || renderer is LineRenderer || !renderer.enabled) continue;
                if (!started)
                {
                    bounds = renderer.bounds;
                    started = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
        }

        return started;
    }

    private void CenterOnPlayer(float size, bool instant = false)
    {
        if (cameraController == null) return;
        Vector3 focus = PlayerFocus();
        cameraController.FocusOn(focus, size, instant: instant);
    }

    private void FitCombatCamera()
    {
        if (cameraController == null) return;
        CollectLimitCells();
        cameraController.FitFocusLimits(limitCells, 1.5f);
    }

    private void OnOverview()
    {
        if (IsCombat || cameraController == null) return;
        cameraController.RecalculateFocusLimits();
        cameraController.FocusOn(WorldFocus(), OverviewSize);
    }

    private void OnRestore()
    {
        if (IsCombat || cameraController == null) return;
        cameraController.RecalculateFocusLimits();
        CenterOnPlayer(PlaySize);
    }

    private Vector3 PlayerFocus()
    {
        Unit player = BattleRoster.Instance != null ? BattleRoster.Instance.PrimaryAlly : null;
        if (player != null) return player.transform.position;
        return CurrentStage != null ? StageWorldCenter(CurrentStage) : board.transform.position;
    }

    private Vector3 WorldFocus()
    {
        if (board == null) return Vector3.zero;
        BoardCell[] cells = board.GetComponentsInChildren<BoardCell>(true);
        if (cells.Length == 0) return board.transform.position;
        Vector3 sum = Vector3.zero;
        int count = 0;
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] == null) continue;
            sum += cells[i].transform.position;
            count++;
        }

        return count == 0 ? board.transform.position : sum / count;
    }

    private void RevealAdjacent(StageDefinition stage)
    {
        if (stage == null) return;
        List<StageDefinition> neighbors = WorldLayout.OrthogonalNeighbors(world, stage);
        for (int i = 0; i < neighbors.Count; i++)
            RunSession.Reveal(neighbors[i].StageId);
    }

    private IEnumerator CollapseForeignStages()
    {
        StageDefinition keep = CurrentStage;
        if (keep == null) yield break;
        RefreshStageLinks();
        if (linkRoot != null) linkRoot.gameObject.SetActive(false);
        SetForeignIconsVisible(keep.StageId, false);
        yield return AnimateForeignStages(keep.StageId, true);
        SetForeignStagesActive(keep.StageId, false);
    }

    private IEnumerator RestoreForeignStages()
    {
        StageDefinition keep = CurrentStage;
        string keepId = keep != null ? keep.StageId : string.Empty;
        SetForeignStagesActive(keepId, true);
        yield return AnimateForeignStages(keepId, false);
        SetForeignIconsVisible(keepId, true);
        if (linkRoot != null) linkRoot.gameObject.SetActive(true);
        RefreshStageLinks();
    }

    private IEnumerator AnimateForeignStages(string keepStageId, bool collapse)
    {
        List<BoardCell> foreign = CollectForeignCells(keepStageId);
        if (foreign.Count == 0) yield break;
        Vector3 epicenter = PlayerFocus();
        float[] delays = new float[foreign.Count];
        float maxDelay = 0f;
        for (int i = 0; i < foreign.Count; i++)
        {
            delays[i] = Vector3.Distance(RestWorldPosition(foreign[i]), epicenter) * 0.012f;
            if (delays[i] > maxDelay) maxDelay = delays[i];
        }

        const float duration = 0.38f;
        float elapsed = 0f;
        float total = duration + maxDelay;
        while (elapsed < total)
        {
            elapsed += Time.deltaTime;
            for (int i = 0; i < foreign.Count; i++)
            {
                BoardCell cell = foreign[i];
                if (cell == null) continue;
                float t = Mathf.Clamp01((elapsed - delays[i]) / duration);
                t = t * t * (3f - 2f * t);
                float fall = collapse ? t : 1f - t;
                cell.transform.localPosition = cell.RestLocalPosition + Vector3.down * (12f * fall);
                cell.transform.localScale = Vector3.Lerp(cell.RestLocalScale, cell.RestLocalScale * 0.12f, fall);
            }

            yield return null;
        }

        for (int i = 0; i < foreign.Count; i++)
        {
            BoardCell cell = foreign[i];
            if (cell == null) continue;
            if (collapse)
            {
                cell.transform.localPosition = cell.RestLocalPosition + Vector3.down * 12f;
                cell.transform.localScale = cell.RestLocalScale * 0.12f;
            }
            else
            {
                cell.transform.localPosition = cell.RestLocalPosition;
                cell.transform.localScale = cell.RestLocalScale;
            }
        }
    }

    private List<BoardCell> CollectForeignCells(string keepStageId)
    {
        List<BoardCell> foreign = new List<BoardCell>();
        foreach (KeyValuePair<string, List<BoardCell>> pair in cellsByStage)
        {
            if (pair.Key == keepStageId) continue;
            foreign.AddRange(pair.Value);
        }

        return foreign;
    }

    private static Vector3 RestWorldPosition(BoardCell cell)
    {
        return cell.transform.parent != null
            ? cell.transform.parent.TransformPoint(cell.RestLocalPosition)
            : cell.RestLocalPosition;
    }

    private void SetForeignStagesActive(string keepStageId, bool active)
    {
        foreach (KeyValuePair<string, List<BoardCell>> pair in cellsByStage)
        {
            if (pair.Key == keepStageId) continue;
            for (int i = 0; i < pair.Value.Count; i++)
            {
                BoardCell cell = pair.Value[i];
                if (cell != null) cell.gameObject.SetActive(active);
            }
        }
    }

    private void SetForeignIconsVisible(string keepStageId, bool visible)
    {
        foreach (KeyValuePair<string, TextMeshPro> pair in stageIcons)
        {
            if (pair.Value == null) continue;
            pair.Value.gameObject.SetActive(visible || pair.Key == keepStageId);
        }
    }

    private void CollectLimitCells()
    {
        limitCells.Clear();
        BoardCell[] cells = board.GetComponentsInChildren<BoardCell>(true);
        StageDefinition current = CurrentStage;
        HashSet<string> visible = new HashSet<string>();
        if (current != null) visible.Add(current.StageId);
        if (!IsCombat)
        {
            List<StageDefinition> neighbors = WorldLayout.OrthogonalNeighbors(world, current);
            for (int i = 0; i < neighbors.Count; i++) visible.Add(neighbors[i].StageId);
            for (int i = 0; i < world.Stages.Count; i++)
            {
                if (RunSession.IsExplored(world.Stages[i].StageId))
                    visible.Add(world.Stages[i].StageId);
            }
        }

        for (int i = 0; i < cells.Length; i++)
        {
            BoardCell cell = cells[i];
            if (!WorldLayout.TryGetStage(world, cell.Coordinate.x, cell.Coordinate.y, out StageDefinition stage))
                continue;
            if (visible.Contains(stage.StageId)) limitCells.Add(cell);
        }
    }

    private Vector3 StageWorldCenter(StageDefinition stage)
    {
        Vector2Int center = WorldLayout.StageCenterCell(stage, world);
        if (board.TryGetCell(center.x, center.y, out BoardCell cell))
            return cell.transform.position;
        return board.transform.position;
    }

    private void ShowPathDots(List<Vector2Int> path)
    {
        ClearPathDots();
        if (pathRoot == null)
        {
            pathRoot = new GameObject("PathDots").transform;
            pathRoot.SetParent(transform, false);
        }

        for (int i = 1; i < path.Count; i++)
        {
            if (!board.TryGetCell(path[i].x, path[i].y, out BoardCell cell)) continue;
            GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = "PathDot";
            dot.transform.SetParent(pathRoot, false);
            dot.transform.position = cell.transform.position + Vector3.up * 0.35f;
            dot.transform.localScale = Vector3.one * 0.22f;
            Collider dotCollider = dot.GetComponent<Collider>();
            if (dotCollider != null) Destroy(dotCollider);
            Renderer dotRenderer = dot.GetComponent<Renderer>();
            if (dotRenderer != null) dotRenderer.material.color = PathDotColor;
            pathDots.Add(dot);
        }
    }

    private void ClearPathDots()
    {
        for (int i = 0; i < pathDots.Count; i++)
            if (pathDots[i] != null) Destroy(pathDots[i]);
        pathDots.Clear();
    }

    private void SetCombatUiVisible(bool visible)
    {
        if (cardControlRoot == null) cardControlRoot = GameObject.Find("C_CardControl");
        if (endRoundRoot == null) endRoundRoot = GameObject.Find("B_EndRound");
        SetAuthoredUiVisible(cardControlRoot, visible, ref cardControlRestScale, ref cardControlScaleCaptured);
        SetAuthoredUiVisible(endRoundRoot, visible, ref endRoundRestScale, ref endRoundScaleCaptured);
        if (overviewButton != null) overviewButton.SetActive(!visible);
        if (restoreButton != null) restoreButton.SetActive(!visible);
        SetSceneEnemyLabel();
    }

    /// <summary>
    /// 隐藏场景里已有的 UI 时不要 SetActive/AddComponent。Play 时 Inspector 若正选着该物体，
    /// 关掉或往上加组件会让 Image/Button 编辑器拿到空 SerializedObject。
    /// </summary>
    private static void SetAuthoredUiVisible(GameObject root, bool visible, ref Vector3 restScale, ref bool captured)
    {
        if (root == null) return;
        CanvasGroup group = root.GetComponent<CanvasGroup>();
        if (group != null)
        {
            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
            return;
        }

        if (!captured)
        {
            restScale = root.transform.localScale.sqrMagnitude < 0.0001f ? Vector3.one : root.transform.localScale;
            captured = true;
        }

        root.transform.localScale = visible ? restScale : Vector3.zero;
    }

    private void SetSceneEnemyLabel()
    {
        TMP_Text text = GameObject.Find("T_Enemy_Name")?.GetComponent<TMP_Text>();
        if (text == null) return;
        if (IsCombat && BattleRoster.Instance != null && BattleRoster.Instance.PrimaryEnemy != null)
            text.text = BattleRoster.Instance.PrimaryEnemy.DisplayName;
        else if (CurrentStage != null)
            text.text = CurrentStage.DisplayName;
    }

    private void UpdateHoverStatus()
    {
        if (statusText == null || mainCamera == null || Mouse.current == null) return;
        Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit)) return;
        WorldCloudPatch cloud = WorldCloudCover.FromHit(hit);
        if (cloud != null)
        {
            RefreshCloudHoverHud(cloud);
            return;
        }

        BoardCell cell = hit.collider.GetComponentInParent<BoardCell>();
        if (cell == null || !WorldLayout.TryGetStage(world, cell.Coordinate.x, cell.Coordinate.y, out StageDefinition stage))
            return;
        RefreshHud(stage);
    }

    private void RefreshCloudHoverHud(WorldCloudPatch cloud)
    {
        if (statusText == null || !RunSession.HasActive) return;
        if (cloud == null || !cloud.HasStage ||
            !WorldCatalog.TryGetStage(world, cloud.StageId, out StageDefinition stage) || stage == null)
        {
            statusText.text = "云海之外。";
            return;
        }

        StringBuilder builder = new StringBuilder();
        bool explored = RunSession.IsExplored(stage.StageId);
        builder.Append(explored ? "迷雾外缘 · " : "未解锁 · ");
        builder.Append(string.IsNullOrWhiteSpace(stage.DisplayName) ? stage.StageId : stage.DisplayName);
        builder.Append(" · ").Append(TypeLabel(stage));
        builder.Append("    (").Append(stage.GridX).Append(", ").Append(stage.GridY).Append(")");
        if (RunSession.IsLocked(stage))
        {
            builder.Append("    [锁定]");
            if (!string.IsNullOrWhiteSpace(stage.RequiredKeyId))
                builder.Append(" 需要 ").Append(stage.RequiredKeyId);
        }

        if (!string.IsNullOrEmpty(pendingMessage)) builder.Append("    ").Append(pendingMessage);
        statusText.text = builder.ToString();
    }

    private void EnsureHud()
    {
        if (hud != null) return;
        GameObject canvasObject = new GameObject("WorldPlayHud", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        hud = canvasObject.GetComponent<Canvas>();
        hud.renderMode = RenderMode.ScreenSpaceOverlay;
        hud.sortingOrder = 20;
        statusText = CreateLabel("Status", canvasObject.transform, new Vector2(0.5f, 1f), new Vector2(0f, -18f),
            new Vector2(1100f, 72f), 22f);
        overviewButton = CreateCornerButton("全览", new Vector2(-110f, -56f), OnOverview);
        restoreButton = CreateCornerButton("恢复", new Vector2(-110f, -116f), OnRestore);
        CreateButton("领取奖励", new Vector2(-220f, 36f), OnReward);
        CreateButton("商店", new Vector2(0f, 36f), OnShop);
        CreateButton("使用钥匙", new Vector2(220f, 36f), OnUseKey);
    }

    private void RefreshHud(StageDefinition hovered = null)
    {
        if (statusText == null || !RunSession.HasActive) return;
        StageDefinition stage = hovered ?? CurrentStage;
        StringBuilder builder = new StringBuilder();
        builder.Append(stage != null ? stage.DisplayName : RunSession.Current.currentStageId);
        builder.Append(" · ").Append(TypeLabel(stage));
        builder.Append("    生命 ").Append(RunSession.Current.health).Append("/").Append(RunSession.Current.maxHealth);
        builder.Append("    金币 ").Append(RunSession.Current.gold);
        if (RunSession.HasKey(RunSession.BossKeyId)) builder.Append("    钥匙已入手");
        if (RunSession.IsLocked(stage)) builder.Append("    [锁定]");
        if (!string.IsNullOrEmpty(pendingMessage)) builder.Append("    ").Append(pendingMessage);
        statusText.text = builder.ToString();
    }

    private StageDefinition CurrentStage
    {
        get
        {
            if (world == null || !RunSession.HasActive) return null;
            WorldCatalog.TryGetStage(world, RunSession.Current.currentStageId, out StageDefinition stage);
            return stage;
        }
    }

    private StageDefinition FindBoss()
    {
        for (int i = 0; i < world.Stages.Count; i++)
            if (world.Stages[i].StageType == ContentStageTypeKeys.Boss) return world.Stages[i];
        return null;
    }

    private static string IconGlyph(StageDefinition stage)
    {
        return stage.StageType switch
        {
            ContentStageTypeKeys.Battle => "战",
            ContentStageTypeKeys.Rest => "憩",
            ContentStageTypeKeys.Shop => "店",
            ContentStageTypeKeys.Reward => "赏",
            ContentStageTypeKeys.Boss => "王",
            ContentStageTypeKeys.Start => "营",
            _ => "·"
        };
    }

    private static Color IconColor(StageDefinition stage)
    {
        return stage.StageType switch
        {
            ContentStageTypeKeys.Start => new Color(0.45f, 0.85f, 0.4f),
            ContentStageTypeKeys.Battle => new Color(0.92f, 0.42f, 0.32f),
            ContentStageTypeKeys.Reward => new Color(0.95f, 0.82f, 0.28f),
            ContentStageTypeKeys.Shop => new Color(0.4f, 0.65f, 0.95f),
            ContentStageTypeKeys.Rest => new Color(0.4f, 0.85f, 0.8f),
            ContentStageTypeKeys.Boss => new Color(0.85f, 0.25f, 0.55f),
            _ => Color.white
        };
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

    private GameObject CreateCornerButton(string label, Vector2 anchored, UnityEngine.Events.UnityAction click)
    {
        GameObject obj = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        obj.transform.SetParent(hud.transform, false);
        RectTransform rect = (RectTransform)obj.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.sizeDelta = new Vector2(160f, 48f);
        rect.anchoredPosition = anchored;
        obj.GetComponent<Image>().color = new Color(0.16f, 0.18f, 0.22f, 0.9f);
        TMP_Text text = CreateLabel("Label", obj.transform, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, 22f);
        text.text = label;
        obj.GetComponent<Button>().onClick.AddListener(click);
        return obj;
    }

    private Button CreateButton(string label, Vector2 anchored, UnityEngine.Events.UnityAction click)
    {
        GameObject obj = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        obj.transform.SetParent(hud.transform, false);
        RectTransform rect = (RectTransform)obj.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(180f, 48f);
        rect.anchoredPosition = anchored;
        obj.GetComponent<Image>().color = new Color(0.16f, 0.18f, 0.22f, 0.9f);
        TMP_Text text = CreateLabel("Label", obj.transform, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, 20f);
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
        rect.sizeDelta = size == Vector2.zero ? new Vector2(180f, 48f) : size;
        rect.anchoredPosition = anchored;
        TextMeshProUGUI text = obj.GetComponent<TextMeshProUGUI>();
        text.fontSize = font;
        text.color = Color.white;
        text.alignment = size.x > 400f ? TextAlignmentOptions.Top : TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
    }
}
