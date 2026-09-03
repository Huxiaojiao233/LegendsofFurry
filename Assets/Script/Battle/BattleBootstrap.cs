using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;

public static class RuntimeSceneBootstrap
{
    /// <summary>在首个场景加载前登记统一场景初始化回调。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    /// <summary>每次场景加载完成后补齐该场景需要的运行时入口组件。</summary>
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureScene(scene.name);
    }

    /// <summary>兼容直接从当前场景进入播放时的首次初始化。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInitialScene()
    {
        EnsureScene(SceneManager.GetActiveScene().name);
    }

    /// <summary>按场景名称创建且只创建一个职业选择或战斗初始化器。</summary>
    private static void EnsureScene(string sceneName)
    {
        if (!ContentRuntime.IsLoaded)
        {
            ContentLoadFailureNotice.Ensure();
            return;
        }

        if (sceneName == "S_ClassSelect" && Object.FindAnyObjectByType<ClassSelectionController>() == null)
            new GameObject("ClassSelectionController").AddComponent<ClassSelectionController>();
        if (sceneName == "S_World" && Object.FindAnyObjectByType<WorldMapController>() == null)
            new GameObject("WorldMapController").AddComponent<WorldMapController>();
        if (sceneName == "S_Battle" && Object.FindAnyObjectByType<BattleBootstrap>() == null)
            new GameObject("BattleBootstrap").AddComponent<BattleBootstrap>();
    }
}

[DefaultExecutionOrder(-50)]
public class BattleBootstrap : MonoBehaviour
{
    /// <summary>生成编制棋子、绑定职业与卡牌解析器，并补齐战斗 HUD / 摄像机。</summary>
    private void Awake()
    {
        if (!ContentRuntime.IsLoaded)
        {
            Debug.LogError($"战斗内容未加载：{ContentRuntime.LoadError}", this);
            return;
        }

        BattleRoster roster = GetComponent<BattleRoster>() ?? gameObject.AddComponent<BattleRoster>();
        WorldPlaySession worldSession = null;
        if (RunSession.HasActive)
        {
            BoardGenerator board = FindAnyObjectByType<BoardGenerator>();
            worldSession = GetComponent<WorldPlaySession>() ?? gameObject.AddComponent<WorldPlaySession>();
            worldSession.Bind(board);
            roster.SpawnRunParty(worldSession.AllySpawnCell, worldSession.ShouldStartCombat, worldSession.EnemySpawnCell);
        }
        else
        {
            roster.SpawnEncounter();
        }

        Unit player = roster.PrimaryAlly;
        Unit enemy = roster.PrimaryEnemy;
        if (player == null)
        {
            Debug.LogError("战斗编制未能生成己方角色。", this);
            return;
        }

        if (worldSession == null && enemy == null)
        {
            Debug.LogError("战斗编制未能生成至少一名己方和一名敌人。", this);
            return;
        }

        if (worldSession != null && worldSession.ShouldStartCombat && enemy == null)
        {
            Debug.LogError("当前关卡需要战斗，但没有生成敌人。", this);
            return;
        }

        player.State.ClearAll();
        for (int i = 0; i < roster.Enemies.Count; i++)
        {
            roster.Enemies[i]?.State.ClearAll();
        }

        if (ContentClassPassiveRuntime.TryGetSelectedProfile(out ClassProfileDefinition profile))
        {
            player.ConfigureMaximumHealth(profile.InitialHealth);
            player.State.ConfigureMana(profile.InitialMana, profile.MaximumMana);
            (player.GetComponent<RuntimeEquipmentLoadout>() ?? player.gameObject.AddComponent<RuntimeEquipmentLoadout>())
                .Configure(profile);
        }

        if (RunSession.HasActive)
            player.Revive(Mathf.Max(1, RunSession.Current.health));

        BattleFlow flow = FindAnyObjectByType<BattleFlow>();
        flow?.ConfigureCardsPerTurn(ContentRuntime.Registry.GameSettings.DrawPerTurn);
        flow?.BindCombatants(player, enemy);

        SetText("T_Self_Name", player.DisplayName);
        SetText("T_Enemy_Name", enemy != null ? FormatEnemyNames(roster) : (worldSession != null ? "探索" : string.Empty));

        Canvas canvas = null;
        foreach (Canvas candidate in FindObjectsByType<Canvas>())
            if (candidate.renderMode != RenderMode.WorldSpace) { canvas = candidate; break; }
        if (canvas != null && FindAnyObjectByType<BattleRuntimeHud>() == null)
            canvas.gameObject.AddComponent<BattleRuntimeHud>();

        Camera mainCamera = Camera.main;
        if (mainCamera != null && mainCamera.GetComponent<BoardCameraController>() == null)
            mainCamera.gameObject.AddComponent<BoardCameraController>();

        CardEffectResolver resolver = FindAnyObjectByType<CardEffectResolver>();
        if (resolver == null)
        {
            GameObject host = GameObject.Find("GameManager") ?? gameObject;
            resolver = host.AddComponent<CardEffectResolver>();
        }
        resolver.Bind(FindAnyObjectByType<BoardClickController>(), player, enemy);
    }

    /// <summary>按名称更新场景文本；节点不存在时保持兼容并跳过。</summary>
    private static void SetText(string name, string value)
    {
        TMP_Text text = GameObject.Find(name)?.GetComponent<TMP_Text>();
        if (text != null) text.text = value;
    }

    private static string FormatEnemyNames(BattleRoster roster)
    {
        if (roster.Enemies.Count == 1) return roster.PrimaryEnemy.DisplayName;
        var names = new System.Text.StringBuilder();
        for (int i = 0; i < roster.Enemies.Count; i++)
        {
            if (roster.Enemies[i] == null) continue;
            if (names.Length > 0) names.Append(" / ");
            names.Append(roster.Enemies[i].DisplayName);
        }
        return names.ToString();
    }
}
