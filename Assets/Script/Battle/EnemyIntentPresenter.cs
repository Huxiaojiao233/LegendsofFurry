using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>在敌方头顶显示攻击意图，并高亮其预览目标格。</summary>
public sealed class EnemyIntentPresenter : MonoBehaviour
{
    public static EnemyIntentPresenter Instance { get; private set; }

    private static readonly Color IntentCellColor = new Color(1f, 0.35f, 0.15f, 0.85f);
    private static readonly Color IntentMoveColor = new Color(1f, 0.7f, 0.2f, 0.75f);

    private readonly Dictionary<Unit, TextMeshPro> labels = new Dictionary<Unit, TextMeshPro>();
    private readonly List<BoardCell> intentCells = new List<BoardCell>();
    private Transform root;

    private void Awake()
    {
        Instance = this;
        root = new GameObject("EnemyIntentRoot").transform;
        root.SetParent(transform, false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        ClearVisuals();
    }

    public static EnemyIntentPresenter Ensure()
    {
        if (Instance != null) return Instance;
        BattleFlow flow = BattleFlow.Instance;
        GameObject host = flow != null ? flow.gameObject : new GameObject("EnemyIntentPresenter");
        return host.GetComponent<EnemyIntentPresenter>() ?? host.AddComponent<EnemyIntentPresenter>();
    }

    public void RefreshAll()
    {
        ClearVisuals();
        if (BattleFlow.Instance != null && BattleFlow.Instance.Phase == BattlePhase.Exploration)
            return;

        BattleRoster roster = BattleRoster.Instance;
        if (roster == null) return;
        for (int i = 0; i < roster.Enemies.Count; i++)
        {
            Unit enemy = roster.Enemies[i];
            if (enemy == null || !enemy.IsAlive) continue;
            UtilityAiController ai = enemy.GetComponent<UtilityAiController>();
            if (ai == null) continue;
            if (!ai.TryPeekIntent(out EnemyIntentPreview intent) || !intent.HasValue) continue;
            AttachLabel(enemy, intent);
            HighlightIntent(enemy, intent);
        }
    }

    public void ClearVisuals()
    {
        foreach (KeyValuePair<Unit, TextMeshPro> pair in labels)
        {
            if (pair.Value != null) Destroy(pair.Value.gameObject);
        }

        labels.Clear();
        for (int i = 0; i < intentCells.Count; i++)
        {
            if (intentCells[i] != null)
                intentCells[i].SetIntentHighlight(false, IntentCellColor);
        }

        intentCells.Clear();
    }

    private void LateUpdate()
    {
        Camera camera = Camera.main;
        if (camera == null) return;
        foreach (KeyValuePair<Unit, TextMeshPro> pair in labels)
        {
            if (pair.Key == null || pair.Value == null) continue;
            pair.Value.transform.position = pair.Key.transform.position + Vector3.up * 1.55f;
            pair.Value.transform.rotation =
                Quaternion.LookRotation(pair.Value.transform.position - camera.transform.position);
        }
    }

    private void AttachLabel(Unit enemy, EnemyIntentPreview intent)
    {
        GameObject labelObject = new GameObject($"Intent_{enemy.DisplayName}");
        labelObject.transform.SetParent(root, false);
        TextMeshPro text = labelObject.AddComponent<TextMeshPro>();
        text.fontSize = 3.2f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(1f, 0.85f, 0.35f, 1f);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        MeshRenderer meshRenderer = labelObject.GetComponent<MeshRenderer>();
        if (meshRenderer != null) meshRenderer.sortingOrder = 20;
        string icon = intent.ActionKind switch
        {
            EnemyIntentPreview.Kind.PlayCard => "攻",
            EnemyIntentPreview.Kind.Retreat => "退",
            EnemyIntentPreview.Kind.Approach => "靠",
            _ => "·"
        };
        text.text = $"[{icon}] {intent.Label}";
        labels[enemy] = text;
    }

    private void HighlightIntent(Unit enemy, EnemyIntentPreview intent)
    {
        BoardGenerator board = enemy.Board ?? Object.FindAnyObjectByType<BoardGenerator>();
        if (board == null) return;
        if (intent.Target != null &&
            board.TryGetCell(intent.Target.Position.x, intent.Target.Position.y, out BoardCell targetCell))
        {
            targetCell.SetIntentHighlight(true, IntentCellColor);
            intentCells.Add(targetCell);
        }

        if (intent.Destination.HasValue &&
            board.TryGetCell(intent.Destination.Value.x, intent.Destination.Value.y, out BoardCell destCell))
        {
            destCell.SetIntentHighlight(true, IntentMoveColor);
            intentCells.Add(destCell);
        }
    }
}
