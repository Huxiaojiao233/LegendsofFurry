using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// 棋盘交互总控制器。
/// 负责己方单位选中、四方向可达范围、行动点消耗、格子点击与悬浮消耗提示。
/// </summary>
public class BoardClickController : MonoBehaviour
{
    private static readonly Vector2Int[] FourDirections =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left
    };

    [Header("点击检测")]
    [SerializeField] private Camera mainCamera;

    [Header("行动点")]
    [SerializeField, Min(0)] private int maxActionPoints = 3;
    [SerializeField] private int currentActionPoints;
    [SerializeField] private int nextMoveDiscount;

    [Header("UI")]
    [SerializeField] private TMP_Text actionPointText;
    [SerializeField] private Canvas tooltipCanvas;

    [Header("移动范围颜色")]
    [SerializeField] private Color lowCostColor =
        new Color(0.1f, 0.9f, 0.2f, 1f);
    [SerializeField] private Color highCostColor =
        new Color(0.72f, 1f, 0.75f, 1f);

    [Header("攻击范围颜色")]
    [SerializeField] private Color attackRangeColor =
        new Color(1f, 0.28f, 0.12f, 0.95f);
    [SerializeField] private Color attackTargetColor =
        new Color(1f, 0.72f, 0.2f, 1f);

    [Header("选目标")]
    [SerializeField] private HandCardSystem handCardSystem;

    private Unit selectedUnit;
    private readonly Dictionary<BoardCell, int> movableCells =
        new Dictionary<BoardCell, int>();
    private readonly List<BoardCell> attackCells = new List<BoardCell>();

    private TMP_Text moveCostTooltip;
    private RectTransform tooltipRect;

    public int MaxActionPoints => maxActionPoints;
    public int CurrentActionPoints => currentActionPoints;
    public int NextMoveDiscount => nextMoveDiscount;
    public event Action<int, int> ActionPointsChanged;

    private void Awake()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        currentActionPoints = maxActionPoints;
        BindActionPointUI();
        ResolveHandCardSystem();
        CreateMoveCostTooltip();
        UpdateActionPointUI();
    }

    private void Update()
    {
        if (!BattleFlow.CanPlayerAct || Mouse.current == null || mainCamera == null)
        {
            HideMoveCostTooltip();
            return;
        }

        if (IsTargeting)
        {
            HideMoveCostTooltip();
            if (Mouse.current.rightButton.wasPressedThisFrame)
            {
                handCardSystem.CancelTargeting();
                return;
            }

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                HandleTargetingClick();
            }

            return;
        }

        UpdateMoveCostTooltip();

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            HandleClick();
        }
    }

    public void ResetActionPoints()
    {
        nextMoveDiscount = 0;
        SetActionPoints(maxActionPoints);

        if (selectedUnit != null)
        {
            ClearMoveRange();
            ShowMoveRange(selectedUnit);
        }
    }

    public bool TrySpendActionPoints(int amount)
    {
        if (amount < 0 || amount > currentActionPoints)
        {
            return false;
        }

        SetActionPoints(currentActionPoints - amount);
        return true;
    }

    public void GrantNextMoveDiscount(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        nextMoveDiscount += amount;

        if (selectedUnit != null)
        {
            ClearMoveRange();
            ShowMoveRange(selectedUnit);
        }
    }

    public void ClearSelection()
    {
        if (selectedUnit != null)
        {
            selectedUnit.SetSelected(false);
            selectedUnit = null;
        }

        ClearMoveRange();
        ClearAttackRange();
        HideMoveCostTooltip();
    }

    public void ShowAttackRange(Unit caster, int radius)
    {
        ClearMoveRange();
        ClearAttackRange();
        HideMoveCostTooltip();

        if (caster == null || caster.Board == null || radius < 0)
        {
            return;
        }

        BoardGenerator board = caster.Board;
        Vector2Int origin = caster.Position;

        for (int x = origin.x - radius; x <= origin.x + radius; x++)
        {
            for (int z = origin.y - radius; z <= origin.y + radius; z++)
            {
                if (!board.TryGetCell(x, z, out BoardCell cell))
                {
                    continue;
                }

                bool hasEnemy = board.TryGetOccupant(x, z, out Unit occupant) &&
                    occupant != null &&
                    occupant.IsAlive &&
                    occupant.Faction == UnitFaction.Enemy;
                cell.SetMoveHighlight(true, hasEnemy ? attackTargetColor : attackRangeColor);
                attackCells.Add(cell);
            }
        }
    }

    public void ClearAttackRange()
    {
        for (int i = 0; i < attackCells.Count; i++)
        {
            if (attackCells[i] != null)
            {
                attackCells[i].SetMoveHighlight(false, attackRangeColor);
            }
        }

        attackCells.Clear();
    }

    private bool IsTargeting =>
        handCardSystem != null && handCardSystem.IsTargeting;

    private void ResolveHandCardSystem()
    {
        if (handCardSystem == null)
        {
            handCardSystem = FindAnyObjectByType<HandCardSystem>();
        }
    }

    private void HandleTargetingClick()
    {
        ResolveHandCardSystem();
        if (handCardSystem == null)
        {
            return;
        }

        if (EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        if (!TryRaycastPointer(out RaycastHit hit))
        {
            handCardSystem.CancelTargeting();
            return;
        }

        Unit clickedUnit = hit.collider.GetComponentInParent<Unit>();
        if (handCardSystem.TryConfirmTarget(clickedUnit))
        {
            return;
        }

        handCardSystem.CancelTargeting();
    }

    private void HandleClick()
    {
        if (EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        if (!TryRaycastPointer(out RaycastHit hit))
        {
            return;
        }

        Unit clickedUnit = hit.collider.GetComponentInParent<Unit>();
        if (clickedUnit != null)
        {
            if (!clickedUnit.IsMoving && clickedUnit.IsPlayer)
            {
                SelectUnit(clickedUnit);
            }

            return;
        }

        BoardCell clickedCell = hit.collider.GetComponentInParent<BoardCell>();
        if (clickedCell != null)
        {
            ClickCell(clickedCell);
        }
    }

    private void SelectUnit(Unit unit)
    {
        if (selectedUnit == unit)
        {
            selectedUnit.SetSelected(false);
            selectedUnit = null;
            ClearMoveRange();
            HideMoveCostTooltip();
            Debug.Log($"取消选中棋子：{unit.gameObject.name}");
            return;
        }

        if (selectedUnit != null && selectedUnit != unit)
        {
            selectedUnit.SetSelected(false);
        }

        ClearMoveRange();
        selectedUnit = unit;
        selectedUnit.SetSelected(true);
        ShowMoveRange(unit);

        Debug.Log(
            $"选中了棋子：{unit.gameObject.name}，剩余行动点：{currentActionPoints}");
    }

    private void ClickCell(BoardCell cell)
    {
        if (selectedUnit == null || !movableCells.TryGetValue(cell, out int cost))
        {
            return;
        }

        Vector2Int coordinate = cell.Coordinate;
        if (!selectedUnit.MoveToAnimated(coordinate.x, coordinate.y))
        {
            return;
        }

        TrySpendActionPoints(cost);
        nextMoveDiscount = 0;
        Debug.Log($"移动到 {coordinate}，消耗 {cost} 点行动点，剩余 {currentActionPoints} 点");

        selectedUnit.SetSelected(false);
        selectedUnit = null;
        ClearMoveRange();
        HideMoveCostTooltip();
    }

    private void ShowMoveRange(Unit unit)
    {
        ClearAttackRange();
        int searchBudget = currentActionPoints + nextMoveDiscount;
        if (searchBudget <= 0)
        {
            return;
        }

        BoardGenerator board = unit.Board;
        Vector2Int start = unit.Position;
        Queue<Vector2Int> frontier = new Queue<Vector2Int>();
        Dictionary<Vector2Int, int> costs = new Dictionary<Vector2Int, int>();

        frontier.Enqueue(start);
        costs[start] = 0;

        while (frontier.Count > 0)
        {
            Vector2Int current = frontier.Dequeue();
            int currentCost = costs[current];

            if (currentCost >= searchBudget)
            {
                continue;
            }

            foreach (Vector2Int direction in FourDirections)
            {
                Vector2Int next = current + direction;
                if (costs.ContainsKey(next) ||
                    !board.TryGetCell(next.x, next.y, out BoardCell cell) ||
                    board.IsOccupied(next.x, next.y, unit))
                {
                    continue;
                }

                int nextStepDistance = currentCost + 1;
                costs[next] = nextStepDistance;
                frontier.Enqueue(next);
                int payableCost = Mathf.Max(0, nextStepDistance - nextMoveDiscount);
                movableCells[cell] = payableCost;

                float fade = maxActionPoints <= 1
                    ? 0f
                    : payableCost / Mathf.Max(1f, maxActionPoints);
                Color color = Color.Lerp(lowCostColor, highCostColor, fade);
                cell.SetMoveHighlight(true, color);
            }
        }
    }

    private void ClearMoveRange()
    {
        foreach (BoardCell cell in movableCells.Keys)
        {
            if (cell != null)
            {
                cell.SetMoveHighlight(false, lowCostColor);
            }
        }

        movableCells.Clear();
    }

    private bool TryRaycastPointer(out RaycastHit hit)
    {
        Vector2 mousePosition = Mouse.current.position.ReadValue();
        Ray ray = mainCamera.ScreenPointToRay(mousePosition);
        return Physics.Raycast(ray, out hit);
    }

    private void UpdateMoveCostTooltip()
    {
        if (moveCostTooltip == null || movableCells.Count == 0 ||
            (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) ||
            !TryRaycastPointer(out RaycastHit hit))
        {
            HideMoveCostTooltip();
            return;
        }

        BoardCell cell = hit.collider.GetComponentInParent<BoardCell>();
        if (cell == null || !movableCells.TryGetValue(cell, out int cost))
        {
            HideMoveCostTooltip();
            return;
        }

        moveCostTooltip.text = $"<mark=#000000B0> 消耗 {cost} 点 </mark>";
        PositionTooltip(Mouse.current.position.ReadValue());
        moveCostTooltip.gameObject.SetActive(true);
    }

    private void PositionTooltip(Vector2 screenPosition)
    {
        RectTransform canvasRect = (RectTransform)tooltipCanvas.transform;
        Camera uiCamera = tooltipCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : tooltipCanvas.worldCamera;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, screenPosition, uiCamera, out Vector2 localPoint))
        {
            tooltipRect.anchoredPosition = localPoint + new Vector2(22f, 30f);
        }
    }

    private void HideMoveCostTooltip()
    {
        if (moveCostTooltip != null)
        {
            moveCostTooltip.gameObject.SetActive(false);
        }
    }

    private void BindActionPointUI()
    {
        if (actionPointText != null)
        {
            return;
        }

        GameObject cardControl = GameObject.Find("C_CardControl");
        Transform actionPoint = cardControl == null
            ? null
            : cardControl.transform.Find("ActionPoint");
        Transform number = actionPoint == null ? null : actionPoint.Find("N_Point");
        actionPointText = number == null ? null : number.GetComponent<TMP_Text>();

        if (actionPointText == null)
        {
            Debug.LogWarning("未找到行动点文字，请在 Inspector 指定 Action Point Text。", this);
        }
    }

    private void CreateMoveCostTooltip()
    {
        if (tooltipCanvas == null)
        {
            tooltipCanvas = FindAnyObjectByType<Canvas>();
        }

        if (tooltipCanvas == null)
        {
            return;
        }

        GameObject tooltipObject = new GameObject(
            "MoveCostTooltip",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        tooltipObject.transform.SetParent(tooltipCanvas.transform, false);

        tooltipRect = (RectTransform)tooltipObject.transform;
        tooltipRect.anchorMin = new Vector2(0.5f, 0.5f);
        tooltipRect.anchorMax = new Vector2(0.5f, 0.5f);
        tooltipRect.pivot = new Vector2(0f, 0.5f);
        tooltipRect.sizeDelta = new Vector2(150f, 42f);

        moveCostTooltip = tooltipObject.GetComponent<TMP_Text>();
        moveCostTooltip.fontSize = 24f;
        moveCostTooltip.color = Color.white;
        moveCostTooltip.alignment = TextAlignmentOptions.Center;
        moveCostTooltip.raycastTarget = false;

        TMP_Text[] existingTexts = tooltipCanvas.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text text in existingTexts)
        {
            if (text != moveCostTooltip && text.font != null)
            {
                moveCostTooltip.font = text.font;
                break;
            }
        }

        tooltipObject.SetActive(false);
    }

    private void SetActionPoints(int value)
    {
        currentActionPoints = Mathf.Clamp(value, 0, maxActionPoints);
        UpdateActionPointUI();
        ActionPointsChanged?.Invoke(currentActionPoints, maxActionPoints);
    }

    private void UpdateActionPointUI()
    {
        if (actionPointText != null)
        {
            actionPointText.text = $"{currentActionPoints}/{maxActionPoints}";
        }
    }

    private void OnValidate()
    {
        maxActionPoints = Mathf.Max(0, maxActionPoints);
        currentActionPoints = Mathf.Clamp(currentActionPoints, 0, maxActionPoints);
    }

    private void OnDisable()
    {
        ClearSelection();
    }
}
