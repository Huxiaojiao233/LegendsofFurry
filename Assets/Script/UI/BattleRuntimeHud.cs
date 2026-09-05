using System.Collections;
using System.Collections.Generic;
using System.Text;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>将战斗运行时数据写入 BattleInterface 现有节点，并按已装备内容显示角色卡上的装备图标。</summary>
public class BattleRuntimeHud : MonoBehaviour
{
    [Header("BattleInterface 绑定")]
    [SerializeField] private TMP_Text selfNameAndClassText;
    [SerializeField] private TMP_Text careerText;
    [SerializeField] private TMP_Text selfStatusText;
    [SerializeField] private TMP_Text drawPileText;
    [SerializeField] private TMP_Text discardPileText;
    [SerializeField] private TMP_Text exhaustPileText;
    [SerializeField] private TMP_Text compactDrawPileText;
    [SerializeField] private TMP_Text turnOwnerText;
    [SerializeField] private TMP_Text roundNumberText;

    private readonly Dictionary<string, EquipmentSlotView> equipmentSlots = new Dictionary<string, EquipmentSlotView>();
    private Button classAbilityButton;
    private Unit player;
    private HandCardSystem hand;
    private ClassProfileDefinition profile;
    private RuntimeEquipmentLoadout loadout;
    private RectTransform equipmentTooltipRect;
    private TMP_Text equipmentTooltipText;
    private string hoveredSlotKey = string.Empty;
    private Image selfAvatar;
    private Image enemyAvatar;
    private TMP_Text selfHealthText;
    private TMP_Text selfArmorText;
    private TMP_Text enemyNameText;
    private TMP_Text enemyHealthText;
    private TMP_Text enemyArmorText;
    private TMP_Text enemyIntentText;
    private TMP_Text enemyStatusText;
    private RectTransform enemyPanel;
    private CanvasGroup enemyPanelGroup;
    private Vector2 enemyPanelRest;
    private Vector2 enemyPanelHidden;
    private bool enemyPanelShown;
    private bool lastCombat;
    private Coroutine enemyPanelMotion;
    private Unit boundEnemy;
    private UnitDefinition boundSelfDefinition;

    /// <summary>场景里的一枚装备图标，只在该槽有装备时显示。</summary>
    private sealed class EquipmentSlotView
    {
        public Transform Root;
        public Vector3 RestScale = Vector3.one;
        public string SlotKey = string.Empty;
        public EquipmentDefinition Item;
    }

    /// <summary>解析战斗对象、职业配置和场景 UI，并绑定装备图标。</summary>
    private void Start()
    {
        hand = FindAnyObjectByType<HandCardSystem>();
        ContentClassPassiveRuntime.TryGetSelectedProfile(out profile);
        ResolveInterfaceBindings();
        ResolveEquipmentBindings();
        CaptureEnemyPanel();
        CreateEquipmentTooltip();
        CreateClassAbilityButton();
        BindPlayerAndEquipment();
        BindSelfPortrait();
        if (GetComponent<BattleCombatLogView>() == null &&
            (FindDescendant(transform, "BattleInfomation") != null ||
             FindDescendant(transform, "BattleInformation") != null))
            gameObject.AddComponent<BattleCombatLogView>();
        lastCombat = IsInCombat();
        if (lastCombat) SetEnemyPanelVisible(true, false);
        RefreshInterface();
    }

    private void OnDestroy()
    {
        if (loadout != null) loadout.Changed -= RefreshEquipmentIcons;
    }

    /// <summary>持续同步战斗中会变化的状态、牌区数量、回合信息和按钮可用性。</summary>
    private void Update()
    {
        if (player == null) BindPlayerAndEquipment();
        BindSelfPortrait();
        bool combat = IsInCombat();
        if (combat != lastCombat)
        {
            lastCombat = combat;
            SetEnemyPanelVisible(combat, false);
        }

        RefreshInterface();
        if (combat) RefreshEnemyCombatant();
        UpdateEquipmentHover();
    }

    /// <summary>将职业、状态、牌区数量与当前回合写入 BattleInterface，法力仅供法师显示在状态栏。</summary>
    private void RefreshInterface()
    {
        if (player == null) return;
        string className = profile != null ? profile.DisplayName : GameSession.SelectedClassId;
        if (selfNameAndClassText != null) selfNameAndClassText.text = player.DisplayName;
        if (careerText != null) careerText.text = className;
        WriteUnitVitals(player, selfHealthText, selfArmorText);
        if (selfStatusText != null)
        {
            List<string> statusLines = new List<string>();
            if (profile != null && (profile.GetTraitBool("uses_mana") || profile.InitialMana > 0))
                statusLines.Add($"魔力 {player.State.Mana}/{player.State.MaxMana}");
            string summary = player.State.GetSummary();
            if (!string.IsNullOrWhiteSpace(summary) && summary != "暂无") statusLines.Add(summary);
            selfStatusText.text = statusLines.Count == 0 ? "暂无" : string.Join("\n", statusLines);
        }

        int drawCount = hand?.DrawPileCount ?? 0;
        int discardCount = hand?.DiscardPileCount ?? 0;
        int exhaustCount = hand?.ExhaustPileCount ?? 0;
        if (drawPileText != null) drawPileText.text = drawCount.ToString();
        if (discardPileText != null) discardPileText.text = discardCount.ToString();
        if (exhaustPileText != null) exhaustPileText.text = exhaustCount.ToString();
        if (compactDrawPileText != null) compactDrawPileText.text = drawCount.ToString();
        RefreshRoundInformation();
        if (classAbilityButton != null)
        {
            classAbilityButton.interactable = BattleFlow.CanPlayerAct &&
                                               !player.State.ActivatedAbilityUsedThisTurn &&
                                               (hand?.HandCount ?? 0) > 0;
        }
    }

    /// <summary>根据 BattleFlow 的权威阶段和回合数更新“我方/对方回合”及第几回合。</summary>
    private void RefreshRoundInformation()
    {
        BattleFlow flow = BattleFlow.Instance;
        if (flow == null) return;
        if (turnOwnerText != null)
        {
            turnOwnerText.text = flow.Phase switch
            {
                BattlePhase.Exploration => "探索",
                BattlePhase.PlayerTurn => "我方回合",
                BattlePhase.EnemyTurn => "对方回合",
                _ => "战斗结束"
            };
        }
        if (roundNumberText != null) roundNumberText.text = $"第{flow.RoundNumber}回合";
    }

    /// <summary>使用 Inspector 引用优先、最新 S_Battle 固定层级路径回退的方式绑定信息控件。</summary>
    private void ResolveInterfaceBindings()
    {
        selfNameAndClassText ??= FindText("P_SelfInfo/SelfDetails/T_Self_Name") ??
                                 FindNestedText("P_SelfInfo", "T_Self_Name", "T_Name");
        careerText ??= FindCareerText();
        selfStatusText ??= FindText("P_SelfInfo/S_StatusColumn/Details");
        selfAvatar ??= FindImage("P_SelfInfo/SelfDetails/P_Self_Avatar") ??
                       FindNestedImage("P_SelfInfo", "P_Self_Avatar", "I_Avatar");
        selfHealthText ??= FindText("P_SelfInfo/SelfDetails/T_Self_HP") ??
                           FindNestedText("P_SelfInfo", "T_Self_HP", "T_HP");
        selfArmorText ??= FindText("P_SelfInfo/SelfDetails/T_Self_Armor") ??
                          FindNestedText("P_SelfInfo", "T_Self_Armor", "T_Armor");
        compactDrawPileText ??= FindText("C_CardControl/Stack/Number") ??
                                FindNestedText("C_CardControl", "Number");
        drawPileText ??= FindText("P_EnemyInfo/StackInfo/Details/N_Stack") ??
                         FindNestedText("C_CardControl", "N_Stack", "Number") ??
                         compactDrawPileText;
        discardPileText ??= FindText("P_EnemyInfo/StackInfo/Details/N_Fold") ??
                            FindText("C_CardControl/Stack/N_Fold") ??
                            FindNestedText("C_CardControl", "N_Fold");
        exhaustPileText ??= FindText("P_EnemyInfo/StackInfo/Details/N_Loss") ??
                            FindNestedText("C_CardControl", "N_Loss");
        turnOwnerText ??= FindText("I_RoundInfo/RoundObj/Title") ??
                          FindNestedText("I_RoundInfo", "RoundObj");
        roundNumberText ??= FindText("I_RoundInfo/RoundNumber/Title") ??
                            FindNestedText("I_RoundInfo", "RoundNumber");
        BindEnemyPanelWidgets();
        if (selfNameAndClassText == null || selfStatusText == null || turnOwnerText == null || roundNumberText == null)
            Debug.LogError("BattleInterface 信息节点绑定不完整，请检查最新 S_Battle 的层级。", this);
        if (careerText == null) Debug.LogWarning("没有找到 P_Carrer 内的职业文字，请保存该节点后重新进入战斗。", this);
    }

    /// <summary>查找 P_Carrer 自身或其 Info、Details、Text、Title 子节点中的职业文字。</summary>
    private TMP_Text FindCareerText()
    {
        Transform container = transform.Find("P_SelfInfo/SelfDeatils/P_Carrer") ??
                              transform.Find("P_SelfInfo/SelfDetails/P_Carrer") ??
                              FindDescendant(transform, "P_Carrer");
        if (container == null) return null;
        TMP_Text directText = container.GetComponent<TMP_Text>();
        if (directText != null) return directText;
        string[] preferredChildren = { "Info", "Details", "Text", "Title" };
        foreach (string childName in preferredChildren)
        {
            TMP_Text childText = container.Find(childName)?.GetComponent<TMP_Text>();
            if (childText != null) return childText;
        }
        return container.GetComponentInChildren<TMP_Text>(true);
    }

    /// <summary>在 BattleInterface 层级中递归查找指定名称的对象，兼容 P_Carrer 后续调整位置。</summary>
    private static Transform FindDescendant(Transform root, string objectName)
    {
        if (root == null) return null;
        if (root.name == objectName) return root;
        for (int index = 0; index < root.childCount; index++)
        {
            Transform found = FindDescendant(root.GetChild(index), objectName);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>优先绑角色卡 SelfDetails 上那一排图标；找不到再回退到旧的 EquipmentColumn。</summary>
    private void ResolveEquipmentBindings()
    {
        BindEquipmentSlot(ContentEquipmentSlotKeys.Weapon, "Weapon");
        BindEquipmentSlot(ContentEquipmentSlotKeys.Offhand, "Armor1");
        BindEquipmentSlot(ContentEquipmentSlotKeys.Accessory, "Accessories");
        BindEquipmentSlot(ContentEquipmentSlotKeys.Armor, "Armor2");
        BindEquipmentSlot(ContentEquipmentSlotKeys.Treasure, "Treasure");
        BindEquipmentSlot(ContentEquipmentSlotKeys.Boot, "Boot");
    }

    private void BindEquipmentSlot(string slotKey, string objectName)
    {
        Transform slotRoot = FindEquipmentSlotRoot(objectName);
        if (slotRoot == null) return;
        EquipmentSlotView slot = new EquipmentSlotView
        {
            Root = slotRoot,
            RestScale = slotRoot.localScale.sqrMagnitude < 0.0001f ? Vector3.one : slotRoot.localScale,
            SlotKey = slotKey
        };
        equipmentSlots[slotKey] = slot;
        SetSlotVisible(slot, false);
    }

    private Transform FindEquipmentSlotRoot(string objectName)
    {
        Transform selfInfo = transform.Find("P_SelfInfo");
        Transform details = selfInfo != null
            ? selfInfo.Find("SelfDetails") ?? selfInfo.Find("SelfDeatils")
            : null;
        Transform slot = details != null ? details.Find(objectName) : null;
        if (slot != null) return slot;
        slot = selfInfo != null ? FindDescendant(selfInfo, objectName) : null;
        if (slot != null) return slot;
        return transform.Find("EquipmentColumn/ControlPanel/" + objectName) ??
               FindDescendant(transform, objectName);
    }

    private void BindPlayerAndEquipment()
    {
        player = BattleUnits.PrimaryAlly;
        if (player == null) return;
        RuntimeEquipmentLoadout next = player.GetComponent<RuntimeEquipmentLoadout>();
        if (next == loadout) return;
        if (loadout != null) loadout.Changed -= RefreshEquipmentIcons;
        loadout = next;
        if (loadout != null) loadout.Changed += RefreshEquipmentIcons;
        RefreshEquipmentIcons();
        if (selfHealthText != null || selfArmorText != null)
            player.BindCombatUI(selfHealthText, selfArmorText);
    }

    /// <summary>有装备才显示对应图标；空槽隐藏。</summary>
    private void RefreshEquipmentIcons()
    {
        foreach (EquipmentSlotView slot in equipmentSlots.Values)
        {
            EquipmentInstance item = null;
            bool equipped = loadout != null && loadout.TryGet(slot.SlotKey, out item) &&
                            item != null && item.Definition != null;
            slot.Item = equipped ? item.Definition : null;
            SetSlotVisible(slot, equipped);
        }

        if (!string.IsNullOrEmpty(hoveredSlotKey) &&
            (!equipmentSlots.TryGetValue(hoveredSlotKey, out EquipmentSlotView hovered) || hovered.Item == null))
            HideEquipmentTooltip();
    }

    private static void SetSlotVisible(EquipmentSlotView slot, bool visible)
    {
        if (slot.Root == null) return;
        slot.Root.localScale = visible ? slot.RestScale : Vector3.zero;
    }

    private void UpdateEquipmentHover()
    {
        if (equipmentSlots.Count == 0 || Mouse.current == null)
        {
            HideEquipmentTooltip();
            return;
        }

        Canvas canvas = GetComponentInParent<Canvas>();
        Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;
        Vector2 screen = Mouse.current.position.ReadValue();
        string hitKey = null;
        foreach (KeyValuePair<string, EquipmentSlotView> pair in equipmentSlots)
        {
            if (pair.Value.Item == null || pair.Value.Root is not RectTransform rect) continue;
            if (!RectTransformUtility.RectangleContainsScreenPoint(rect, screen, uiCamera)) continue;
            hitKey = pair.Key;
            break;
        }

        if (hitKey == null)
        {
            HideEquipmentTooltip();
            return;
        }

        if (hitKey != hoveredSlotKey) ShowEquipmentTooltip(hitKey);
        else FollowEquipmentTooltip();
    }

    internal void ShowEquipmentTooltip(string slotKey)
    {
        if (!equipmentSlots.TryGetValue(slotKey, out EquipmentSlotView slot) || slot.Item == null ||
            equipmentTooltipText == null)
            return;
        hoveredSlotKey = slotKey;
        equipmentTooltipText.text = FormatEquipmentTooltip(slot.Item);
        equipmentTooltipRect.gameObject.SetActive(true);
        FollowEquipmentTooltip();
    }

    internal void HideEquipmentTooltip()
    {
        hoveredSlotKey = string.Empty;
        if (equipmentTooltipRect != null) equipmentTooltipRect.gameObject.SetActive(false);
    }

    private void FollowEquipmentTooltip()
    {
        if (equipmentTooltipRect == null || Mouse.current == null) return;
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        RectTransform canvasRect = (RectTransform)canvas.transform;
        Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 screen = Mouse.current.position.ReadValue();
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, uiCamera, out Vector2 local))
            return;
        equipmentTooltipRect.SetAsLastSibling();
        equipmentTooltipRect.anchoredPosition = local + new Vector2(18f, 24f);
    }

    private void CreateEquipmentTooltip()
    {
        GameObject panel = new GameObject(
            "EquipmentTooltip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panel.transform.SetParent(transform, false);
        equipmentTooltipRect = (RectTransform)panel.transform;
        equipmentTooltipRect.anchorMin = equipmentTooltipRect.anchorMax = new Vector2(0.5f, 0.5f);
        equipmentTooltipRect.pivot = new Vector2(0f, 0f);
        equipmentTooltipRect.sizeDelta = new Vector2(280f, 140f);
        Image background = panel.GetComponent<Image>();
        // 无 Sprite 时 Image 不绘制，深色底+深色字会叠在暗 UI 上完全看不清。
        background.sprite = WhiteUiSprite();
        background.color = new Color(0.96f, 0.96f, 0.97f, 0.98f);
        background.raycastTarget = false;
        equipmentTooltipText = CreateText("Body", panel.transform, 18f);
        equipmentTooltipText.color = new Color(0.12f, 0.12f, 0.14f, 1f);
        equipmentTooltipText.richText = true;
        equipmentTooltipText.alignment = TextAlignmentOptions.TopLeft;
        equipmentTooltipText.rectTransform.anchorMin = Vector2.zero;
        equipmentTooltipText.rectTransform.anchorMax = Vector2.one;
        equipmentTooltipText.rectTransform.offsetMin = new Vector2(12f, 10f);
        equipmentTooltipText.rectTransform.offsetMax = new Vector2(-12f, -10f);
        panel.SetActive(false);
    }

    private static Sprite whiteUiSprite;

    private static Sprite WhiteUiSprite()
    {
        if (whiteUiSprite != null) return whiteUiSprite;
        Texture2D texture = Texture2D.whiteTexture;
        whiteUiSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f);
        whiteUiSprite.name = "BattleHudWhiteUi";
        return whiteUiSprite;
    }

    private static string FormatEquipmentTooltip(EquipmentDefinition item)
    {
        StringBuilder text = new StringBuilder();
        text.Append("<b>").Append(item.DisplayName).Append("</b>\n");
        text.Append("槽位：").Append(SlotDisplayName(item.SlotKey)).Append('\n');
        text.Append("加成：").Append(FormatEquipmentBonus(item)).Append('\n');
        string description = string.IsNullOrWhiteSpace(item.Description) ? "暂无介绍。" : item.Description.Trim();
        text.Append("介绍：").Append(description);
        return text.ToString();
    }

    private static string FormatEquipmentBonus(EquipmentDefinition item)
    {
        if (item.Tags != null && item.Tags.Count > 0)
            return string.Join("、", item.Tags);
        string poolName = item.CardPoolId;
        if (ContentRuntime.IsLoaded &&
            ContentRuntime.Registry.TryGetCardPool(item.CardPoolId, out CardPoolDefinition pool) &&
            !string.IsNullOrWhiteSpace(pool.DisplayName))
            poolName = pool.DisplayName;
        if (ContentEquipmentSlotKeys.UsesAppraisal(item.SlotKey))
            return string.IsNullOrWhiteSpace(poolName) ? "每场战斗鉴定一张专属牌" : "每场鉴定「" + poolName + "」";
        return string.IsNullOrWhiteSpace(poolName) ? "开局抽取从属卡组" : "开局抽取「" + poolName + "」卡组";
    }

    private static string SlotDisplayName(string slotKey)
    {
        return slotKey switch
        {
            ContentEquipmentSlotKeys.Weapon => "主手",
            ContentEquipmentSlotKeys.Offhand => "副手",
            ContentEquipmentSlotKeys.Armor => "护甲",
            ContentEquipmentSlotKeys.Boot => "鞋子",
            ContentEquipmentSlotKeys.Treasure => "宝物",
            ContentEquipmentSlotKeys.Accessory => "饰品",
            _ => slotKey
        };
    }

    /// <summary>Creates the selected class's authored activated-ability button.</summary>
    private void CreateClassAbilityButton()
    {
        string abilityId = profile?.GetTraitString("activated_ability_id");
        if (string.IsNullOrWhiteSpace(abilityId)) return;
        GameObject buttonObject = new GameObject(
            "ClassAbility_" + abilityId, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        Transform leftPanel = transform.Find("P_SelfInfo") ?? transform;
        buttonObject.transform.SetParent(leftPanel, false);
        RectTransform buttonRect = (RectTransform)buttonObject.transform;
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0f);
        buttonRect.pivot = new Vector2(0.5f, 0f);
        buttonRect.anchoredPosition = new Vector2(0f, 20f);
        buttonRect.sizeDelta = new Vector2(300f, 48f);
        buttonRect.SetAsLastSibling();
        buttonObject.GetComponent<Image>().color = new Color(0.48f, 0.35f, 0.12f, 0.95f);
        classAbilityButton = buttonObject.GetComponent<Button>();
        classAbilityButton.onClick.AddListener(() => hand?.BeginActivatedClassAbility());
        TMP_Text label = CreateText("Label", buttonObject.transform, 18f);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        label.text = profile.GetTraitString("activated_ability_description",
            profile.GetTraitString("activated_ability_name", abilityId));
        label.alignment = TextAlignmentOptions.Center;
    }

    /// <summary>在当前 BattleInterface 根节点下按相对路径查找 TextMeshPro 文字组件。</summary>
    private TMP_Text FindText(string relativePath)
    {
        return transform.Find(relativePath)?.GetComponent<TMP_Text>();
    }

    private Image FindImage(string relativePath)
    {
        return transform.Find(relativePath)?.GetComponent<Image>();
    }

    private TMP_Text FindNestedText(string rootName, params string[] names)
    {
        Transform root = transform.Find(rootName) ?? FindDescendant(transform, rootName);
        if (root == null) return null;
        for (int i = 0; i < names.Length; i++)
        {
            Transform found = FindDescendant(root, names[i]);
            TMP_Text text = found != null ? found.GetComponent<TMP_Text>() : null;
            if (text != null) return text;
        }

        return null;
    }

    private Image FindNestedImage(string rootName, params string[] names)
    {
        Transform root = transform.Find(rootName) ?? FindDescendant(transform, rootName);
        if (root == null) return null;
        for (int i = 0; i < names.Length; i++)
        {
            Transform found = FindDescendant(root, names[i]);
            Image image = found != null ? found.GetComponent<Image>() : null;
            if (image != null) return image;
        }

        return null;
    }

    private void BindSelfPortrait()
    {
        if (selfAvatar == null) return;
        UnitDefinition definition = player != null ? player.Definition : null;
        if (definition == null && ContentRuntime.IsLoaded)
            ContentRuntime.Registry.TryGetUnit(ContentRuntime.Registry.GameSettings.PlayerUnitId, out definition);
        if (definition == boundSelfDefinition && selfAvatar.sprite != null) return;
        boundSelfDefinition = definition;
        Sprite portrait = TokenVisualRuntime.LoadPortraitSprite(definition);
        if (portrait == null) return;
        selfAvatar.sprite = portrait;
        selfAvatar.preserveAspect = true;
        selfAvatar.color = Color.white;
    }

    private static bool IsInCombat()
    {
        if (WorldPlaySession.Instance != null) return WorldPlaySession.Instance.IsCombat;
        BattleFlow flow = BattleFlow.Instance;
        return flow != null && flow.Phase != BattlePhase.Exploration;
    }

    private void CaptureEnemyPanel()
    {
        Transform found = transform.Find("P_EnemyInfo") ?? FindDescendant(transform, "P_EnemyInfo");
        enemyPanel = found as RectTransform;
        if (enemyPanel == null) return;
        BindEnemyPanelWidgets();
        enemyPanelRest = enemyPanel.anchoredPosition;
        float width = Mathf.Max(enemyPanel.rect.width, Mathf.Abs(enemyPanel.sizeDelta.x), 420f);
        enemyPanelHidden = enemyPanelRest + new Vector2(width + 64f, 0f);
        enemyPanelGroup = enemyPanel.GetComponent<CanvasGroup>();
        enemyPanel.anchoredPosition = enemyPanelHidden;
        enemyPanelShown = false;
        SetEnemyPanelInteractable(false);
    }

    private void BindEnemyPanelWidgets()
    {
        if (enemyPanel == null) return;
        enemyAvatar ??= FindChildImage(enemyPanel, "P_Enemy_Avatar", "I_Avatar");
        enemyNameText ??= FindChildText(enemyPanel, "T_Enemy_Name", "T_Name");
        enemyHealthText ??= FindChildText(enemyPanel, "T_Enemy_HP", "T_HP");
        enemyArmorText ??= FindChildText(enemyPanel, "T_Enemy_Armor", "T_Armor");
        enemyIntentText ??= FindChildText(enemyPanel, "T_Want");
        enemyStatusText ??= FindChildText(enemyPanel, "T_Status");
    }

    private static Image FindChildImage(Transform root, params string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            Transform found = FindDescendant(root, names[i]);
            Image image = found != null ? found.GetComponent<Image>() : null;
            if (image != null) return image;
        }

        return null;
    }

    private static TMP_Text FindChildText(Transform root, params string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            Transform found = FindDescendant(root, names[i]);
            TMP_Text text = found != null ? found.GetComponent<TMP_Text>() : null;
            if (text != null) return text;
        }

        return null;
    }

    private void SetEnemyPanelVisible(bool visible, bool instant)
    {
        if (enemyPanel == null) return;
        if (visible == enemyPanelShown && enemyPanelMotion == null) return;
        if (visible) RefreshEnemyCombatant();
        Vector2 target = visible ? enemyPanelRest : enemyPanelHidden;
        if (instant)
        {
            if (enemyPanelMotion != null)
            {
                StopCoroutine(enemyPanelMotion);
                enemyPanelMotion = null;
            }

            enemyPanel.anchoredPosition = target;
            enemyPanelShown = visible;
            SetEnemyPanelInteractable(visible);
            return;
        }

        if (enemyPanelMotion != null) StopCoroutine(enemyPanelMotion);
        enemyPanelMotion = StartCoroutine(SlideEnemyPanel(target, visible));
    }

    private IEnumerator SlideEnemyPanel(Vector2 target, bool visible)
    {
        enemyPanel.gameObject.SetActive(true);
        if (visible) SetEnemyPanelInteractable(true);
        Vector2 start = enemyPanel.anchoredPosition;
        const float duration = 0.4f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = 1f - (1f - t) * (1f - t);
            enemyPanel.anchoredPosition = Vector2.LerpUnclamped(start, target, t);
            yield return null;
        }

        enemyPanel.anchoredPosition = target;
        enemyPanelShown = visible;
        SetEnemyPanelInteractable(visible);
        enemyPanelMotion = null;
    }

    private void SetEnemyPanelInteractable(bool visible)
    {
        if (enemyPanelGroup == null) return;
        enemyPanelGroup.blocksRaycasts = visible;
        enemyPanelGroup.interactable = visible;
    }

    private void RefreshEnemyCombatant()
    {
        Unit enemy = BattleUnits.PrimaryEnemy;
        if (enemy != boundEnemy)
        {
            boundEnemy = enemy;
            if (enemy == null)
            {
                if (enemyNameText != null) enemyNameText.text = string.Empty;
                WriteUnitVitals(null, enemyHealthText, enemyArmorText);
                WriteEnemyIntent(null);
                WriteEnemyStatus(null);
                return;
            }

            Sprite portrait = TokenVisualRuntime.LoadPortraitSprite(enemy.Definition);
            if (enemyAvatar != null && portrait != null)
            {
                enemyAvatar.sprite = portrait;
                enemyAvatar.preserveAspect = true;
                enemyAvatar.color = Color.white;
            }

            enemy.BindCombatUI(enemyHealthText, enemyArmorText);
        }

        if (enemy == null)
        {
            if (enemyNameText != null) enemyNameText.text = string.Empty;
            WriteUnitVitals(null, enemyHealthText, enemyArmorText);
            WriteEnemyIntent(null);
            WriteEnemyStatus(null);
            return;
        }

        if (enemyNameText != null) enemyNameText.text = enemy.DisplayName;
        WriteEnemyIntent(enemy);
        WriteEnemyStatus(enemy);
        WriteUnitVitals(enemy, enemyHealthText, enemyArmorText);
    }

    private void WriteEnemyIntent(Unit enemy)
    {
        if (enemyIntentText == null) return;
        if (enemy == null)
        {
            enemyIntentText.text = string.Empty;
            return;
        }

        UtilityAiController ai = enemy.GetComponent<UtilityAiController>();
        enemyIntentText.text = ai != null && ai.CachedIntent.HasValue
            ? ai.CachedIntent.Label
            : "暂无";
    }

    private void WriteEnemyStatus(Unit enemy)
    {
        if (enemyStatusText == null) return;
        if (enemy == null)
        {
            enemyStatusText.text = string.Empty;
            return;
        }

        string summary = enemy.State != null ? enemy.State.GetSummary() : string.Empty;
        enemyStatusText.text = string.IsNullOrWhiteSpace(summary) || summary == "暂无"
            ? "暂无"
            : summary;
    }

    private static void WriteUnitVitals(Unit unit, TMP_Text health, TMP_Text armor)
    {
        if (health != null)
            health.text = unit == null ? "0/0" : $"{unit.CurrentHealth}/{unit.MaxHealth}";
        if (armor != null)
            armor.text = unit == null ? "0" : unit.Armor.ToString();
    }

    /// <summary>创建职业能力按钮内部的 TextMeshPro 文字并应用统一字体样式。</summary>
    private static TMP_Text CreateText(string name, Transform parent, float size)
    {
        GameObject obj = new GameObject(
            name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        TMP_Text text = obj.GetComponent<TMP_Text>();
        text.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/SourceHanSansSC-Regular SDF") ??
                    TMP_Settings.defaultFontAsset;
        text.fontSize = size;
        text.color = Color.white;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        return text;
    }
}
