using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using LegendsOfFurry.Content.Runtime;
using LegendsOfFurry.Content.Contracts;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum BattlePhase { Exploration, PlayerTurn, EnemyTurn, GameOver }

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
        if (WorldPlaySession.Instance != null && WorldPlaySession.Instance.IsExploring)
            phase = BattlePhase.Exploration;
        ResolveReferences();
        ApplyContentConfiguration();
    }

    private IEnumerator Start()
    {
        ResolveReferences();
        SubscribeRoster();
        yield return null;
        if (WorldPlaySession.Instance != null && WorldPlaySession.Instance.IsExploring)
        {
            phase = BattlePhase.Exploration;
            SetEndRoundInteractable(false);
            yield break;
        }

        yield return BeginCombatRoutine(false);
    }

    /// <summary>从探索走进战斗关后开始回合循环。</summary>
    public void BeginWorldCombat()
    {
        if (resultOverlay != null)
        {
            Destroy(resultOverlay);
            resultOverlay = null;
        }

        ResolveReferences();
        UnsubscribeRoster();
        player = BattleUnits.PrimaryAlly ?? player;
        enemy = BattleRoster.Instance != null ? BattleRoster.Instance.PrimaryEnemy : enemy;
        SubscribeRoster();
        round = 1;
        isBusy = false;
        StartCoroutine(BeginCombatRoutine(true));
    }

    /// <summary>打赢后不切场景，回到同一棋盘上的探索镜头。</summary>
    public void EnterExplorationPhase()
    {
        if (resultOverlay != null)
        {
            Destroy(resultOverlay);
            resultOverlay = null;
        }

        UnsubscribeRoster();
        phase = BattlePhase.Exploration;
        isBusy = false;
        round = 1;
        player = BattleUnits.PrimaryAlly ?? player;
        enemy = null;
        SubscribeRoster();
        SetEndRoundInteractable(false);
        boardClickController?.ClearSelection();
        handCardSystem?.CancelTargeting();
    }

    private IEnumerator BeginCombatRoutine(bool drawOpeningHand)
    {
        phase = BattlePhase.PlayerTurn;
        yield return null;
        if (drawOpeningHand) handCardSystem?.DrawOpeningHand();
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.f8Key.wasPressedThisFrame) return;
        WoundEnemiesForTest();
    }

    /// <summary>战斗中把存活敌人生命打到 1，方便测结算；编辑器 / Development 包可用。</summary>
    private void WoundEnemiesForTest()
    {
        if (phase != BattlePhase.PlayerTurn && phase != BattlePhase.EnemyTurn)
        {
            Debug.Log("当前不在战斗中，F8 不会扣血。", this);
            return;
        }

        int wounded = 0;
        BattleRoster roster = BattleRoster.Instance;
        if (roster != null)
        {
            for (int i = 0; i < roster.Enemies.Count; i++)
            {
                Unit unit = roster.Enemies[i];
                if (unit == null || !unit.IsAlive) continue;
                unit.Revive(1);
                wounded++;
            }
        }
        else if (enemy != null && enemy.IsAlive)
        {
            enemy.Revive(1);
            wounded = 1;
        }

        Debug.Log(wounded > 0 ? $"测试：已将 {wounded} 名敌人生命设为 1。" : "测试：没有可扣血的敌人。", this);
    }
#endif

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
        UtilityAiController ai = actor.GetComponent<UtilityAiController>();
        if (ai != null) yield return ai.RunTurn(enemyStepPause);
        actor.State.EndTurn(actor);
        ContentActorBehaviorRuntime.Execute(actor, ContentTriggerKeys.OnUnitTurnEnd, null);
    }

    private void HandleUnitDied(Unit unit)
    {
        if (phase == BattlePhase.GameOver || phase == BattlePhase.Exploration) return;
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

    /// <summary>屏幕中央一条简短胜负提示，点任意处关闭并退出战斗。</summary>
    private void ShowResult(bool playerWon)
    {
        if (resultOverlay != null) return;
        overlayCanvas ??= FindScreenCanvas();
        if (overlayCanvas == null) return;
        if (playerWon && RunSession.HasActive) CaptureRunAfterBattle(true);

        resultOverlay = new GameObject(
            "P_BattleResult", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        resultOverlay.transform.SetParent(overlayCanvas.transform, false);
        resultOverlay.transform.SetAsLastSibling();
        RectTransform root = (RectTransform)resultOverlay.transform;
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        Image catcher = resultOverlay.GetComponent<Image>();
        catcher.color = new Color(0f, 0f, 0f, 0.18f);
        catcher.raycastTarget = true;

        GameObject banner = new GameObject("Banner", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        banner.transform.SetParent(resultOverlay.transform, false);
        RectTransform bannerRect = (RectTransform)banner.transform;
        bannerRect.anchorMin = bannerRect.anchorMax = new Vector2(0.5f, 0.5f);
        bannerRect.sizeDelta = new Vector2(240f, 64f);
        Image bannerImage = banner.GetComponent<Image>();
        bannerImage.color = new Color(0.08f, 0.09f, 0.12f, 0.92f);
        bannerImage.raycastTarget = false;

        TMP_Text title = CreateText("ResultTitle", banner.transform, 34f);
        title.rectTransform.anchorMin = Vector2.zero;
        title.rectTransform.anchorMax = Vector2.one;
        title.rectTransform.offsetMin = title.rectTransform.offsetMax = Vector2.zero;
        title.text = playerWon ? "胜利" : "失败";
        title.color = Color.white;
        title.alignment = TextAlignmentOptions.Center;

        Button click = resultOverlay.GetComponent<Button>();
        click.transition = Selectable.Transition.None;
        click.onClick.AddListener(() => DismissResult(playerWon));
    }

    private void DismissResult(bool playerWon)
    {
        if (resultOverlay != null)
        {
            Destroy(resultOverlay);
            resultOverlay = null;
        }

        if (RunSession.HasActive)
        {
            if (playerWon)
            {
                if (WorldPlaySession.Instance != null)
                    WorldPlaySession.Instance.FinishCombatVictory();
                else
                    SceneManager.LoadScene("S_Battle");
                return;
            }

            RunSession.Clear();
            SceneManager.LoadScene("S_Menu");
            return;
        }

        SceneManager.LoadScene("S_ClassSelect");
    }

    private void CaptureRunAfterBattle(bool won)
    {
        if (!won || !RunSession.HasActive) return;
        WorldDefinition world = null;
        WorldCatalog.TryGet(RunSession.Current.worldId, out world);
        world ??= WorldCatalog.Default;
        WorldCatalog.TryGetStage(world, RunSession.Current.currentStageId, out StageDefinition stage);
        Unit ally = BattleRoster.Instance != null ? BattleRoster.Instance.PrimaryAlly : player;
        List<string> deck = handCardSystem != null ? handCardSystem.ExportOwnedCardIds() : new List<string>(RunSession.Current.deckCardIds);
        int health = ally != null ? ally.CurrentHealth : RunSession.Current.health;
        int maxHealth = ally != null ? ally.MaxHealth : RunSession.Current.maxHealth;
        RunSession.CompleteCurrentStage(health, maxHealth, deck, stage);
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
