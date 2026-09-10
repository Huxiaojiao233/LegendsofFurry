using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LegendsOfFurry.Content.Contracts;

/// <summary>点击敌方单位后，只读展示手牌、下回合手牌预览、牌堆与弃牌堆。</summary>
public sealed class EnemyCardInspectUI : MonoBehaviour
{
    public static EnemyCardInspectUI Instance { get; private set; }

    private GameObject modal;
    private Unit inspected;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public static EnemyCardInspectUI Ensure()
    {
        if (Instance != null) return Instance;
        GameObject host = new GameObject("EnemyCardInspectUI");
        return host.AddComponent<EnemyCardInspectUI>();
    }

    public bool IsOpen => modal != null;

    public void Show(Unit enemy)
    {
        if (enemy == null || enemy.IsPlayer) return;
        Close();
        UtilityAiController ai = enemy.GetComponent<UtilityAiController>();
        if (ai == null)
        {
            Debug.LogWarning($"单位 {enemy.DisplayName} 没有 AI 牌组，无法检视。", enemy);
            return;
        }

        inspected = enemy;
        ai.EnsureDeckPublic();
        IReadOnlyList<CardInstance> hand = ai.GetCards(ContentCardZoneKeys.Hand);
        IReadOnlyList<CardInstance> draw = ai.GetCards(ContentCardZoneKeys.Draw);
        IReadOnlyList<CardInstance> discard = ai.GetCards(ContentCardZoneKeys.Discard);
        IReadOnlyList<CardInstance> exhaust = ai.GetCards(ContentCardZoneKeys.Exhaust);
        List<CardInstance> upcoming = ai.CopyUpcomingHandPreview();

        StringBuilder body = new StringBuilder();
        body.AppendLine($"【{enemy.DisplayName}】");
        if (ai.CachedIntent.HasValue)
            body.AppendLine($"意图：{ai.CachedIntent.Label}");
        body.AppendLine();
        body.AppendLine($"手牌（{hand.Count}）");
        body.AppendLine(FormatCards(hand));
        body.AppendLine();
        body.AppendLine($"下回合手牌预览（{upcoming.Count}）");
        body.AppendLine(FormatCards(upcoming));
        body.AppendLine();
        body.AppendLine($"牌堆（{draw.Count}，最上为牌顶）");
        body.AppendLine(FormatCards(draw));
        body.AppendLine();
        body.AppendLine($"弃牌堆（{discard.Count}）");
        body.AppendLine(FormatCards(discard));
        if (exhaust.Count > 0)
        {
            body.AppendLine();
            body.AppendLine($"消耗堆（{exhaust.Count}）");
            body.AppendLine(FormatCards(exhaust));
        }

        modal = CreateModal($"{enemy.DisplayName} · 牌组");
        Transform content = modal.transform.Find("Panel/ScrollView/Viewport/Content");
        TMP_Text label = CreateText("CardList", content, 17f);
        label.text = body.ToString();
        label.alignment = TextAlignmentOptions.TopLeft;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Overflow;
        ContentSizeFitter fitter = label.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        LayoutElement layout = label.gameObject.AddComponent<LayoutElement>();
        layout.minHeight = 120f;
        layout.flexibleWidth = 1f;
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 1f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.pivot = new Vector2(0.5f, 1f);
        labelRect.sizeDelta = new Vector2(0f, 0f);
        Button close = CreateButton(modal.transform.Find("Panel"), "关闭");
        close.onClick.AddListener(Close);
        Canvas.ForceUpdateCanvases();
    }

    public void Close()
    {
        if (modal != null) Destroy(modal);
        modal = null;
        inspected = null;
    }

    private void Update()
    {
        if (modal == null) return;
        if (inspected == null || !inspected.IsAlive) Close();
    }

    private static string FormatCards(IReadOnlyList<CardInstance> cards)
    {
        if (cards == null || cards.Count == 0) return "（空）";
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < cards.Count; i++)
        {
            CardInstance card = cards[i];
            if (card?.Data == null) continue;
            builder.Append(i + 1).Append(". ").Append(card.Data.cardName)
                .Append("　[").Append(card.Data.costText).Append(']');
            if (i + 1 < cards.Count) builder.Append('\n');
        }

        return builder.Length == 0 ? "（空）" : builder.ToString();
    }

    private GameObject CreateModal(string title)
    {
        Canvas canvas = FindScreenCanvas();
        GameObject root = new GameObject("EnemyInspectModal", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        root.transform.SetParent(canvas.transform, false);
        RectTransform rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;
        root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);

        GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panel.transform.SetParent(root.transform, false);
        RectTransform panelRect = (RectTransform)panel.transform;
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(640f, 720f);
        panel.GetComponent<Image>().color = Color.white;

        TMP_Text heading = CreateText("Heading", panel.transform, 26f);
        RectTransform headingRect = heading.rectTransform;
        headingRect.anchorMin = new Vector2(0f, 1f);
        headingRect.anchorMax = new Vector2(1f, 1f);
        headingRect.pivot = new Vector2(0.5f, 1f);
        headingRect.anchoredPosition = new Vector2(0f, -12f);
        headingRect.sizeDelta = new Vector2(-32f, 40f);
        heading.text = title;
        heading.color = new Color(0.12f, 0.12f, 0.14f);
        heading.alignment = TextAlignmentOptions.Center;

        GameObject scrollObject = new GameObject("ScrollView", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(ScrollRect));
        scrollObject.transform.SetParent(panel.transform, false);
        RectTransform scrollRectTransform = (RectTransform)scrollObject.transform;
        scrollRectTransform.anchorMin = new Vector2(0.05f, 0.12f);
        scrollRectTransform.anchorMax = new Vector2(0.95f, 0.88f);
        scrollRectTransform.offsetMin = Vector2.zero;
        scrollRectTransform.offsetMax = Vector2.zero;
        scrollObject.GetComponent<Image>().color = new Color(0.96f, 0.96f, 0.97f, 1f);
        ScrollRect scroll = scrollObject.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28f;

        GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(RectMask2D));
        viewport.transform.SetParent(scrollObject.transform, false);
        RectTransform viewportRect = (RectTransform)viewport.transform;
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(8f, 8f);
        viewportRect.offsetMax = new Vector2(-8f, -8f);
        viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
        viewport.GetComponent<Image>().raycastTarget = true;

        GameObject content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        RectTransform contentRect = (RectTransform)content.transform;
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0f, 0f);
        VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        layout.padding = new RectOffset(4, 4, 4, 4);
        layout.spacing = 4f;
        ContentSizeFitter contentFitter = content.GetComponent<ContentSizeFitter>();
        contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewportRect;
        scroll.content = contentRect;

        root.transform.SetAsLastSibling();
        return root;
    }

    private Button CreateButton(Transform parent, string label)
    {
        GameObject obj = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        obj.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)obj.transform;
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 18f);
        rect.sizeDelta = new Vector2(160f, 42f);
        obj.GetComponent<Image>().color = new Color(0.2f, 0.45f, 0.75f, 1f);
        Button button = obj.GetComponent<Button>();
        TMP_Text text = CreateText("Label", obj.transform, 20f);
        text.text = label;
        text.alignment = TextAlignmentOptions.Center;
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        return button;
    }

    private static TMP_Text CreateText(string name, Transform parent, float size)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        TMP_Text text = obj.GetComponent<TMP_Text>();
        text.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/SourceHanSansSC-Regular SDF") ??
                    TMP_Settings.defaultFontAsset;
        text.fontSize = size;
        text.color = new Color(0.12f, 0.12f, 0.14f);
        text.raycastTarget = false;
        return text;
    }

    private static Canvas FindScreenCanvas()
    {
        foreach (Canvas canvas in Object.FindObjectsByType<Canvas>())
        {
            if (canvas.renderMode != RenderMode.WorldSpace) return canvas;
        }

        GameObject created = new GameObject("EnemyInspectCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas result = created.GetComponent<Canvas>();
        result.renderMode = RenderMode.ScreenSpaceOverlay;
        result.sortingOrder = 80;
        return result;
    }
}
