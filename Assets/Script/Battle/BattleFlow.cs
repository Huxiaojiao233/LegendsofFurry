using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using LegendsOfFurry.Content.Runtime;
using LegendsOfFurry.Content.Contracts;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum BattlePhase { PlayerTurn, EnemyTurn, GameOver }

public class BattleFlow : MonoBehaviour
{
    [SerializeField] private BoardClickController boardClickController;
    [SerializeField] private HandCardSystem handCardSystem;
    [SerializeField] private Unit player;
    [SerializeField] private Unit enemy;
    [SerializeField] private Button endRoundButton;
    [SerializeField] private Canvas overlayCanvas;
    [SerializeField, Min(0)] private int cardsDrawnPerRound = 5;
    [SerializeField, Min(0f)] private float enemyStepPause = 0.08f;

    private BattlePhase phase = BattlePhase.PlayerTurn;
    private bool isBusy;
    private GameObject resultOverlay;
    private int round = 1;
    private ClassProfileDefinition activeClassProfile;

    public BattlePhase Phase => phase;
    public int RoundNumber => round;
    public static BattleFlow Instance { get; private set; }
    public static bool CanPlayerAct => Instance == null || (Instance.phase == BattlePhase.PlayerTurn && !Instance.isBusy);

    public void ConfigureCardsPerTurn(int count)
    {
        cardsDrawnPerRound = Mathf.Max(0, count);
    }

    private void Awake()
    {
        Instance = this;
        ResolveReferences();
        ApplyContentConfiguration();
    }

    private IEnumerator Start()
    {
        ResolveReferences();
        Subscribe(player); Subscribe(enemy);
        yield return null;
        bool frozen = BeginPlayerTurnState(false);
        SetEndRoundInteractable(true);
        if (frozen) StartCoroutine(SkipFrozenTurn());
        else BeginPassiveFreeMove(ApplyClassPassive("on_unit_turn_start"));
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Unsubscribe(player); Unsubscribe(enemy);
    }

    public void RequestEndPlayerTurn()
    {
        if (!CanPlayerAct || isBusy ||
            (handCardSystem != null && handCardSystem.HasBlockingChoice) ||
            (boardClickController != null && boardClickController.IsResolvingFreeMove)) return;
        StartCoroutine(EndPlayerTurnRoutine());
    }

    private IEnumerator EndPlayerTurnRoutine()
    {
        isBusy = true;
        SetEndRoundInteractable(false);
        handCardSystem?.CancelTargeting();
        boardClickController?.ClearSelection();
        handCardSystem?.EndTurnDiscardAll();
        player?.State.EndTurn(player);
        ApplyClassPassive("on_unit_turn_end");
        if (phase == BattlePhase.GameOver || player == null || !player.IsAlive) yield break;

        phase = BattlePhase.EnemyTurn;
        yield return EnemyTurnRoutine();
        if (phase == BattlePhase.GameOver) yield break;

        round++;
        phase = BattlePhase.PlayerTurn;
        bool frozen = BeginPlayerTurnState(true);
        isBusy = false;
        SetEndRoundInteractable(true);
        if (frozen) StartCoroutine(SkipFrozenTurn());
        else BeginPassiveFreeMove(ApplyClassPassive("on_unit_turn_start"));
    }

    private bool BeginPlayerTurnState(bool drawCards)
    {
        if (player == null) return false;
        player.State.BeginTurn(player);
        boardClickController?.ResetActionPoints();
        if (drawCards) handCardSystem?.DrawCards(cardsDrawnPerRound, true);

        return player.State.ConsumeFrozenForTurn();
    }

    private IEnumerator SkipFrozenTurn()
    {
        yield return null;
        isBusy = false;
        RequestEndPlayerTurn();
    }

    /// <summary>应用内容包中的职业与基础战斗参数；内容不可用时保留场景默认值。</summary>
    private void ApplyContentConfiguration()
    {
        if (!ContentRuntime.IsLoaded) return;
        string classId = GameSession.SelectedClass.ToString().ToLowerInvariant();
        ContentRuntime.Registry.TryGetClassProfile(classId, out activeClassProfile);
        cardsDrawnPerRound = Mathf.Max(0, ContentRuntime.Registry.GameSettings.DrawPerTurn);
        boardClickController?.ConfigureBaseActionPoints(ContentRuntime.Registry.GameSettings.BaseActionPoints);
        if (activeClassProfile != null && player != null)
        {
            player.ConfigureMaximumHealth(activeClassProfile.InitialHealth);
            player.State.ConfigureMana(activeClassProfile.InitialMana, activeClassProfile.MaximumMana);
        }
    }

    /// <summary>执行当前职业指定生命周期的数据库被动行为。</summary>
    private CardPlayResult ApplyClassPassive(string triggerKey)
    {
        return ContentClassPassiveRuntime.Execute(activeClassProfile, triggerKey, player, boardClickController);
    }

    /// <summary>若职业被动请求免费移动，则进入统一移动交互。</summary>
    private void BeginPassiveFreeMove(CardPlayResult result)
    {
        if (result != null && result.FreeMoveSteps > 0 && player != null && player.IsAlive)
            boardClickController?.BeginFreeMove(player, result.FreeMoveSteps);
    }

    private IEnumerator EnemyTurnRoutine()
    {
        if (enemy == null || player == null || !enemy.IsAlive || !player.IsAlive) yield break;
        enemy.State.BeginTurn(enemy);
        if (enemy.State.ConsumeFrozenForTurn())
        {
            enemy.State.EndTurn(enemy);
            yield break;
        }

        yield return new WaitForSeconds(0.2f);
        if (!IsAdjacent(enemy.Position, player.Position)) yield return MoveEnemyTowardPlayer();
        if (phase != BattlePhase.GameOver && player.IsAlive && enemy.IsAlive && IsAdjacent(enemy.Position, player.Position))
        {
            CombatVfx.PlayClaw(enemy, player);
            player.TakeTypedDamage(enemy.AttackDamage, DamageType.Normal, enemy);
            yield return new WaitForSeconds(0.35f);
        }
        enemy.State.EndTurn(enemy);
    }

    private IEnumerator MoveEnemyTowardPlayer()
    {
        BoardGenerator board = enemy.Board;
        if (board == null) yield break;
        List<Vector2Int> path = new List<Vector2Int>();
        if (!board.TryFindPath(enemy.Position, player.Position, enemy, path) || path.Count <= 1) yield break;
        int steps = Mathf.Max(0, enemy.MoveStepsPerTurn - enemy.State.Get(CombatStatus.Cold));
        if (steps == 0) yield break;
        int moved = 0;
        for (int i = 1; i < path.Count && moved < steps; i++)
        {
            Vector2Int next = path[i];
            if (next == player.Position || !enemy.MoveToAnimated(next.x, next.y)) break;
            moved++;
            while (enemy.IsMoving) yield return null;
            if (enemyStepPause > 0f) yield return new WaitForSeconds(enemyStepPause);
        }
    }

    private void HandleUnitDied(Unit unit)
    {
        if (phase == BattlePhase.GameOver) return;
        phase = BattlePhase.GameOver;
        isBusy = false;
        boardClickController?.ClearSelection();
        SetEndRoundInteractable(false);
        ShowResult(unit != null && unit.Faction == UnitFaction.Enemy);
    }

    /// <summary>创建白底深色文字的战斗结果弹窗，并提供重新战斗与返回按钮。</summary>
    private void ShowResult(bool playerWon)
    {
        if (resultOverlay != null) return;
        overlayCanvas ??= FindScreenCanvas();
        if (overlayCanvas == null) return;
        resultOverlay = new GameObject("P_BattleResult", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        resultOverlay.transform.SetParent(overlayCanvas.transform, false);
        RectTransform root = (RectTransform)resultOverlay.transform;
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = root.offsetMax = Vector2.zero;
        resultOverlay.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.98f);
        TMP_Text title = CreateText("ResultTitle", resultOverlay.transform, 60f);
        title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        title.rectTransform.sizeDelta = new Vector2(700f, 100f);
        title.rectTransform.anchoredPosition = new Vector2(0f, 70f);
        title.text = playerWon ? "鸿叶胜利" : "太糕胜利";
        title.alignment = TextAlignmentOptions.Center;
        Button retry = CreateButton("重新战斗", new Vector2(-130f, -45f));
        retry.onClick.AddListener(() => SceneManager.LoadScene("S_Battle"));
        Button classes = CreateButton("返回职业选择", new Vector2(130f, -45f));
        classes.onClick.AddListener(() => SceneManager.LoadScene("S_ClassSelect"));
    }

    /// <summary>创建战斗结果弹窗中的浅灰按钮和深色居中文字。</summary>
    private Button CreateButton(string label, Vector2 position)
    {
        GameObject obj = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        obj.transform.SetParent(resultOverlay.transform, false);
        RectTransform rect = (RectTransform)obj.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f); rect.sizeDelta = new Vector2(230f, 58f); rect.anchoredPosition = position;
        obj.GetComponent<Image>().color = new Color(0.88f, 0.88f, 0.9f, 1f);
        TMP_Text text = CreateText("Label", obj.transform, 24f);
        text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one; text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
        text.text = label; text.alignment = TextAlignmentOptions.Center;
        return obj.GetComponent<Button>();
    }

    private void ResolveReferences()
    {
        boardClickController ??= FindAnyObjectByType<BoardClickController>();
        handCardSystem ??= FindAnyObjectByType<HandCardSystem>();
        player ??= GameObject.Find("Player")?.GetComponent<Unit>();
        enemy ??= GameObject.Find("Monster")?.GetComponent<Unit>();
        endRoundButton ??= GameObject.Find("B_EndRound")?.GetComponent<Button>();
        overlayCanvas ??= FindScreenCanvas();
    }

    private void SetEndRoundInteractable(bool value) { if (endRoundButton != null) endRoundButton.interactable = value; }
    private void Subscribe(Unit unit) { if (unit != null) unit.Died += HandleUnitDied; }
    private void Unsubscribe(Unit unit) { if (unit != null) unit.Died -= HandleUnitDied; }
    private static bool IsAdjacent(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) == 1;

    /// <summary>创建战斗结果弹窗使用的深色 TextMeshPro 文字。</summary>
    private static TMP_Text CreateText(string name, Transform parent, float size)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        TMP_Text text = obj.GetComponent<TMP_Text>();
        text.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/SourceHanSansSC-Regular SDF") ?? TMP_Settings.defaultFontAsset;
        text.fontSize = size; text.color = new Color(0.08f, 0.08f, 0.1f, 1f); text.raycastTarget = false;
        return text;
    }

    private static Canvas FindScreenCanvas()
    {
        foreach (Canvas canvas in FindObjectsByType<Canvas>())
            if (canvas.renderMode != RenderMode.WorldSpace) return canvas;
        return null;
    }
}
