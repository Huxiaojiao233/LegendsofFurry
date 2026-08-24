using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
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

    public BattlePhase Phase => phase;
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
    }

    private IEnumerator Start()
    {
        ResolveReferences();
        Subscribe(player); Subscribe(enemy);
        yield return null;
        bool frozen = BeginPlayerTurnState(false);
        SetEndRoundInteractable(true);
        if (frozen) StartCoroutine(SkipFrozenTurn());
        else TriggerAssassinFreeMove();
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
        if (GameSession.SelectedClass == HeroClass.Warrior && player != null && player.IsAlive) player.AddArmor(3);
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
        else TriggerAssassinFreeMove();
    }

    private bool BeginPlayerTurnState(bool drawCards)
    {
        if (player == null) return false;
        player.State.BeginTurn(player);
        switch (GameSession.SelectedClass)
        {
            case HeroClass.Warrior: player.State.Add(CombatStatus.Sharp); break;
            case HeroClass.Mage: player.State.TryGainMana(1); break;
            case HeroClass.Assassin: player.State.Add(CombatStatus.Quick); break;
        }
        boardClickController?.ResetActionPoints();
        if (drawCards) handCardSystem?.DrawCards(cardsDrawnPerRound, true);

        TMP_Text roundText = GameObject.Find("T_Round")?.GetComponent<TMP_Text>();
        if (roundText != null) roundText.text = $"第{round}回合";

        return player.State.ConsumeFrozenForTurn();
    }

    private IEnumerator SkipFrozenTurn()
    {
        yield return null;
        isBusy = false;
        RequestEndPlayerTurn();
    }

    private void TriggerAssassinFreeMove()
    {
        if (GameSession.SelectedClass == HeroClass.Assassin && player != null && player.IsAlive)
            boardClickController?.BeginFreeMove(player, 1);
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

    private void ShowResult(bool playerWon)
    {
        if (resultOverlay != null) return;
        overlayCanvas ??= FindScreenCanvas();
        if (overlayCanvas == null) return;
        resultOverlay = new GameObject("P_BattleResult", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        resultOverlay.transform.SetParent(overlayCanvas.transform, false);
        RectTransform root = (RectTransform)resultOverlay.transform;
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = root.offsetMax = Vector2.zero;
        resultOverlay.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);
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

    private Button CreateButton(string label, Vector2 position)
    {
        GameObject obj = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        obj.transform.SetParent(resultOverlay.transform, false);
        RectTransform rect = (RectTransform)obj.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f); rect.sizeDelta = new Vector2(230f, 58f); rect.anchoredPosition = position;
        obj.GetComponent<Image>().color = new Color(0.18f, 0.24f, 0.34f, 1f);
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

    private static TMP_Text CreateText(string name, Transform parent, float size)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        TMP_Text text = obj.GetComponent<TMP_Text>();
        text.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/SourceHanSansSC-Regular SDF") ?? TMP_Settings.defaultFontAsset;
        text.fontSize = size; text.color = Color.white; text.raycastTarget = false;
        return text;
    }

    private static Canvas FindScreenCanvas()
    {
        foreach (Canvas canvas in FindObjectsByType<Canvas>())
            if (canvas.renderMode != RenderMode.WorldSpace) return canvas;
        return null;
    }
}
