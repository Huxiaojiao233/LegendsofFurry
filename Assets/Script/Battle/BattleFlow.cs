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

    /// <summary>运行时编制生成后覆盖场景里可能失效的棋子引用。</summary>
    public void BindCombatants(Unit ally, Unit opponent)
    {
        UnsubscribeRoster();
        player = ally;
        enemy = opponent;
        SubscribeRoster();
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
        SubscribeRoster();
        yield return null;
        bool frozen = BeginPlayerTurnState(false);
        SetEndRoundInteractable(true);
        if (frozen) StartCoroutine(SkipFrozenTurn());
        else BeginPassiveFreeMove(ApplyClassPassive(ContentTriggerKeys.OnUnitTurnStart));
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        UnsubscribeRoster();
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
        EndAllyTurns();
        ApplyClassPassive(ContentTriggerKeys.OnUnitTurnEnd);
        if (phase == BattlePhase.GameOver || !HasLivingPlayerSide()) yield break;

        phase = BattlePhase.EnemyTurn;
        yield return EnemyTurnRoutine();
        if (phase == BattlePhase.GameOver) yield break;

        round++;
        phase = BattlePhase.PlayerTurn;
        bool frozen = BeginPlayerTurnState(true);
        isBusy = false;
        SetEndRoundInteractable(true);
        if (frozen) StartCoroutine(SkipFrozenTurn());
        else BeginPassiveFreeMove(ApplyClassPassive(ContentTriggerKeys.OnUnitTurnStart));
    }

    private bool BeginPlayerTurnState(bool drawCards)
    {
        if (!HasLivingPlayerSide()) return false;
        bool frozen = BeginAllyTurns();
        boardClickController?.ResetActionPoints();
        if (drawCards) handCardSystem?.DrawCards(cardsDrawnPerRound, true);
        return frozen;
    }

    private bool BeginAllyTurns()
    {
        BattleRoster roster = BattleRoster.Instance;
        if (roster != null)
        {
            bool anyCanAct = false;
            for (int i = 0; i < roster.Allies.Count; i++)
            {
                Unit ally = roster.Allies[i];
                if (ally == null || !ally.IsAlive) continue;
                ally.State.BeginTurn(ally);
                if (ally.State.CanTakeTurn(ally)) anyCanAct = true;
            }

            return !anyCanAct;
        }

        if (player == null) return false;
        player.State.BeginTurn(player);
        return !player.State.CanTakeTurn(player);
    }

    private void EndAllyTurns()
    {
        BattleRoster roster = BattleRoster.Instance;
        if (roster != null)
        {
            for (int i = 0; i < roster.Allies.Count; i++)
            {
                Unit ally = roster.Allies[i];
                if (ally == null) continue;
                ally.State.EndTurn(ally);
            }

            return;
        }

        player?.State.EndTurn(player);
    }

    private static bool HasLivingPlayerSide()
    {
        if (BattleRoster.Instance != null) return BattleRoster.Instance.HasLivingAlly();
        return Instance != null && Instance.player != null && Instance.player.IsAlive;
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
        string classId = GameSession.SelectedClassId;
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
        CardPlayResult classResult = ContentClassPassiveRuntime.Execute(activeClassProfile, triggerKey, player, boardClickController);
        CardPlayResult actorResult = ContentActorBehaviorRuntime.Execute(player, triggerKey, boardClickController);
        classResult.FreeMoveSteps = Mathf.Max(classResult.FreeMoveSteps, actorResult.FreeMoveSteps);
        classResult.EndTurn |= actorResult.EndTurn;
        classResult.Success &= actorResult.Success;
        return classResult;
    }

    /// <summary>若职业被动请求免费移动，则进入统一移动交互。</summary>
    private void BeginPassiveFreeMove(CardPlayResult result)
    {
        if (result != null && result.FreeMoveSteps > 0 && player != null && player.IsAlive)
            boardClickController?.BeginFreeMove(player, result.FreeMoveSteps);
    }

    private IEnumerator EnemyTurnRoutine()
    {
        BattleRoster roster = BattleRoster.Instance;
        if (roster != null)
        {
            for (int i = 0; i < roster.Enemies.Count; i++)
            {
                if (phase == BattlePhase.GameOver) yield break;
                yield return RunEnemyActor(roster.Enemies[i]);
            }

            yield break;
        }

        yield return RunEnemyActor(enemy);
    }

    private IEnumerator RunEnemyActor(Unit actor)
    {
        Unit target = BattleRoster.Instance != null
            ? BattleRoster.Instance.FindNearestLivingOpponent(actor)
            : player;
        if (actor == null || target == null || !actor.IsAlive || !target.IsAlive) yield break;
        actor.State.BeginTurn(actor);
        ContentActorBehaviorRuntime.Execute(actor, ContentTriggerKeys.OnUnitTurnStart, null);
        if (!actor.State.CanTakeTurn(actor))
        {
            actor.State.EndTurn(actor);
            ContentActorBehaviorRuntime.Execute(actor, ContentTriggerKeys.OnUnitTurnEnd, null);
            yield break;
        }

        yield return new WaitForSeconds(0.2f);
        if (!IsAdjacent(actor.Position, target.Position)) yield return MoveEnemyToward(actor, target);
        if (phase != BattlePhase.GameOver && target.IsAlive && actor.IsAlive && IsAdjacent(actor.Position, target.Position))
        {
            CombatVfx.PlayClaw(actor, target);
            target.TakeTypedDamage(actor.AttackDamage, DamageType.Normal, actor);
            yield return new WaitForSeconds(0.35f);
        }
        actor.State.EndTurn(actor);
        ContentActorBehaviorRuntime.Execute(actor, ContentTriggerKeys.OnUnitTurnEnd, null);
    }

    private IEnumerator MoveEnemyToward(Unit actor, Unit target)
    {
        BoardGenerator board = actor.Board;
        if (board == null) yield break;
        List<Vector2Int> path = new List<Vector2Int>();
        if (!board.TryFindPath(actor.Position, target.Position, actor, path) || path.Count <= 1) yield break;
        ContentRuleQuery moveQuery = ContentRuleQueryRuntime.Evaluate(new ContentRuleQuery(
            ContentRuleQueryKeys.MoveSteps, actor, target, actor.MoveStepsPerTurn));
        int steps = Mathf.Max(0, moveQuery.Value);
        if (steps == 0) yield break;
        int moved = 0;
        for (int i = 1; i < path.Count && moved < steps; i++)
        {
            Vector2Int next = path[i];
            if (next == target.Position || !actor.MoveToAnimated(next.x, next.y)) break;
            moved++;
            while (actor.IsMoving) yield return null;
            if (enemyStepPause > 0f) yield return new WaitForSeconds(enemyStepPause);
        }
    }

    private void HandleUnitDied(Unit unit)
    {
        if (phase == BattlePhase.GameOver) return;
        BattleRoster roster = BattleRoster.Instance;
        if (roster != null)
        {
            if (roster.HasLivingAlly() && roster.HasLivingEnemy()) return;
            phase = BattlePhase.GameOver;
            isBusy = false;
            boardClickController?.ClearSelection();
            SetEndRoundInteractable(false);
            ShowResult(roster.HasLivingAlly());
            return;
        }

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
        Unit winner = playerWon ? player : enemy;
        if (BattleRoster.Instance != null)
        {
            winner = playerWon ? BattleRoster.Instance.PrimaryAlly : BattleRoster.Instance.PrimaryEnemy;
        }
        title.text = $"{(winner != null ? winner.DisplayName : string.Empty)}胜利";
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
        player = BattleUnits.PrimaryAlly ?? player;
        enemy = BattleUnits.PrimaryEnemy ?? enemy;
        endRoundButton ??= GameObject.Find("B_EndRound")?.GetComponent<Button>();
        overlayCanvas ??= FindScreenCanvas();
    }

    private void SubscribeRoster()
    {
        BattleRoster roster = BattleRoster.Instance;
        if (roster == null)
        {
            Subscribe(player);
            Subscribe(enemy);
            return;
        }

        for (int i = 0; i < roster.Allies.Count; i++) Subscribe(roster.Allies[i]);
        for (int i = 0; i < roster.Enemies.Count; i++) Subscribe(roster.Enemies[i]);
    }

    private void UnsubscribeRoster()
    {
        BattleRoster roster = BattleRoster.Instance;
        if (roster == null)
        {
            Unsubscribe(player);
            Unsubscribe(enemy);
            return;
        }

        for (int i = 0; i < roster.Allies.Count; i++) Unsubscribe(roster.Allies[i]);
        for (int i = 0; i < roster.Enemies.Count; i++) Unsubscribe(roster.Enemies[i]);
    }

    private void SetEndRoundInteractable(bool value) { if (endRoundButton != null) endRoundButton.interactable = value; }
    private void Subscribe(Unit unit)
    {
        if (unit == null) return;
        unit.Died -= HandleUnitDied;
        unit.Died += HandleUnitDied;
    }
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
