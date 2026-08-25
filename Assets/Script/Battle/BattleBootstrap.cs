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
        if (sceneName == "S_ClassSelect" && Object.FindAnyObjectByType<ClassSelectionController>() == null)
            new GameObject("ClassSelectionController").AddComponent<ClassSelectionController>();
        if (sceneName == "S_Battle" && Object.FindAnyObjectByType<BattleBootstrap>() == null)
            new GameObject("BattleBootstrap").AddComponent<BattleBootstrap>();
    }
}

[DefaultExecutionOrder(-900)]
public class BattleBootstrap : MonoBehaviour
{
    /// <summary>初始化双方战斗数据、BattleInterface、卡牌解析器和棋盘摄像机控制。</summary>
    private void Awake()
    {
        Unit player = GameObject.Find("Player")?.GetComponent<Unit>();
        Unit enemy = GameObject.Find("Monster")?.GetComponent<Unit>();
        if (player == null || enemy == null)
        {
            Debug.LogError("战斗场景缺少 Player 或 Monster 单位。", this);
            return;
        }

        player.ConfigureCombatant("鸿叶", 30, 3, 2);
        enemy.ConfigureCombatant("太糕", 100, 3, 2);
        player.State.ClearAll();
        enemy.State.ClearAll();
        if (ContentClassPassiveRuntime.TryGetSelectedProfile(out ClassProfileDefinition profile))
        {
            player.ConfigureMaximumHealth(profile.InitialHealth);
            player.State.NormalDamageAvoidChance = profile.GetTraitFloat("normal_damage_avoid_chance");
            player.State.ReviveAvailable = profile.GetTraitBool("revive_available");
            player.State.ConfigureMana(profile.InitialMana, profile.MaximumMana);
        }

        BattleFlow flow = FindAnyObjectByType<BattleFlow>();
        flow?.ConfigureCardsPerTurn(5);

        SetText("T_Self_Name", "鸿叶");
        SetText("T_Enemy_Name", "太糕");

        Canvas canvas = null;
        foreach (Canvas candidate in FindObjectsByType<Canvas>())
            if (candidate.renderMode != RenderMode.WorldSpace) { canvas = candidate; break; }
        if (canvas != null && canvas.GetComponent<BattleRuntimeHud>() == null)
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
}
