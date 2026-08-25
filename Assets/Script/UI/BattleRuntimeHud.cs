using System.Collections.Generic;
using System.Linq;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>将战斗运行时数据写入 BattleInterface 现有节点，并管理本局装备栏的显示与卸下交互。</summary>
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

    /// <summary>缓存一个装备槽的按钮、名称文本和当前装备池 ID。</summary>
    private sealed class EquipmentSlotView
    {
        public Button Button;
        public TMP_Text Info;
        public string PoolId = string.Empty;
    }

    /// <summary>解析战斗对象、职业配置和场景 UI，并绑定装备卸下事件。</summary>
    private void Start()
    {
        player = GameObject.Find("Player")?.GetComponent<Unit>();
        hand = FindAnyObjectByType<HandCardSystem>();
        ContentClassPassiveRuntime.TryGetSelectedProfile(out profile);
        ResolveInterfaceBindings();
        ResolveEquipmentBindings();
        ApplyInitialEquipment();
        CreateClassAbilityButton();
        RefreshInterface();
    }

    /// <summary>持续同步战斗中会变化的状态、牌区数量、回合信息和按钮可用性。</summary>
    private void Update()
    {
        RefreshInterface();
    }

    /// <summary>将职业、状态、牌区数量与当前回合写入 BattleInterface，法力仅供法师显示在状态栏。</summary>
    private void RefreshInterface()
    {
        if (player == null) return;
        string className = profile != null ? profile.DisplayName : GameSession.SelectedClass.ToString();
        if (selfNameAndClassText != null) selfNameAndClassText.text = "鸿叶";
        if (careerText != null) careerText.text = className;
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
                                               !player.State.PriestHealUsedThisTurn &&
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
        selfNameAndClassText ??= FindText("P_SelfInfo/SelfDetails/T_Self_Name");
        careerText ??= FindCareerText();
        selfStatusText ??= FindText("P_SelfInfo/S_StatusColumn/Details");
        drawPileText ??= FindText("P_EnemyInfo/StackInfo/Details/N_Stack");
        discardPileText ??= FindText("P_EnemyInfo/StackInfo/Details/N_Fold");
        exhaustPileText ??= FindText("P_EnemyInfo/StackInfo/Details/N_Loss");
        compactDrawPileText ??= FindText("C_CardControl/Stack/Number");
        turnOwnerText ??= FindText("I_RoundInfo/RoundObj/Title");
        roundNumberText ??= FindText("I_RoundInfo/RoundNumber/Title");
        if (selfNameAndClassText == null || selfStatusText == null || drawPileText == null ||
            discardPileText == null || exhaustPileText == null || turnOwnerText == null || roundNumberText == null)
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

    /// <summary>绑定最新场景中的六个装备槽，并为每个槽注册点击卸下行为。</summary>
    private void ResolveEquipmentBindings()
    {
        Transform root = transform.Find("P_SelfInfo/EquipmentColumn/ControlPanel");
        BindEquipmentSlot("weapon", root?.Find("Weapon"));
        BindEquipmentSlot("offhand", root?.Find("Armor1"));
        BindEquipmentSlot("accessory", root?.Find("Accessories"));
        BindEquipmentSlot("armor", root?.Find("Armor2"));
        BindEquipmentSlot("treasure", root?.Find("Treasure"));
        BindEquipmentSlot("boot", root?.Find("Boot"));
    }

    /// <summary>登记一个场景装备槽；点击已有装备时只卸下该槽，不影响其他槽。</summary>
    private void BindEquipmentSlot(string slotKey, Transform slotRoot)
    {
        if (slotRoot == null) return;
        EquipmentSlotView slot = new EquipmentSlotView
        {
            Button = slotRoot.GetComponent<Button>(),
            Info = slotRoot.Find("Info")?.GetComponent<TMP_Text>()
        };
        equipmentSlots[slotKey] = slot;
        if (slot.Button != null) slot.Button.onClick.AddListener(() => Unequip(slotKey));
    }

    /// <summary>从职业数据特性填充初始装备；主手缺省时使用职业牌库配方中的首个有效卡池。</summary>
    private void ApplyInitialEquipment()
    {
        if (profile == null) return;
        string fallbackWeapon = profile.DeckRecipe.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.PoolId))?.PoolId ?? string.Empty;
        Equip("weapon", profile.GetTraitString("equipment_weapon_pool", fallbackWeapon));
        Equip("offhand", profile.GetTraitString("equipment_offhand_pool"));
        Equip("accessory", profile.GetTraitString("equipment_accessory_pool"));
        Equip("armor", profile.GetTraitString("equipment_armor_pool"));
        Equip("treasure", profile.GetTraitString("equipment_treasure_pool"));
        Equip("boot", profile.GetTraitString("equipment_boot_pool"));
    }

    /// <summary>把卡池 ID 装入指定槽位，并使用内容数据库中的卡池显示名刷新界面。</summary>
    private void Equip(string slotKey, string poolId)
    {
        if (!equipmentSlots.TryGetValue(slotKey, out EquipmentSlotView slot)) return;
        slot.PoolId = poolId ?? string.Empty;
        RefreshEquipmentSlot(slot);
    }

    /// <summary>响应装备槽点击；已有装备变为未装备状态，空槽点击不会产生副作用。</summary>
    private void Unequip(string slotKey)
    {
        if (!equipmentSlots.TryGetValue(slotKey, out EquipmentSlotView slot) || string.IsNullOrEmpty(slot.PoolId)) return;
        slot.PoolId = string.Empty;
        RefreshEquipmentSlot(slot);
    }

    /// <summary>根据槽内卡池 ID 刷新策划名称；无装备或失效引用均显示“未装备”。</summary>
    private static void RefreshEquipmentSlot(EquipmentSlotView slot)
    {
        if (slot.Info == null) return;
        slot.Info.text = !string.IsNullOrEmpty(slot.PoolId) && ContentRuntime.IsLoaded &&
                         ContentRuntime.Registry.TryGetCardPool(slot.PoolId, out CardPoolDefinition pool)
            ? pool.DisplayName
            : "未装备";
    }

    /// <summary>牧师职业启用时创建祈福按钮；治疗目标也改由棋盘棋子选择。</summary>
    private void CreateClassAbilityButton()
    {
        if (!ContentClassPassiveRuntime.GetSelectedTraitBool("card_sacrifice_heal")) return;
        GameObject buttonObject = new GameObject(
            "PriestHeal", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
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
        classAbilityButton.onClick.AddListener(() => hand?.BeginPriestSacrifice());
        TMP_Text label = CreateText("Label", buttonObject.transform, 18f);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        label.text = "祈福：消耗1张牌，治疗5";
        label.alignment = TextAlignmentOptions.Center;
    }

    /// <summary>在当前 BattleInterface 根节点下按相对路径查找 TextMeshPro 文字组件。</summary>
    private TMP_Text FindText(string relativePath)
    {
        return transform.Find(relativePath)?.GetComponent<TMP_Text>();
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
