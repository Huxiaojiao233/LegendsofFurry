using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RuntimeSceneBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureScene(scene.name);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInitialScene()
    {
        EnsureScene(SceneManager.GetActiveScene().name);
    }

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
        player.State.NormalDamageAvoidChance = GameSession.SelectedClass == HeroClass.Ranger ? 0.10f : 0f;
        player.State.ReviveAvailable = GameSession.SelectedClass == HeroClass.Priest;
        player.State.ConfigureMana(GameSession.SelectedClass == HeroClass.Mage ? 3 : 0, 10);

        BattleFlow flow = FindAnyObjectByType<BattleFlow>();
        flow?.ConfigureCardsPerTurn(5);

        SetText("T_Self_Name", "鸿叶");
        SetText("T_Enemy_Name", "太糕");

        Canvas canvas = null;
        foreach (Canvas candidate in FindObjectsByType<Canvas>())
            if (candidate.renderMode != RenderMode.WorldSpace) { canvas = candidate; break; }
        if (canvas != null && canvas.GetComponent<BattleRuntimeHud>() == null)
            canvas.gameObject.AddComponent<BattleRuntimeHud>();

        CardEffectResolver resolver = FindAnyObjectByType<CardEffectResolver>();
        if (resolver == null)
        {
            GameObject host = GameObject.Find("GameManager") ?? gameObject;
            resolver = host.AddComponent<CardEffectResolver>();
        }
        resolver.Bind(FindAnyObjectByType<BoardClickController>(), player, enemy);
    }

    private static void SetText(string name, string value)
    {
        TMP_Text text = GameObject.Find(name)?.GetComponent<TMP_Text>();
        if (text != null) text.text = value;
    }
}
