using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 玩家手牌、抽牌堆与弃牌堆控制器。
/// 抽空后会把弃牌洗回抽牌堆。
/// </summary>
public class HandCardSystem : MonoBehaviour
{
    [Header("卡牌资源")]
    [SerializeField] private HandCardView cardViewPrefab;
    [SerializeField] private DeckData startingDeck;
    [SerializeField] private CardEffectResolver cardEffectResolver;

    [Header("开局设置")]
    [SerializeField, Min(0)] private int startingHandSize = 5;

    [Header("手牌布局")]
    [SerializeField] private float cardWidth = 200f;
    [SerializeField] private float cardHeight = 280f;
    [SerializeField] private float preferredSpacing = 165f;

    [Header("牌堆 UI")]
    [SerializeField] private Image deckPileImage;
    [SerializeField] private Button deckPileButton;
    [SerializeField] private TMP_Text deckCountText;

    [Header("选目标")]
    [SerializeField] private BoardClickController boardClickController;
    [SerializeField] private Unit player;

    private readonly List<CardData> drawPile = new List<CardData>();
    private readonly List<CardData> discardPile = new List<CardData>();
    private readonly List<HandCardView> hand = new List<HandCardView>();

    private RectTransform handRoot;
    private GameObject detailPanel;
    private Image detailImage;
    private TMP_Text detailTitle;
    private TMP_Text detailDescription;
    private HandCardView detailOwner;
    private HandCardView targetingCard;
    private bool isInitialDealRunning;

    public int DrawPileCount => drawPile.Count;
    public int DiscardPileCount => discardPile.Count;
    public int HandCount => hand.Count;
    public bool IsTargeting => targetingCard != null;
    public event Action<CardData> CardPlayed;

    private void Awake()
    {
        handRoot = transform as RectTransform;
        ResolveCardEffectResolver();
        ResolveTargetingReferences();
        BindDeckPile();
        CreateDetailPanel();
    }

    private IEnumerator Start()
    {
        if (!TryBuildDrawPile())
        {
            UpdateDeckDisplay();
            yield break;
        }

        Shuffle(drawPile);
        UpdateDeckDisplay();
        isInitialDealRunning = true;

        int drawCount = Mathf.Min(startingHandSize, drawPile.Count);
        for (int i = 0; i < drawCount; i++)
        {
            DrawCard();
            yield return new WaitForSecondsRealtime(0.16f);
        }

        isInitialDealRunning = false;
        UpdateDeckDisplay();
    }

    public bool DrawCard()
    {
        if (drawPile.Count == 0)
        {
            RecycleDiscardPile();
        }

        if (drawPile.Count == 0 || cardViewPrefab == null || handRoot == null)
        {
            UpdateDeckDisplay();
            return false;
        }

        CardData data = drawPile[0];
        drawPile.RemoveAt(0);

        HandCardView view = Instantiate(cardViewPrefab, handRoot);
        view.name = $"HandCard_{data.cardId}_{hand.Count}";

        RectTransform cardRect = (RectTransform)view.transform;
        cardRect.anchorMin = new Vector2(0.5f, 0f);
        cardRect.anchorMax = new Vector2(0.5f, 0f);
        cardRect.pivot = new Vector2(0.5f, 0f);
        cardRect.sizeDelta = new Vector2(cardWidth, cardHeight);

        view.Initialize(this, data);
        hand.Add(view);
        LayoutHand(true);
        view.PlayDrawAnimation();
        UpdateDeckDisplay();
        return true;
    }

    public void PlayCard(HandCardView card)
    {
        if (card == null || card.Data == null || !hand.Contains(card))
        {
            return;
        }

        if (!BattleFlow.CanPlayerAct)
        {
            return;
        }

        if (targetingCard == card)
        {
            CancelTargeting();
            return;
        }

        if (IsTargeting)
        {
            CancelTargeting();
        }

        if (card.Data.RequiresTarget)
        {
            BeginTargeting(card);
            return;
        }

        TryCommitCard(card, null);
    }

    /// <summary>选中攻击范围内的敌人后真正结算。目标无效时返回 false，由棋盘点击去取消选目标。</summary>
    public bool TryConfirmTarget(Unit target)
    {
        if (!IsTargeting || targetingCard == null || targetingCard.Data == null)
        {
            return false;
        }

        ResolveTargetingReferences();
        if (player == null ||
            target == null ||
            !target.IsAlive ||
            target.Faction != UnitFaction.Enemy ||
            !targetingCard.Data.IsWithinAttackRange(player.Position, target.Position))
        {
            return false;
        }

        HandCardView card = targetingCard;
        if (!TryCommitCard(card, target))
        {
            CancelTargeting();
            return true;
        }

        return true;
    }

    public void CancelTargeting()
    {
        if (targetingCard == null)
        {
            boardClickController?.ClearAttackRange();
            return;
        }

        HandCardView card = targetingCard;
        targetingCard = null;
        card.SetAwaitingTarget(false);
        boardClickController?.ClearAttackRange();
        LayoutHand();
        Debug.Log($"取消打出 {card.Data.cardName}，卡牌已回到手牌。", this);
    }

    public void ShowCardDetails(HandCardView card)
    {
        if (card == null || card.Data == null || detailPanel == null)
        {
            return;
        }

        detailOwner = card;
        detailImage.sprite = card.Data.artwork;
        detailTitle.text = card.Data.cardName;
        string rangeLine = card.Data.RequiresTarget
            ? $"攻击范围 {card.Data.GetAttackRangeRadius() * 2 + 1}×{card.Data.GetAttackRangeRadius() * 2 + 1}\n"
            : string.Empty;
        detailDescription.text =
            $"消耗 {card.Data.actionPointCost} 点行动点\n{rangeLine}{card.Data.description}";
        detailPanel.SetActive(true);
    }

    public void HideCardDetails(HandCardView card)
    {
        if (detailOwner != card)
        {
            return;
        }

        detailOwner = null;
        if (detailPanel != null)
        {
            detailPanel.SetActive(false);
        }
    }

    private bool TryCommitCard(HandCardView card, Unit target)
    {
        CardEffectResolver resolver = ResolveCardEffectResolver();
        if (resolver == null || !resolver.TryPlay(card.Data, target))
        {
            return false;
        }

        if (targetingCard == card)
        {
            targetingCard = null;
            boardClickController?.ClearAttackRange();
        }

        hand.Remove(card);
        discardPile.Add(card.Data);
        HideCardDetails(card);
        Debug.Log($"打出了卡牌：{card.Data.cardName}");
        CardPlayed?.Invoke(card.Data);
        card.PlayCard();
        LayoutHand();
        UpdateDeckDisplay();
        return true;
    }

    private void BeginTargeting(HandCardView card)
    {
        ResolveTargetingReferences();
        CardEffectResolver resolver = ResolveCardEffectResolver();
        if (resolver == null || boardClickController == null || player == null)
        {
            Debug.LogWarning("无法进入选目标：缺少行动点、玩家或棋盘引用。", this);
            return;
        }

        if (boardClickController.CurrentActionPoints < card.Data.actionPointCost)
        {
            Debug.Log($"行动点不足，无法打出 {card.Data.cardName}。", card.Data);
            return;
        }

        boardClickController.ClearSelection();
        targetingCard = card;
        card.SetAwaitingTarget(true);
        card.transform.SetAsLastSibling();
        boardClickController.ShowAttackRange(player, card.Data.GetAttackRangeRadius());
        LayoutHand();
        ShowCardDetails(card);
        Debug.Log($"选择 {card.Data.cardName} 的攻击目标。范围内点击敌人出牌，点击其他位置取消。", this);
    }

    private void ResolveTargetingReferences()
    {
        if (boardClickController == null)
        {
            boardClickController = FindAnyObjectByType<BoardClickController>();
        }

        if (player == null)
        {
            GameObject playerObject = GameObject.Find("Player");
            player = playerObject == null ? null : playerObject.GetComponent<Unit>();
        }
    }

    public void RestoreCardOrder()
    {
        for (int i = 0; i < hand.Count; i++)
        {
            if (hand[i] != null)
            {
                hand[i].transform.SetSiblingIndex(i);
            }
        }

        targetingCard?.transform.SetAsLastSibling();
    }

    private void RecycleDiscardPile()
    {
        if (discardPile.Count == 0)
        {
            return;
        }

        drawPile.AddRange(discardPile);
        discardPile.Clear();
        Shuffle(drawPile);
        Debug.Log($"弃牌堆已洗回抽牌堆，当前 {drawPile.Count} 张。", this);
    }

    private CardEffectResolver ResolveCardEffectResolver()
    {
        if (cardEffectResolver != null)
        {
            return cardEffectResolver;
        }

        cardEffectResolver = FindAnyObjectByType<CardEffectResolver>();
        if (cardEffectResolver == null)
        {
            BattleFlow battleFlow = FindAnyObjectByType<BattleFlow>();
            GameObject host = battleFlow != null ? battleFlow.gameObject : gameObject;
            cardEffectResolver = host.AddComponent<CardEffectResolver>();
        }

        return cardEffectResolver;
    }

    private bool TryBuildDrawPile()
    {
        drawPile.Clear();
        discardPile.Clear();

        if (startingDeck == null)
        {
            Debug.LogError("请为 HandCardSystem 设置 Starting Deck。", this);
            return false;
        }

        if (cardViewPrefab == null)
        {
            Debug.LogError("请为 HandCardSystem 设置 Card View Prefab。", this);
            return false;
        }

        drawPile.AddRange(startingDeck.CreateDrawPile());
        if (drawPile.Count == 0)
        {
            Debug.LogError("Starting Deck 中没有有效卡牌。", startingDeck);
            return false;
        }

        return true;
    }

    private void BindDeckPile()
    {
        if (deckPileImage == null || deckPileButton == null || deckCountText == null)
        {
            GameObject cardControl = GameObject.Find("C_CardControl");
            Transform stack = cardControl == null ? null : cardControl.transform.Find("Stack");
            Transform pile = stack == null ? null : stack.Find("I_Stack");
            Transform number = stack == null ? null : stack.Find("Number");

            if (deckPileImage == null && pile != null)
            {
                deckPileImage = pile.GetComponent<Image>();
            }

            if (deckPileButton == null && pile != null)
            {
                deckPileButton = pile.GetComponent<Button>();
                if (deckPileButton == null)
                {
                    deckPileButton = pile.gameObject.AddComponent<Button>();
                }
            }

            if (deckCountText == null && number != null)
            {
                deckCountText = number.GetComponent<TMP_Text>();
            }
        }

        if (deckPileButton != null)
        {
            deckPileButton.targetGraphic = deckPileImage;
            deckPileButton.transition = Selectable.Transition.ColorTint;
            deckPileButton.onClick.RemoveListener(OnDeckPileClicked);
            deckPileButton.onClick.AddListener(OnDeckPileClicked);
        }

        if (deckPileButton == null || deckCountText == null)
        {
            Debug.LogWarning("未找到牌堆 I_Stack 或数量文本 Number。请在 Inspector 指定。", this);
        }
    }

    private void OnDeckPileClicked()
    {
        if (isInitialDealRunning || !BattleFlow.CanPlayerAct)
        {
            return;
        }

        CancelTargeting();

        if (!DrawCard())
        {
            Debug.Log("牌堆已经抽空，或手牌系统尚未配置。", this);
        }
    }

    private void UpdateDeckDisplay()
    {
        if (deckCountText != null)
        {
            deckCountText.text = drawPile.Count.ToString();
        }

        bool canDraw = drawPile.Count + discardPile.Count > 0 && cardViewPrefab != null;
        if (deckPileButton != null)
        {
            deckPileButton.interactable = canDraw;
        }

        if (deckPileImage != null)
        {
            deckPileImage.color = canDraw
                ? Color.white
                : new Color(0.45f, 0.45f, 0.45f, 0.7f);
        }
    }

    private void LayoutHand(bool immediate = false)
    {
        if (handRoot == null)
        {
            return;
        }

        int layoutCount = 0;
        for (int i = 0; i < hand.Count; i++)
        {
            if (hand[i] != null && hand[i] != targetingCard)
            {
                layoutCount++;
            }
        }

        if (layoutCount == 0)
        {
            return;
        }

        float availableWidth = Mathf.Max(cardWidth, handRoot.rect.width - cardWidth);
        float spacing = layoutCount == 1
            ? 0f
            : Mathf.Min(preferredSpacing, availableWidth / (layoutCount - 1));
        float totalWidth = spacing * (layoutCount - 1);
        int layoutIndex = 0;

        for (int i = 0; i < hand.Count; i++)
        {
            HandCardView card = hand[i];
            if (card == null)
            {
                continue;
            }

            if (card == targetingCard)
            {
                card.transform.SetAsLastSibling();
                continue;
            }

            card.transform.SetSiblingIndex(layoutIndex);
            card.SetLayoutPosition(
                new Vector2(-totalWidth * 0.5f + spacing * layoutIndex, 0f),
                immediate);
            layoutIndex++;
        }
    }

    private void CreateDetailPanel()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            return;
        }

        detailPanel = new GameObject(
            "P_HandCardDetails", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        detailPanel.transform.SetParent(canvas.transform, false);

        RectTransform panelRect = (RectTransform)detailPanel.transform;
        panelRect.anchorMin = new Vector2(1f, 0.5f);
        panelRect.anchorMax = new Vector2(1f, 0.5f);
        panelRect.pivot = new Vector2(1f, 0.5f);
        panelRect.anchoredPosition = new Vector2(-24f, 60f);
        panelRect.sizeDelta = new Vector2(300f, 500f);
        detailPanel.GetComponent<Image>().color =
            new Color(0.06f, 0.08f, 0.12f, 0.88f);

        detailImage = CreateImage("CardPreview", detailPanel.transform);
        RectTransform imageRect = (RectTransform)detailImage.transform;
        imageRect.anchorMin = new Vector2(0.5f, 1f);
        imageRect.anchorMax = new Vector2(0.5f, 1f);
        imageRect.pivot = new Vector2(0.5f, 1f);
        imageRect.anchoredPosition = new Vector2(0f, -18f);
        imageRect.sizeDelta = new Vector2(240f, 336f);
        detailImage.preserveAspect = true;
        detailImage.raycastTarget = false;

        TMP_FontAsset font = FindExistingFont(canvas.transform);
        detailTitle = CreateText("CardName", detailPanel.transform, font, 30f);
        RectTransform titleRect = (RectTransform)detailTitle.transform;
        titleRect.anchorMin = new Vector2(0f, 0f);
        titleRect.anchorMax = new Vector2(1f, 0f);
        titleRect.pivot = new Vector2(0.5f, 0f);
        titleRect.anchoredPosition = new Vector2(0f, 108f);
        titleRect.sizeDelta = new Vector2(-30f, 42f);
        detailTitle.alignment = TextAlignmentOptions.Center;

        detailDescription = CreateText("CardDescription", detailPanel.transform, font, 21f);
        RectTransform descriptionRect = (RectTransform)detailDescription.transform;
        descriptionRect.anchorMin = new Vector2(0f, 0f);
        descriptionRect.anchorMax = new Vector2(1f, 0f);
        descriptionRect.pivot = new Vector2(0.5f, 0f);
        descriptionRect.anchoredPosition = new Vector2(0f, 20f);
        descriptionRect.sizeDelta = new Vector2(-36f, 82f);
        detailDescription.alignment = TextAlignmentOptions.Center;
        detailDescription.textWrappingMode = TextWrappingModes.Normal;
        detailPanel.SetActive(false);
    }

    private static Image CreateImage(string name, Transform parent)
    {
        GameObject imageObject = new GameObject(
            name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        return imageObject.GetComponent<Image>();
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

    private static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int randomIndex = UnityEngine.Random.Range(0, i + 1);
            T temp = list[i];
            list[i] = list[randomIndex];
            list[randomIndex] = temp;
        }
    }

    private void OnDestroy()
    {
        if (deckPileButton != null)
        {
            deckPileButton.onClick.RemoveListener(OnDeckPileClicked);
        }
    }
}
