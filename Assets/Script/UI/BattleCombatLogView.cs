using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LegendsOfFurry.Content.Contracts;

/// <summary>
/// 把战斗记录写入 S_Battle 的 BattleInfomation Scroll View，并提供分类筛选。
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleCombatLogView : MonoBehaviour
{
    private static readonly (BattleLogCategory Category, string Label)[] FilterOptions =
    {
        (BattleLogCategory.All, "全部"),
        (BattleLogCategory.Damage, "伤害"),
        (BattleLogCategory.Heal | BattleLogCategory.Armor, "治疗/护甲"),
        (BattleLogCategory.Move, "移动"),
        (BattleLogCategory.Card, "出牌"),
        (BattleLogCategory.Status, "状态"),
        (BattleLogCategory.Turn, "回合"),
    };

    private readonly List<IDisposable> subscriptions = new List<IDisposable>();
    private readonly List<(BattleLogCategory Category, GameObject Row)> rows =
        new List<(BattleLogCategory, GameObject)>();

    private ScrollRect scrollRect;
    private RectTransform content;
    private BattleLogCategory filterMask = BattleLogCategory.All;
    private readonly Dictionary<BattleLogCategory, Toggle> filterToggles =
        new Dictionary<BattleLogCategory, Toggle>();

    private void Awake()
    {
        BindScrollView();
        BuildFilterBar();
        SubscribeCombatEvents();
        BattleCombatLog.EntryAdded += OnEntryAdded;
        BattleCombatLog.Cleared += OnCleared;
    }

    private void OnDestroy()
    {
        BattleCombatLog.EntryAdded -= OnEntryAdded;
        BattleCombatLog.Cleared -= OnCleared;
        for (int i = 0; i < subscriptions.Count; i++)
            subscriptions[i]?.Dispose();
        subscriptions.Clear();
    }

    private void Start()
    {
        RebuildFromStore();
    }

    private void BindScrollView()
    {
        Transform root = FindDescendant(transform, "BattleInfomation") ??
                         FindDescendant(transform, "BattleInformation");
        if (root == null)
        {
            Debug.LogWarning("未找到 BattleInfomation Scroll View，战斗信息无法显示。", this);
            return;
        }

        scrollRect = root.GetComponent<ScrollRect>();
        if (scrollRect == null)
        {
            Debug.LogWarning("BattleInfomation 缺少 ScrollRect。", this);
            return;
        }

        content = scrollRect.content;
        if (content == null)
        {
            Transform contentTransform = FindDescendant(root, "Content");
            content = contentTransform as RectTransform;
            scrollRect.content = content;
        }

        if (content == null) return;

        VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
        if (layout == null) layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        layout.spacing = 4f;
        layout.padding = new RectOffset(8, 8, 6, 6);

        ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
        if (fitter == null) fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
    }

    private void BuildFilterBar()
    {
        if (scrollRect == null) return;
        RectTransform host = scrollRect.transform as RectTransform;
        if (host == null) return;

        const float filterHeight = 36f;
        GameObject bar = new GameObject("BattleLogFilters", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(HorizontalLayoutGroup));
        bar.transform.SetParent(host, false);
        // 必须画在 Viewport 之上，否则日志字会盖住筛选钮。
        bar.transform.SetAsLastSibling();
        RectTransform barRect = (RectTransform)bar.transform;
        barRect.anchorMin = new Vector2(0f, 1f);
        barRect.anchorMax = new Vector2(1f, 1f);
        barRect.pivot = new Vector2(0.5f, 1f);
        barRect.sizeDelta = new Vector2(0f, filterHeight);
        barRect.anchoredPosition = Vector2.zero;
        Image barBackground = bar.GetComponent<Image>();
        barBackground.color = new Color(0.08f, 0.09f, 0.12f, 0.98f);
        barBackground.raycastTarget = true;

        HorizontalLayoutGroup row = bar.GetComponent<HorizontalLayoutGroup>();
        row.spacing = 4f;
        row.padding = new RectOffset(4, 4, 2, 2);
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = true;
        row.childForceExpandHeight = true;

        if (scrollRect.viewport != null)
        {
            RectTransform viewport = scrollRect.viewport;
            Vector2 max = viewport.offsetMax;
            viewport.offsetMax = new Vector2(max.x, -filterHeight);
        }

        for (int i = 0; i < FilterOptions.Length; i++)
        {
            (BattleLogCategory category, string label) = FilterOptions[i];
            Toggle toggle = CreateFilterToggle(bar.transform, label, category == BattleLogCategory.All);
            filterToggles[category] = toggle;
            BattleLogCategory captured = category;
            toggle.onValueChanged.AddListener(on => OnFilterChanged(captured, on));
        }
    }

    private void OnFilterChanged(BattleLogCategory category, bool enabled)
    {
        if (category == BattleLogCategory.All)
        {
            if (enabled)
            {
                filterMask = BattleLogCategory.All;
                foreach (KeyValuePair<BattleLogCategory, Toggle> pair in filterToggles)
                {
                    if (pair.Key == BattleLogCategory.All) continue;
                    pair.Value.SetIsOnWithoutNotify(false);
                }
            }
            else if (filterMask == BattleLogCategory.All)
            {
                filterMask = BattleLogCategory.None;
            }
        }
        else
        {
            if (enabled)
            {
                if (filterMask == BattleLogCategory.All) filterMask = BattleLogCategory.None;
                filterMask |= category;
                if (filterToggles.TryGetValue(BattleLogCategory.All, out Toggle all))
                    all.SetIsOnWithoutNotify(false);
            }
            else
            {
                filterMask &= ~category;
            }

            if (filterMask == BattleLogCategory.None &&
                filterToggles.TryGetValue(BattleLogCategory.All, out Toggle restoreAll))
            {
                restoreAll.SetIsOnWithoutNotify(true);
                filterMask = BattleLogCategory.All;
            }
        }

        ApplyFilterVisibility();
    }

    private void ApplyFilterVisibility()
    {
        for (int i = 0; i < rows.Count; i++)
        {
            bool visible = (rows[i].Category & filterMask) != 0;
            if (rows[i].Row != null) rows[i].Row.SetActive(visible);
        }
    }

    private void SubscribeCombatEvents()
    {
        subscriptions.Add(CombatEventBus.Shared.Subscribe<DamageResolvedEvent>(OnDamage));
        subscriptions.Add(CombatEventBus.Shared.Subscribe<UnitMoveCompletedEvent>(OnMove));
        subscriptions.Add(CombatEventBus.Shared.Subscribe<StatusChangedEvent>(OnStatus));
        subscriptions.Add(CombatEventBus.Shared.Subscribe<CardPlayedEvent>(OnCardPlayed));
        subscriptions.Add(CombatEventBus.Shared.Subscribe<HealResolvedEvent>(OnHeal));
        subscriptions.Add(CombatEventBus.Shared.Subscribe<ArmorGainedEvent>(OnArmor));
        subscriptions.Add(CombatEventBus.Shared.Subscribe<UnitTriggerEvent>(OnUnitTrigger));
    }

    private void OnDamage(DamageResolvedEvent evt)
    {
        DamageResolution resolution = evt?.Resolution;
        DamageRequest request = resolution?.Request;
        if (request?.Target == null) return;
        if (resolution.WasDodgedOrImmune)
        {
            BattleCombatLog.Append(BattleLogCategory.Damage,
                $"{NameOf(request.Target)} 免疫了来自 {NameOf(request.Source)} 的伤害");
            return;
        }

        string source = NameOf(request.Source);
        string target = NameOf(request.Target);
        string armor = resolution.AbsorbedByArmor > 0 ? $"（护甲吸收 {resolution.AbsorbedByArmor}）" : string.Empty;
        string kill = resolution.Killed ? "，击杀" : resolution.Revived ? "，触发复活" : string.Empty;
        BattleCombatLog.Append(BattleLogCategory.Damage,
            $"{source} 对 {target} 造成 {resolution.HealthDamage} 点伤害{armor}{kill}");
    }

    private void OnMove(UnitMoveCompletedEvent evt)
    {
        if (evt?.Unit == null) return;
        BattleCombatLog.Append(BattleLogCategory.Move,
            $"{NameOf(evt.Unit)} 移动 ({evt.From.x},{evt.From.y}) → ({evt.To.x},{evt.To.y})");
    }

    private void OnStatus(StatusChangedEvent evt)
    {
        if (evt?.Unit == null || string.IsNullOrEmpty(evt.StatusId)) return;
        if (evt.CurrentStacks <= 0 && evt.PreviousStacks > 0)
        {
            BattleCombatLog.Append(BattleLogCategory.Status,
                $"{NameOf(evt.Unit)} 失去状态「{evt.StatusId}」");
            return;
        }

        if (evt.CurrentStacks == evt.PreviousStacks) return;
        if (evt.PreviousStacks <= 0 && evt.CurrentStacks > 0)
        {
            BattleCombatLog.Append(BattleLogCategory.Status,
                $"{NameOf(evt.Unit)} 获得「{evt.StatusId}」×{evt.CurrentStacks}");
            return;
        }

        BattleCombatLog.Append(BattleLogCategory.Status,
            $"{NameOf(evt.Unit)} 的「{evt.StatusId}」变为 {evt.CurrentStacks} 层");
    }

    private void OnCardPlayed(CardPlayedEvent evt)
    {
        if (evt?.Actor == null) return;
        string card = string.IsNullOrWhiteSpace(evt.CardName) ? evt.CardId : evt.CardName;
        string target = evt.Target != null ? $"，目标：{NameOf(evt.Target)}" : string.Empty;
        BattleCombatLog.Append(BattleLogCategory.Card,
            $"{NameOf(evt.Actor)} 打出【{card}】{target}");
    }

    private void OnHeal(HealResolvedEvent evt)
    {
        if (evt?.Target == null || evt.Amount <= 0) return;
        string source = evt.Source != null ? $"{NameOf(evt.Source)} 使 " : string.Empty;
        BattleCombatLog.Append(BattleLogCategory.Heal,
            $"{source}{NameOf(evt.Target)} 回复 {evt.Amount} 点生命");
    }

    private void OnArmor(ArmorGainedEvent evt)
    {
        if (evt?.Target == null || evt.Amount <= 0) return;
        string source = evt.Source != null ? $"{NameOf(evt.Source)} 使 " : string.Empty;
        BattleCombatLog.Append(BattleLogCategory.Armor,
            $"{source}{NameOf(evt.Target)} 获得 {evt.Amount} 点护甲");
    }

    private void OnUnitTrigger(UnitTriggerEvent evt)
    {
        if (evt?.Unit == null) return;
        if (evt.TriggerKey == ContentTriggerKeys.OnUnitTurnStart)
            BattleCombatLog.Append(BattleLogCategory.Turn, $"{NameOf(evt.Unit)} 的回合开始");
        else if (evt.TriggerKey == ContentTriggerKeys.OnUnitTurnEnd)
            BattleCombatLog.Append(BattleLogCategory.Turn, $"{NameOf(evt.Unit)} 的回合结束");
    }

    private void OnEntryAdded(BattleLogEntry entry) => AppendRow(entry, true);

    private void OnCleared()
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Row != null) Destroy(rows[i].Row);
        }

        rows.Clear();
    }

    private void RebuildFromStore()
    {
        OnCleared();
        IReadOnlyList<BattleLogEntry> store = BattleCombatLog.Entries;
        for (int i = 0; i < store.Count; i++)
            AppendRow(store[i], false);
        Canvas.ForceUpdateCanvases();
        if (scrollRect != null) scrollRect.verticalNormalizedPosition = 0f;
    }

    private void AppendRow(BattleLogEntry entry, bool scrollToBottom)
    {
        if (content == null) return;
        GameObject row = new GameObject("LogLine", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI),
            typeof(LayoutElement));
        row.transform.SetParent(content, false);
        TMP_Text text = row.GetComponent<TMP_Text>();
        text.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/SourceHanSansSC-Regular SDF") ??
                    TMP_Settings.defaultFontAsset;
        text.fontSize = 15f;
        text.color = ColorFor(entry.Category);
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        text.text = entry.Message;
        ContentSizeFitter sizeFitter = row.AddComponent<ContentSizeFitter>();
        sizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        row.GetComponent<LayoutElement>().flexibleWidth = 1f;
        rows.Add((entry.Category, row));
        bool visible = (entry.Category & filterMask) != 0;
        row.SetActive(visible);
        if (scrollToBottom)
        {
            Canvas.ForceUpdateCanvases();
            if (scrollRect != null) scrollRect.verticalNormalizedPosition = 0f;
        }
    }

    private static Color ColorFor(BattleLogCategory category)
    {
        if ((category & BattleLogCategory.Damage) != 0) return new Color(1f, 0.72f, 0.68f);
        if ((category & BattleLogCategory.Heal) != 0) return new Color(0.72f, 0.95f, 0.78f);
        if ((category & BattleLogCategory.Armor) != 0) return new Color(0.75f, 0.85f, 1f);
        if ((category & BattleLogCategory.Move) != 0) return new Color(0.85f, 0.9f, 1f);
        if ((category & BattleLogCategory.Card) != 0) return new Color(1f, 0.92f, 0.7f);
        if ((category & BattleLogCategory.Status) != 0) return new Color(0.9f, 0.8f, 1f);
        return new Color(0.9f, 0.92f, 0.95f);
    }

    private static string NameOf(Unit unit) =>
        unit == null ? "未知" : (string.IsNullOrWhiteSpace(unit.DisplayName) ? unit.name : unit.DisplayName);

    private Toggle CreateFilterToggle(Transform parent, string label, bool isOn)
    {
        GameObject obj = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(Toggle), typeof(LayoutElement));
        obj.transform.SetParent(parent, false);
        obj.GetComponent<LayoutElement>().preferredHeight = 28f;
        obj.GetComponent<LayoutElement>().flexibleWidth = 1f;
        Image background = obj.GetComponent<Image>();
        background.color = new Color(0.12f, 0.14f, 0.18f, 0.92f);
        Toggle toggle = obj.GetComponent<Toggle>();
        toggle.isOn = isOn;
        GameObject check = new GameObject("Checkmark", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        check.transform.SetParent(obj.transform, false);
        RectTransform checkRect = (RectTransform)check.transform;
        checkRect.anchorMin = Vector2.zero;
        checkRect.anchorMax = Vector2.one;
        checkRect.offsetMin = new Vector2(1f, 1f);
        checkRect.offsetMax = new Vector2(-1f, -1f);
        check.GetComponent<Image>().color = new Color(0.28f, 0.45f, 0.72f, 0.95f);
        toggle.graphic = check.GetComponent<Image>();
        toggle.targetGraphic = background;
        TMP_Text text = CreateText("Label", obj.transform, 12f);
        text.text = label;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        return toggle;
    }

    private static TMP_Text CreateText(string name, Transform parent, float size)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        TMP_Text text = obj.GetComponent<TMP_Text>();
        text.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/SourceHanSansSC-Regular SDF") ??
                    TMP_Settings.defaultFontAsset;
        text.fontSize = size;
        text.color = Color.white;
        return text;
    }

    private static Transform FindDescendant(Transform root, string objectName)
    {
        if (root == null) return null;
        if (root.name == objectName) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDescendant(root.GetChild(i), objectName);
            if (found != null) return found;
        }

        return null;
    }
}
