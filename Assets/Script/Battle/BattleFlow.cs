using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum BattlePhase
{
    PlayerTurn,
    EnemyTurn,
    GameOver
}

/// <summary>
/// 战斗回合机：玩家结束回合后敌人移动/攻击，再轮到玩家清甲、回满行动点并抽牌。
/// 单位死亡时弹出胜负界面。
/// </summary>
public class BattleFlow : MonoBehaviour
{
    [SerializeField] private BoardClickController boardClickController;
    [SerializeField] private HandCardSystem handCardSystem;
    [SerializeField] private Unit player;
    [SerializeField] private Unit enemy;
    [SerializeField] private Button endRoundButton;
    [SerializeField] private Canvas overlayCanvas;
    [SerializeField, Min(0)] private int cardsDrawnPerRound = 3;
    [SerializeField, Min(0f)] private float drawInterval = 0.14f;
    [SerializeField, Min(0f)] private float enemyStepPause = 0.08f;

    private BattlePhase phase = BattlePhase.PlayerTurn;
    private bool isBusy;
    private GameObject resultOverlay;

    public BattlePhase Phase => phase;
    public static BattleFlow Instance { get; private set; }

    public static bool CanPlayerAct =>
        Instance == null ||
        (Instance.phase == BattlePhase.PlayerTurn && !Instance.isBusy);

    private void Awake()
    {
        Instance = this;
        ResolveReferences();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        Unsubscribe(player);
        Unsubscribe(enemy);
    }

    private void Start()
    {
        ResolveReferences();
        Subscribe(player);
        Subscribe(enemy);
        SetEndRoundInteractable(true);
    }

    public void RequestEndPlayerTurn()
    {
        if (!CanPlayerAct || isBusy)
        {
            return;
        }

        StartCoroutine(EndPlayerTurnRoutine());
    }

    private IEnumerator EndPlayerTurnRoutine()
    {
        isBusy = true;
        SetEndRoundInteractable(false);
        handCardSystem?.CancelTargeting();
        boardClickController?.ClearSelection();

        phase = BattlePhase.EnemyTurn;
        yield return EnemyTurnRoutine();

        if (phase == BattlePhase.GameOver)
        {
            isBusy = false;
            yield break;
        }

        yield return StartPlayerTurnRoutine();
        isBusy = false;
        SetEndRoundInteractable(true);
    }

    private IEnumerator StartPlayerTurnRoutine()
    {
        phase = BattlePhase.PlayerTurn;
        player?.ClearArmor();
        boardClickController?.ResetActionPoints();

        if (handCardSystem == null)
        {
            yield break;
        }

        int drawnCards = 0;
        for (int i = 0; i < cardsDrawnPerRound; i++)
        {
            if (!handCardSystem.DrawCard())
            {
                break;
            }

            drawnCards++;
            if (drawInterval > 0f && i < cardsDrawnPerRound - 1)
            {
                yield return new WaitForSecondsRealtime(drawInterval);
            }
        }

        Debug.Log($"玩家回合开始：护甲已清除，行动点已恢复，抽取 {drawnCards} 张牌。", this);
    }

    private IEnumerator EnemyTurnRoutine()
    {
        if (enemy == null || player == null || !enemy.IsAlive || !player.IsAlive)
        {
            yield break;
        }

        enemy.ClearArmor();
        yield return new WaitForSeconds(0.2f);

        if (IsAdjacent(enemy.Position, player.Position))
        {
            ResolveEnemyAttack();
            yield return new WaitForSeconds(0.35f);
            yield break;
        }

        yield return MoveEnemyTowardPlayer();

        if (phase != BattlePhase.GameOver &&
            player.IsAlive &&
            enemy.IsAlive &&
            IsAdjacent(enemy.Position, player.Position))
        {
            ResolveEnemyAttack();
            yield return new WaitForSeconds(0.35f);
        }
    }

    private IEnumerator MoveEnemyTowardPlayer()
    {
        BoardGenerator board = enemy.Board;
        if (board == null)
        {
            yield break;
        }

        List<Vector2Int> path = new List<Vector2Int>();
        if (!board.TryFindPath(enemy.Position, player.Position, enemy, path) ||
            path.Count <= 1)
        {
            yield break;
        }

        int steps = Mathf.Max(1, enemy.MoveStepsPerTurn);
        int moved = 0;
        for (int i = 1; i < path.Count && moved < steps; i++)
        {
            Vector2Int next = path[i];
            if (next == player.Position)
            {
                break;
            }

            if (!enemy.MoveToAnimated(next.x, next.y))
            {
                break;
            }

            moved++;
            while (enemy.IsMoving)
            {
                yield return null;
            }

            if (enemyStepPause > 0f)
            {
                yield return new WaitForSeconds(enemyStepPause);
            }
        }
    }

    private void ResolveEnemyAttack()
    {
        int damage = enemy.AttackDamage;
        CombatVfx.PlayClaw(enemy, player);
        int healthLost = player.TakeDamage(damage);
        Debug.Log($"敌人发动攻击，造成 {damage} 点伤害，玩家实际损失生命 {healthLost} 点。", this);
    }

    private void HandleUnitDied(Unit unit)
    {
        if (phase == BattlePhase.GameOver)
        {
            return;
        }

        phase = BattlePhase.GameOver;
        isBusy = false;
        boardClickController?.ClearSelection();
        SetEndRoundInteractable(false);

        bool playerWon = unit != null && unit.Faction == UnitFaction.Enemy;
        ShowResult(playerWon);
    }

    private void ShowResult(bool playerWon)
    {
        if (resultOverlay != null)
        {
            return;
        }

        if (overlayCanvas == null)
        {
            overlayCanvas = FindAnyObjectByType<Canvas>();
        }

        if (overlayCanvas == null)
        {
            Debug.Log(playerWon ? "胜利" : "失败", this);
            return;
        }

        resultOverlay = new GameObject(
            "P_BattleResult",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        resultOverlay.transform.SetParent(overlayCanvas.transform, false);
        resultOverlay.transform.SetAsLastSibling();

        RectTransform overlayRect = (RectTransform)resultOverlay.transform;
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        resultOverlay.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);

        TMP_FontAsset font = FindExistingFont(overlayCanvas.transform);
        TMP_Text title = CreateText("ResultTitle", resultOverlay.transform, font, 64f);
        RectTransform titleRect = (RectTransform)title.transform;
        titleRect.anchorMin = new Vector2(0.5f, 0.5f);
        titleRect.anchorMax = new Vector2(0.5f, 0.5f);
        titleRect.pivot = new Vector2(0.5f, 0.5f);
        titleRect.anchoredPosition = new Vector2(0f, 60f);
        titleRect.sizeDelta = new Vector2(600f, 90f);
        title.text = playerWon ? "胜利" : "失败";
        title.alignment = TextAlignmentOptions.Center;

        GameObject buttonObject = new GameObject(
            "B_Restart",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button));
        buttonObject.transform.SetParent(resultOverlay.transform, false);

        RectTransform buttonRect = (RectTransform)buttonObject.transform;
        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = new Vector2(0f, -40f);
        buttonRect.sizeDelta = new Vector2(220f, 56f);
        buttonObject.GetComponent<Image>().color = new Color(0.18f, 0.22f, 0.3f, 1f);

        TMP_Text buttonLabel = CreateText("Label", buttonObject.transform, font, 28f);
        RectTransform labelRect = (RectTransform)buttonLabel.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        buttonLabel.text = "再来一局";
        buttonLabel.alignment = TextAlignmentOptions.Center;
        buttonLabel.raycastTarget = false;

        Button button = buttonObject.GetComponent<Button>();
        button.onClick.AddListener(RestartBattle);
    }

    private void RestartBattle()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void ResolveReferences()
    {
        if (boardClickController == null)
        {
            boardClickController = GetComponent<BoardClickController>();
            if (boardClickController == null)
            {
                boardClickController = FindAnyObjectByType<BoardClickController>();
            }
        }

        if (handCardSystem == null)
        {
            handCardSystem = FindAnyObjectByType<HandCardSystem>();
        }

        if (player == null)
        {
            GameObject playerObject = GameObject.Find("Player");
            player = playerObject == null ? null : playerObject.GetComponent<Unit>();
        }

        if (enemy == null)
        {
            GameObject enemyObject = GameObject.Find("Monster");
            enemy = enemyObject == null ? null : enemyObject.GetComponent<Unit>();
        }

        if (endRoundButton == null)
        {
            GameObject buttonObject = GameObject.Find("B_EndRound");
            endRoundButton = buttonObject == null ? null : buttonObject.GetComponent<Button>();
        }

        if (overlayCanvas == null)
        {
            overlayCanvas = FindAnyObjectByType<Canvas>();
        }
    }

    private void SetEndRoundInteractable(bool interactable)
    {
        if (endRoundButton != null)
        {
            endRoundButton.interactable = interactable;
        }
    }

    private void Subscribe(Unit unit)
    {
        if (unit != null)
        {
            unit.Died += HandleUnitDied;
        }
    }

    private void Unsubscribe(Unit unit)
    {
        if (unit != null)
        {
            unit.Died -= HandleUnitDied;
        }
    }

    private static bool IsAdjacent(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) == 1;
    }

    private static TMP_Text CreateText(
        string name, Transform parent, TMP_FontAsset font, float fontSize)
    {
        GameObject textObject = new GameObject(
            name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        TMP_Text text = textObject.GetComponent<TMP_Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static TMP_FontAsset FindExistingFont(Transform root)
    {
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.font != null)
            {
                return text.font;
            }
        }

        return TMP_Settings.defaultFontAsset;
    }
}
