using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BattleRuntimeHud : MonoBehaviour
{
    private TMP_Text info;
    private Button priestButton;
    private Unit player;
    private HandCardSystem hand;

    private void Start()
    {
        player = GameObject.Find("Player")?.GetComponent<Unit>();
        hand = FindAnyObjectByType<HandCardSystem>();
        Build();
    }

    private void Update()
    {
        if (player == null || info == null) return;
        HeroClassProfile profile = ClassCatalog.Get(GameSession.SelectedClass);
        info.text = $"{profile.DisplayName}　魔力 {player.State.Mana}/{player.State.MaxMana}\n状态：{player.State.GetSummary()}\n牌堆 {hand?.DrawPileCount ?? 0}　弃牌 {hand?.DiscardPileCount ?? 0}　消耗 {hand?.ExhaustPileCount ?? 0}";
        if (priestButton != null) priestButton.interactable = BattleFlow.CanPlayerAct && !player.State.PriestHealUsedThisTurn && (hand?.HandCount ?? 0) > 0;
    }

    private void Build()
    {
        GameObject panel = new GameObject("RuntimeBattleInfo", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panel.transform.SetParent(transform, false);
        RectTransform rect = (RectTransform)panel.transform;
        rect.anchorMin = new Vector2(0f, 1f); rect.anchorMax = new Vector2(0f, 1f); rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(18f, -18f); rect.sizeDelta = new Vector2(520f, 125f);
        panel.GetComponent<Image>().color = new Color(0.03f, 0.04f, 0.07f, 0.84f);
        info = CreateText("Info", panel.transform, 18f);
        info.rectTransform.anchorMin = Vector2.zero; info.rectTransform.anchorMax = Vector2.one;
        info.rectTransform.offsetMin = new Vector2(14f, 8f); info.rectTransform.offsetMax = new Vector2(-14f, -8f);

        if (GameSession.SelectedClass != HeroClass.Priest) return;
        GameObject buttonObject = new GameObject("PriestHeal", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(transform, false);
        RectTransform buttonRect = (RectTransform)buttonObject.transform;
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0f, 1f); buttonRect.pivot = new Vector2(0f, 1f);
        buttonRect.anchoredPosition = new Vector2(18f, -150f); buttonRect.sizeDelta = new Vector2(300f, 48f);
        buttonObject.GetComponent<Image>().color = new Color(0.48f, 0.35f, 0.12f, 0.95f);
        priestButton = buttonObject.GetComponent<Button>();
        priestButton.onClick.AddListener(() => hand?.BeginPriestSacrifice());
        TMP_Text label = CreateText("Label", buttonObject.transform, 18f);
        label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one; label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        label.text = "祈福：消耗1张牌，治疗5"; label.alignment = TextAlignmentOptions.Center;
    }

    private static TMP_Text CreateText(string name, Transform parent, float size)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        TMP_Text text = obj.GetComponent<TMP_Text>();
        text.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/SourceHanSansSC-Regular SDF") ?? TMP_Settings.defaultFontAsset;
        text.fontSize = size; text.color = Color.white; text.textWrappingMode = TextWrappingModes.Normal; text.raycastTarget = false;
        return text;
    }
}
