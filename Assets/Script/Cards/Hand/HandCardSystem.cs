using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HandCardSystem : MonoBehaviour
{
    private const int HandLimit = 10;

    [Header("卡牌资源")]
    [SerializeField] private HandCardView cardViewPrefab;
    [SerializeField] private DeckData startingDeck;
    [SerializeField] private CardEffectResolver cardEffectResolver;
    [SerializeField, Min(0)] private int startingHandSize = 5;
    [SerializeField] private float cardWidth = 200f;
    [SerializeField] private float cardHeight = 280f;
    [SerializeField] private float preferredSpacing = 165f;
    [SerializeField] private Image deckPileImage;
    [SerializeField] private Button deckPileButton;
    [SerializeField] private TMP_Text deckCountText;
    [SerializeField] private BoardClickController boardClickController;
    [SerializeField] private Unit player;

    private readonly List<CardInstance> totalDeck = new List<CardInstance>();
    private readonly List<CardInstance> drawPile = new List<CardInstance>();
    private readonly List<CardInstance> discardPile = new List<CardInstance>();
    private readonly List<CardInstance> exhaustPile = new List<CardInstance>();
    private readonly List<HandCardView> hand = new List<HandCardView>();
    private readonly Queue<CardInstance> forcedQueue = new Queue<CardInstance>();
    private readonly Queue<CardInstance> pendingCardAdds = new Queue<CardInstance>();

    private RectTransform handRoot;
    private HandCardView targetingCard;
    private HandCardView forcedCard;
    private GameObject detailPanel;
    private TMP_Text detailTitle;
    private TMP_Text detailDescription;
    private HandCardView detailOwner;
    private GameObject modal;
    private int pendingMandatoryDraws;
    private bool priestSacrificeMode;
    private bool forcedPresentationScheduled;

    public int DrawPileCount => drawPile.Count;
    public int DiscardPileCount => discardPile.Count;
    public int ExhaustPileCount => exhaustPile.Count;
    public int HandCount => hand.Count;
    public bool IsTargeting => targetingCard != null;
    public bool HasBlockingChoice => modal != null || forcedCard != null;
    public event Action<CardData> CardPlayed;

    private void Awake()
    {
        handRoot = transform as RectTransform;
        ResolveReferences();
        BindDeckPile();
        CreateDetailPanel();
    }

    private IEnumerator Start()
    {
        yield return null;
        BuildStartingDeck();
        Shuffle(drawPile);
        DrawCards(startingHandSize, true);
        UpdateDeckDisplay();
    }

    public void DrawCards(int count, bool mandatory)
    {
        int remaining = Mathf.Max(0, count);
        while (remaining > 0)
        {
            if (hand.Count >= HandLimit)
            {
                if (mandatory)
                {
                    pendingMandatoryDraws += remaining;
                    ShowMandatoryDiscardChoice();
                }
                break;
            }

            if (!DrawOne()) break;
            remaining--;
        }
    }

    public bool DrawCard()
    {
        if (hand.Count >= HandLimit) return false;
        return DrawOne();
    }

    public void PlayCard(HandCardView view)
    {
        if (view == null || view.Instance?.Data == null || !hand.Contains(view)) return;
        if (!BattleFlow.CanPlayerAct || modal != null) return;

        if (priestSacrificeMode)
        {
            SacrificeForPriestHeal(view);
            return;
        }

        if (forcedCard != null && view != forcedCard) return;
        if (targetingCard == view) { CancelTargeting(); return; }
        if (IsTargeting) CancelTargeting();

        if (view.Data.unplayable)
        {
            Debug.Log($"【{view.Data.cardName}】无法被打出。", this);
            return;
        }
        if (!ResolveResolver().CanAfford(view.Instance))
        {
            Debug.Log($"费用不足或当前无法打出【{view.Data.cardName}】。", this);
            return;
        }

        if (view.Data.RequiresTarget) BeginTargeting(view);
        else TryCommitCard(view, null, null, null);
    }

    public bool TryConfirmTarget(Unit target, BoardCell cell)
    {
        if (targetingCard == null) return false;
        CardData data = targetingCard.Data;
        Vector2Int? direction = null;
        if (data.targetMode == CardTargetMode.Direction)
        {
            if (cell == null || player == null) return false;
            Vector2Int delta = cell.Coordinate - player.Position;
            if (delta == Vector2Int.zero) return false;
            direction = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)
                ? new Vector2Int((int)Mathf.Sign(delta.x), 0)
                : new Vector2Int(0, (int)Mathf.Sign(delta.y));
        }

        HandCardView view = targetingCard;
        if (!TryCommitCard(view, target, cell, direction)) return false;
        return true;
    }

    public void CancelTargeting()
    {
        if (targetingCard == null)
        {
            boardClickController?.ClearAttackRange();
            return;
        }

        HandCardView cancelled = targetingCard;
        targetingCard = null;
        cancelled.SetAwaitingTarget(false);
        boardClickController?.ClearAttackRange();
        if (cancelled == forcedCard)
        {
            MoveViewToZone(cancelled, discardPile, false);
            forcedCard = null;
            PresentNextForcedCard();
        }
        LayoutHand();
    }

    public void EndTurnDiscardAll()
    {
        CancelTargeting();
        List<HandCardView> snapshot = new List<HandCardView>(hand);
        foreach (HandCardView view in snapshot)
        {
            string id = view.Data.cardId;
            if (id == "crystal_shatter") player.TakeTypedDamage(2, DamageType.Normal);
            if (id == "cross_possessed") player.State.Add(CombatStatus.Corruption, 3);
            if (id == "cloak_broken") player.State.Add(CombatStatus.Vulnerable, 1, 2);
            bool exhaustCurse = id == "crystal_shatter" || id == "cross_possessed" || id == "cloak_broken";
            MoveViewToZone(view, exhaustCurse ? exhaustPile : discardPile, false);
        }
        LayoutHand();
        UpdateDeckDisplay();
    }

    public void ResolveIdentify(string equipmentPool)
    {
        List<CardData> pool = CardCatalog.GetPool(equipmentPool);
        List<CardData> normal = pool.Where(c => c.rarity != CardRarity.Red).ToList();
        List<CardRarity> rarities = normal.Select(c => c.rarity).Distinct().ToList();
        CardRarity rarity = StartingDeckBuilder.RollNormalRarity(rarities);
        List<CardData> candidates = normal.Where(c => c.rarity == rarity).ToList();
        AddCreatedCard(new CardInstance(candidates[UnityEngine.Random.Range(0, candidates.Count)]), true);

        List<CardData> curses = pool.Where(c => c.rarity == CardRarity.Red).ToList();
        if (curses.Count > 0 && UnityEngine.Random.value < 0.20f)
            AddCreatedCard(new CardInstance(curses[UnityEngine.Random.Range(0, curses.Count)]), true);
    }

    public void RemoveFamily(CardFamily family)
    {
        MoveMatching(drawPile, exhaustPile, card => card.Data.family == family);
        MoveMatching(discardPile, exhaustPile, card => card.Data.family == family);
        foreach (HandCardView view in hand.Where(v => v.Data.family == family).ToList())
            MoveViewToZone(view, exhaustPile, false);
        LayoutHand();
        UpdateDeckDisplay();
    }

    public void QueueFreeTopCards(int count)
    {
        for (int i = 0; i < count; i++)
        {
            CardInstance next = TakeTopCard();
            if (next == null) break;
            next.FreePlay = true;
            next.ForcedPlay = true;
            forcedQueue.Enqueue(next);
        }
        if (!forcedPresentationScheduled) StartCoroutine(PresentForcedNextFrame());
    }

    public void ShowTopCardsForDiscard(int count)
    {
        if (modal != null) return;
        List<CardInstance> cards = drawPile.Take(Mathf.Max(0, count)).ToList();
        if (cards.Count == 0) return;
        HashSet<CardInstance> selected = new HashSet<CardInstance>();
        modal = CreateModal("预演：选择要置入弃牌堆的牌");
        Transform content = modal.transform.Find("Panel/Content");
        foreach (CardInstance card in cards)
        {
            Button button = CreateButton(content, $"{card.Data.cardName}  [{card.Data.costText}]");
            button.onClick.AddListener(() =>
            {
                if (!selected.Add(card)) selected.Remove(card);
                button.GetComponent<Image>().color = selected.Contains(card) ? new Color(0.65f, 0.28f, 0.25f) : new Color(0.22f, 0.25f, 0.32f);
            });
        }
        Button confirm = CreateButton(content, "确认");
        confirm.onClick.AddListener(() =>
        {
            foreach (CardInstance card in selected)
                if (drawPile.Remove(card)) discardPile.Add(card);
            CloseModal();
            UpdateDeckDisplay();
        });
    }

    public void BeginPriestSacrifice()
    {
        if (GameSession.SelectedClass != HeroClass.Priest || player.State.PriestHealUsedThisTurn || hand.Count == 0) return;
        priestSacrificeMode = true;
        Debug.Log("请选择一张手牌消耗，用于牧师治疗。", this);
    }

    public void ShowCardDetails(HandCardView card)
    {
        if (card?.Data == null || detailPanel == null) return;
        detailOwner = card;
        detailTitle.text = $"{card.Data.cardName}　{RarityName(card.Data.rarity)}";
        string target = card.Data.targetMode == CardTargetMode.Self ? "自身" : $"距离 {card.Data.range}";
        detailDescription.text = $"费用 {card.Data.costText}　{target}\n{card.Data.description}";
        detailPanel.SetActive(true);
    }

    public void HideCardDetails(HandCardView card)
    {
        if (detailOwner != card) return;
        detailOwner = null;
        detailPanel?.SetActive(false);
    }

    public void RestoreCardOrder()
    {
        for (int i = 0; i < hand.Count; i++) if (hand[i] != null) hand[i].transform.SetSiblingIndex(i);
        targetingCard?.transform.SetAsLastSibling();
    }

    private void BuildStartingDeck()
    {
        foreach (HandCardView view in hand.ToList()) if (view != null) Destroy(view.gameObject);
        hand.Clear(); totalDeck.Clear(); drawPile.Clear(); discardPile.Clear(); exhaustPile.Clear();

        if (startingDeck == null)
        {
            Debug.LogError("战斗场景未配置基础牌库 PlayerStartingDeck，无法生成完整初始牌库。", this);
        }
        else
        {
            foreach (CardData card in startingDeck.CreateDrawPile())
                totalDeck.Add(new CardInstance(card));
        }

        totalDeck.AddRange(StartingDeckBuilder.Build(ClassCatalog.Get(GameSession.SelectedClass)));
        drawPile.AddRange(totalDeck);
        Debug.Log($"{ClassCatalog.Get(GameSession.SelectedClass).DisplayName}初始牌库生成完毕：{drawPile.Count}张。", this);
    }

    private bool DrawOne()
    {
        CardInstance instance = TakeTopCard();
        if (instance == null) return false;
        if (instance.Data.cardId == "crystal_backlash")
        {
            player.TakeTypedDamage(5, DamageType.Dark);
            exhaustPile.Add(instance);
            UpdateDeckDisplay();
            return true;
        }
        AddCardView(instance);
        return true;
    }

    private CardInstance TakeTopCard()
    {
        if (drawPile.Count == 0) RecycleDiscardPile();
        if (drawPile.Count == 0) return null;
        CardInstance card = drawPile[0];
        drawPile.RemoveAt(0);
        UpdateDeckDisplay();
        return card;
    }

    private void AddCreatedCard(CardInstance instance, bool mandatory)
    {
        if (hand.Count >= HandLimit)
        {
            if (mandatory)
            {
                pendingCardAdds.Enqueue(instance);
                ShowMandatoryDiscardChoice();
            }
            else discardPile.Add(instance);
            return;
        }
        AddCardView(instance);
    }

    private HandCardView AddCardView(CardInstance instance)
    {
        if (cardViewPrefab == null || handRoot == null) return null;
        HandCardView view = Instantiate(cardViewPrefab, handRoot);
        view.name = $"HandCard_{instance.Data.cardId}_{hand.Count}";
        RectTransform rect = (RectTransform)view.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(cardWidth, cardHeight);
        view.Initialize(this, instance);
        hand.Add(view);
        LayoutHand(true);
        view.PlayDrawAnimation();
        UpdateDeckDisplay();
        return view;
    }

    private bool TryCommitCard(HandCardView view, Unit target, BoardCell cell, Vector2Int? direction)
    {
        int previousIndex = hand.IndexOf(view);
        if (previousIndex >= 0) hand.RemoveAt(previousIndex);
        CardPlayResult result = ResolveResolver().TryPlay(view.Instance, target, cell, direction);
        if (!result.Success)
        {
            if (previousIndex >= 0) hand.Insert(Mathf.Min(previousIndex, hand.Count), view);
            LayoutHand();
            return false;
        }

        bool wasForced = view == forcedCard;
        List<CardInstance> zone = view.Data.exhaust ? exhaustPile : discardPile;
        MoveViewToZone(view, zone, true);
        if (result.RemoveFamily != CardFamily.None) RemoveFamily(result.RemoveFamily);
        if (wasForced) forcedCard = null;
        CardPlayed?.Invoke(view.Data);

        Action continuation = () =>
        {
            if (wasForced) PresentNextForcedCard();
            if (result.EndTurn) BattleFlow.Instance?.RequestEndPlayerTurn();
        };
        if (result.FreeMoveSteps > 0) boardClickController.BeginFreeMove(player, result.FreeMoveSteps, continuation);
        else continuation();
        return true;
    }

    private void BeginTargeting(HandCardView view)
    {
        targetingCard = view;
        view.SetAwaitingTarget(true);
        boardClickController.ClearSelection();
        int range = view.Data.range + (view.Data.family == CardFamily.Bow && GameSession.SelectedClass == HeroClass.Ranger ? 1 : 0);
        boardClickController.ShowAttackRange(player, Mathf.Max(1, range));
        LayoutHand();
        ShowCardDetails(view);
    }

    private void PresentNextForcedCard()
    {
        if (forcedCard != null || forcedQueue.Count == 0) return;
        CardInstance instance = forcedQueue.Dequeue();
        if (instance.Data.curse)
        {
            instance.ForcedPlay = false;
            instance.FreePlay = false;
            AddCreatedCard(instance, true);
            PresentNextForcedCard();
            return;
        }
        forcedCard = AddCardView(instance);
        if (forcedCard != null) PlayCard(forcedCard);
        else discardPile.Add(instance);
    }

    private IEnumerator PresentForcedNextFrame()
    {
        forcedPresentationScheduled = true;
        yield return null;
        forcedPresentationScheduled = false;
        PresentNextForcedCard();
    }

    private void ShowMandatoryDiscardChoice()
    {
        if (modal != null || hand.Count == 0) return;
        modal = CreateModal(pendingCardAdds.Count > 0
            ? "手牌已满：选择1张弃置后获得卡牌"
            : "手牌已满：选择1张弃置后继续抽牌");
        Transform content = modal.transform.Find("Panel/Content");
        foreach (HandCardView view in hand.ToList())
        {
            Button button = CreateButton(content, view.Data.cardName);
            button.onClick.AddListener(() =>
            {
                MoveViewToZone(view, discardPile, false);
                CloseModal();
                if (pendingCardAdds.Count > 0)
                    AddCardView(pendingCardAdds.Dequeue());
                else if (pendingMandatoryDraws > 0)
                {
                    pendingMandatoryDraws--;
                    DrawCards(1, true);
                    if (drawPile.Count + discardPile.Count == 0) pendingMandatoryDraws = 0;
                }
                if (pendingCardAdds.Count > 0 || pendingMandatoryDraws > 0) ShowMandatoryDiscardChoice();
            });
        }
    }

    private void SacrificeForPriestHeal(HandCardView view)
    {
        priestSacrificeMode = false;
        MoveViewToZone(view, exhaustPile, true);
        player.State.PriestHealUsedThisTurn = true;
        modal = CreateModal("选择距离2内的治疗目标");
        Transform content = modal.transform.Find("Panel/Content");
        foreach (Unit unit in FindObjectsByType<Unit>())
        {
            int distance = Mathf.Abs(unit.Position.x - player.Position.x) + Mathf.Abs(unit.Position.y - player.Position.y);
            if (!unit.IsAlive || distance > 2) continue;
            Button button = CreateButton(content, unit.DisplayName);
            button.onClick.AddListener(() => { unit.Heal(5); CloseModal(); });
        }
    }

    private void MoveViewToZone(HandCardView view, List<CardInstance> zone, bool animate)
    {
        if (view == null) return;
        hand.Remove(view);
        zone.Add(view.Instance);
        if (targetingCard == view) targetingCard = null;
        HideCardDetails(view);
        if (animate) view.PlayCard(); else Destroy(view.gameObject);
        LayoutHand();
        UpdateDeckDisplay();
    }

    private static void MoveMatching(List<CardInstance> from, List<CardInstance> to, Func<CardInstance, bool> predicate)
    {
        foreach (CardInstance card in from.Where(predicate).ToList()) { from.Remove(card); to.Add(card); }
    }

    private void RecycleDiscardPile()
    {
        if (discardPile.Count == 0) return;
        drawPile.AddRange(discardPile);
        discardPile.Clear();
        Shuffle(drawPile);
    }

    private static void Shuffle(List<CardInstance> cards)
    {
        for (int i = cards.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }
    }

    private void LayoutHand(bool immediate = false)
    {
        if (handRoot == null) return;
        float spacing = hand.Count <= 1 ? 0f : Mathf.Min(preferredSpacing, Mathf.Max(55f, (handRoot.rect.width - cardWidth) / (hand.Count - 1)));
        float start = -spacing * (hand.Count - 1) * 0.5f;
        for (int i = 0; i < hand.Count; i++) hand[i]?.SetLayoutPosition(new Vector2(start + i * spacing, 8f), immediate);
    }

    private CardEffectResolver ResolveResolver()
    {
        if (cardEffectResolver == null)
        {
            cardEffectResolver = FindAnyObjectByType<CardEffectResolver>();
            if (cardEffectResolver == null) cardEffectResolver = gameObject.AddComponent<CardEffectResolver>();
        }
        cardEffectResolver.Bind(boardClickController, player, GameObject.Find("Monster")?.GetComponent<Unit>());
        return cardEffectResolver;
    }

    private void ResolveReferences()
    {
        boardClickController ??= FindAnyObjectByType<BoardClickController>();
        player ??= GameObject.Find("Player")?.GetComponent<Unit>();
        ResolveResolver();
    }

    private void BindDeckPile()
    {
        GameObject cardControl = GameObject.Find("C_CardControl");
        Transform stack = cardControl == null ? null : cardControl.transform.Find("Stack");
        Transform pile = stack == null ? null : stack.Find("I_Stack");
        Transform number = stack == null ? null : stack.Find("Number");
        deckPileImage ??= pile?.GetComponent<Image>();
        deckPileButton ??= pile?.GetComponent<Button>();
        deckCountText ??= number?.GetComponent<TMP_Text>();
        if (deckPileButton != null) deckPileButton.interactable = false;
    }

    private void UpdateDeckDisplay()
    {
        if (deckCountText != null) deckCountText.text = drawPile.Count.ToString();
    }

    private void CreateDetailPanel()
    {
        Canvas canvas = FindScreenCanvas();
        if (canvas == null) return;
        detailPanel = new GameObject("CardDetailPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        detailPanel.transform.SetParent(canvas.transform, false);
        RectTransform rect = (RectTransform)detailPanel.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-24f, 0f);
        rect.sizeDelta = new Vector2(360f, 230f);
        detailPanel.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.09f, 0.94f);
        detailTitle = CreateText("Title", detailPanel.transform, 26f);
        detailDescription = CreateText("Description", detailPanel.transform, 20f);
        SetRect(detailTitle.rectTransform, new Vector2(0f, 0.72f), Vector2.one, new Vector2(14f, 0f), new Vector2(-14f, -8f));
        SetRect(detailDescription.rectTransform, Vector2.zero, new Vector2(1f, 0.72f), new Vector2(14f, 12f), new Vector2(-14f, -4f));
        detailDescription.textWrappingMode = TextWrappingModes.Normal;
        detailPanel.SetActive(false);
    }

    private GameObject CreateModal(string title)
    {
        Canvas canvas = FindScreenCanvas();
        GameObject root = new GameObject("CardChoiceModal", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        root.transform.SetParent(canvas.transform, false);
        SetRect((RectTransform)root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);
        GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panel.transform.SetParent(root.transform, false);
        RectTransform panelRect = (RectTransform)panel.transform;
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(560f, 560f);
        panel.GetComponent<Image>().color = new Color(0.1f, 0.12f, 0.18f, 0.98f);
        TMP_Text heading = CreateText("Heading", panel.transform, 28f);
        SetRect(heading.rectTransform, new Vector2(0f, 0.88f), Vector2.one, new Vector2(18f, 0f), new Vector2(-18f, -10f));
        heading.text = title;
        heading.alignment = TextAlignmentOptions.Center;
        GameObject content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup));
        content.transform.SetParent(panel.transform, false);
        SetRect((RectTransform)content.transform, new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.86f), Vector2.zero, Vector2.zero);
        VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 8f; layout.childControlHeight = true; layout.childForceExpandHeight = false;
        root.transform.SetAsLastSibling();
        return root;
    }

    private Button CreateButton(Transform parent, string label)
    {
        GameObject obj = new GameObject("Choice", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        obj.transform.SetParent(parent, false);
        obj.GetComponent<Image>().color = new Color(0.22f, 0.25f, 0.32f);
        obj.GetComponent<LayoutElement>().preferredHeight = 52f;
        TMP_Text text = CreateText("Label", obj.transform, 20f);
        SetRect(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(10f, 4f), new Vector2(-10f, -4f));
        text.text = label; text.alignment = TextAlignmentOptions.Center;
        return obj.GetComponent<Button>();
    }

    private void CloseModal()
    {
        if (modal != null) Destroy(modal);
        modal = null;
    }

    private static TMP_Text CreateText(string name, Transform parent, float size)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        TMP_Text text = obj.GetComponent<TMP_Text>();
        text.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/SourceHanSansSC-Regular SDF") ?? TMP_Settings.defaultFontAsset;
        text.fontSize = size; text.color = Color.white; text.raycastTarget = false;
        return text;
    }

    private static void SetRect(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax;
    }

    private static string RarityName(CardRarity rarity) => rarity switch
    {
        CardRarity.Gray => "灰", CardRarity.Blue => "蓝", CardRarity.Purple => "紫",
        CardRarity.Gold => "金", CardRarity.Red => "红", _ => rarity.ToString()
    };

    private static Canvas FindScreenCanvas()
    {
        foreach (Canvas canvas in FindObjectsByType<Canvas>())
            if (canvas.renderMode != RenderMode.WorldSpace) return canvas;
        return null;
    }
}
