using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HandCardSystem : MonoBehaviour, IContentCardZoneService, IContentTargetQueryService
{
    private const int DefaultHandLimit = 10;

    [Header("卡牌资源")]
    [SerializeField] private HandCardView cardViewPrefab;
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
    private bool activatedAbilityCardCostMode;
    private ClassProfileDefinition pendingActivatedAbilityProfile;
    private bool forcedPresentationScheduled;
    private Action forcedQueueCompleted;
    private Action<Unit> pendingUnitSelection;
    private Func<Unit, bool> pendingUnitValidator;
    private int EffectiveHandLimit => ContentRuntime.IsLoaded
        ? Mathf.Max(1, ContentRuntime.Registry.GameSettings.HandLimit)
        : DefaultHandLimit;

    public int DrawPileCount => drawPile.Count;
    public int DiscardPileCount => discardPile.Count;
    public int ExhaustPileCount => exhaustPile.Count;
    public int HandCount => hand.Count;
    public bool IsTargeting => targetingCard != null || pendingUnitSelection != null;
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
        ResolveReferences();
        CombatCardZoneRegistry.Register(player, this);
        BuildStartingDeck();
        Shuffle(drawPile);
        bool exploring = WorldPlaySession.Instance != null && WorldPlaySession.Instance.IsExploring;
        if (exploring)
        {
            UpdateDeckDisplay();
            yield break;
        }

        DrawOpeningHand();
        UpdateDeckDisplay();
    }

    /// <summary>探索结束后开战，或场景直接开战时补第一手牌。</summary>
    public void DrawOpeningHand()
    {
        if (hand.Count > 0) return;
        int initialDraw = ContentRuntime.IsLoaded
            ? ContentRuntime.Registry.GameSettings.StartingHandSize
            : startingHandSize;
        DrawCards(initialDraw, true);
    }

    /// <summary>把所有区里的牌收回抽牌堆，供下一场战斗使用。</summary>
    public void RecycleAllIntoDrawPile()
    {
        CancelTargeting();
        for (int i = 0; i < hand.Count; i++)
            if (hand[i] != null) Destroy(hand[i].gameObject);
        hand.Clear();
        drawPile.Clear();
        discardPile.Clear();
        exhaustPile.Clear();
        drawPile.AddRange(totalDeck);
        Shuffle(drawPile);
        UpdateDeckDisplay();
    }

    private void OnDestroy()
    {
        CombatCardZoneRegistry.Unregister(player, this);
    }

    /// <summary>奖励关把一张牌加入本局牌库。</summary>
    public void GainCard(string cardId)
    {
        if (!ContentRuntime.IsLoaded || !ContentId.IsValid(cardId)) return;
        if (!ContentRuntime.Registry.TryGetCard(cardId, out CardDefinition card) || card == null) return;
        CardInstance instance = new CardInstance(card);
        totalDeck.Add(instance);
        drawPile.Add(instance);
        UpdateDeckDisplay();
    }

    public void DrawCards(int count, bool mandatory)
    {
        int remaining = Mathf.Max(0, count);
        while (remaining > 0)
        {
            if (hand.Count >= EffectiveHandLimit)
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

    /// <summary>
    /// 为行为图目标选择器返回当前场景单位快照；注册表负责稳定排序和范围筛选。
    /// </summary>
    /// <returns>当前已加载的全部 Unit 组件快照。</returns>
    public IReadOnlyList<Unit> GetUnits()
    {
        return FindObjectsByType<Unit>().Where(unit => unit != null).ToArray();
    }

    /// <summary>
    /// 为行为图目标选择器返回指定牌区的卡牌实例快照，不暴露内部可变列表。
    /// </summary>
    /// <param name="zoneKey">hand、draw、discard 或 exhaust。</param>
    /// <returns>指定牌区的独立只读快照；未知 key 返回空数组。</returns>
    public IReadOnlyList<CardInstance> GetCards(string zoneKey)
    {
        return zoneKey switch
        {
            ContentCardZoneKeys.Hand => hand.Where(view => view != null && view.Instance != null)
                .Select(view => view.Instance).ToArray(),
            ContentCardZoneKeys.Draw => drawPile.ToArray(),
            ContentCardZoneKeys.Discard => discardPile.ToArray(),
            ContentCardZoneKeys.Exhaust => exhaustPile.ToArray(),
            _ => Array.Empty<CardInstance>()
        };
    }

    /// <summary>
    /// 按发布注册表查询卡牌并创建独立实例，确定性选择按卡牌 ID 排序后的第一项。
    /// </summary>
    /// <param name="query">受控卡牌查询。</param>
    /// <param name="count">生成数量。</param>
    /// <param name="destinationZone">目标牌区。</param>
    /// <returns>查询、数量和目标牌区有效且全部加入时返回 true。</returns>
    public bool GenerateCards(ContentCardQuery query, int count, string destinationZone)
    {
        if (query == null || count < 0 || !ContentCardZoneKeys.IsConcrete(destinationZone) ||
            !ContentRuntime.IsLoaded) return false;
        CardDefinition definition = ContentRuntime.Registry.Cards
            .Where(card => MatchesQuery(card, query))
            .OrderBy(card => card.CardId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (definition == null) return false;
        for (int index = 0; index < count; index++)
        {
            if (!AddInstanceToZone(new CardInstance(definition), destinationZone)) return false;
        }
        UpdateDeckDisplay();
        return true;
    }

    /// <summary>
    /// 在两个牌区之间移动查询匹配的卡牌，手牌视图会通过现有动画入口进入目标区。
    /// </summary>
    /// <param name="query">受控卡牌查询。</param>
    /// <param name="count">最多移动数量；零表示全部。</param>
    /// <param name="sourceZone">来源牌区。</param>
    /// <param name="destinationZone">目标牌区。</param>
    /// <returns>实际移动数量。</returns>
    public int MoveCards(ContentCardQuery query, int count, string sourceZone, string destinationZone)
    {
        if (query == null || count < 0 || !ContentCardZoneKeys.IsConcrete(sourceZone) ||
            !ContentCardZoneKeys.IsConcrete(destinationZone) || sourceZone == destinationZone) return 0;
        int limit = count == 0 ? int.MaxValue : count;
        int moved = 0;
        if (sourceZone == ContentCardZoneKeys.Hand)
        {
            List<HandCardView> views = hand.Where(view => view != null && MatchesQuery(view.Instance, query))
                .Take(limit).ToList();
            List<CardInstance> destination = ResolveMutableZone(destinationZone);
            if (destination == null) return 0;
            foreach (HandCardView view in views)
            {
                MoveViewToZone(view, destination, false);
                moved++;
            }
        }
        else
        {
            List<CardInstance> source = ResolveMutableZone(sourceZone);
            if (source == null) return 0;
            foreach (CardInstance instance in source.Where(card => MatchesQuery(card, query)).Take(limit).ToList())
            {
                if (!AddInstanceToZone(instance, destinationZone)) break;
                source.Remove(instance);
                moved++;
            }
        }
        UpdateDeckDisplay();
        return moved;
    }

    /// <summary>
    /// 从单一或全部牌区移除查询匹配的卡牌实例。
    /// </summary>
    /// <param name="query">受控卡牌查询。</param>
    /// <param name="zone">目标牌区或 all。</param>
    /// <returns>实际移除数量。</returns>
    public int RemoveCards(ContentCardQuery query, string zone)
    {
        if (query == null) return 0;
        int removed = 0;
        if (!ContentCardZoneKeys.IsConcreteOrAll(zone)) return 0;
        IEnumerable<string> zones = zone == ContentCardZoneKeys.All
            ? new[] { ContentCardZoneKeys.Hand, ContentCardZoneKeys.Draw,
                ContentCardZoneKeys.Discard, ContentCardZoneKeys.Exhaust }
            : new[] { zone };
        foreach (string currentZone in zones)
        {
            if (currentZone == ContentCardZoneKeys.Hand)
            {
                foreach (HandCardView view in hand.Where(item => item != null && MatchesQuery(item.Instance, query)).ToList())
                {
                    hand.Remove(view);
                    Destroy(view.gameObject);
                    removed++;
                }
                continue;
            }
            List<CardInstance> cards = ResolveMutableZone(currentZone);
            if (cards == null) continue;
            removed += cards.RemoveAll(card => MatchesQuery(card, query));
        }
        LayoutHand();
        UpdateDeckDisplay();
        return removed;
    }

    /// <summary>使用现有模态界面查看牌顶并选择弃置。</summary>
    public void RevealTopCardsAndChooseDiscard(int count, Action onComplete)
    {
        ShowTopCardsForDiscard(count, onComplete);
    }

    /// <summary>使用现有强制出牌队列免费打出牌顶卡。</summary>
    public void PlayTopCardsForFree(int count, Action onComplete)
    {
        QueueFreeTopCards(count, onComplete);
    }

    /// <summary>手牌未满则入手；已满则这张多出的牌进入弃牌堆。</summary>
    public bool AddCardToHandOrDiscard(CardInstance instance)
    {
        if (instance == null) return false;
        AddCreatedCard(instance, false);
        UpdateDeckDisplay();
        return true;
    }

    /// <summary>把实例加入指定牌区；加入手牌时遵守手牌上限并创建视图。</summary>
    private bool AddInstanceToZone(CardInstance instance, string zone)
    {
        if (instance == null) return false;
        if (zone == ContentCardZoneKeys.Hand)
        {
            if (hand.Count >= EffectiveHandLimit) return false;
            AddCardView(instance);
            return true;
        }
        List<CardInstance> destination = ResolveMutableZone(zone);
        if (destination == null) return false;
        destination.Add(instance);
        return true;
    }

    /// <summary>把稳定牌区 key 映射到内部可变列表；手牌由视图列表单独处理。</summary>
    private List<CardInstance> ResolveMutableZone(string zone)
    {
        return zone switch
        {
            ContentCardZoneKeys.Draw => drawPile,
            ContentCardZoneKeys.Discard => discardPile,
            ContentCardZoneKeys.Exhaust => exhaustPile,
            _ => null
        };
    }

    /// <summary>判断数据库卡牌定义是否满足查询的全部非空字段。</summary>
    private static bool MatchesQuery(CardDefinition card, ContentCardQuery query)
    {
        return card != null &&
               (string.IsNullOrWhiteSpace(query.cardId) || card.CardId == query.cardId) &&
               (string.IsNullOrWhiteSpace(query.poolId) || card.Pools.Any(pool => pool.PoolId == query.poolId)) &&
               (string.IsNullOrWhiteSpace(query.familyId) || card.FamilyId == query.familyId) &&
               (string.IsNullOrWhiteSpace(query.rarityId) || card.RarityId == query.rarityId) &&
               (string.IsNullOrWhiteSpace(query.tag) || card.Tags.Contains(query.tag));
    }

    /// <summary>判断运行时实例的数据库定义是否满足查询。</summary>
    private static bool MatchesQuery(CardInstance instance, ContentCardQuery query)
    {
        return instance?.Definition != null && MatchesQuery(instance.Definition, query);
    }

    public bool DrawCard()
    {
        if (hand.Count >= EffectiveHandLimit) return false;
        return DrawOne();
    }

    public void PlayCard(HandCardView view)
    {
        if (view == null || view.Instance?.Data == null || !hand.Contains(view)) return;
        if (!BattleFlow.CanPlayerAct || modal != null) return;

        if (activatedAbilityCardCostMode)
        {
            PayActivatedAbilityCardCost(view);
            return;
        }

        if (forcedCard != null && view != forcedCard) return;
        if (targetingCard == view) { CancelTargeting(); return; }
        if (IsTargeting) CancelTargeting(false);

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

    /// <summary>确认棋盘点击目标；成功后执行卡牌或独立选择回调并立即退出选择界面。</summary>
    public bool TryConfirmTarget(Unit target, BoardCell cell)
    {
        if (pendingUnitSelection != null)
        {
            if (target == null || pendingUnitValidator != null && !pendingUnitValidator(target)) return false;
            Action<Unit> completed = pendingUnitSelection;
            ClearPendingUnitSelection();
            completed(target);
            if (BattleFlow.CanPlayerAct &&
                (boardClickController == null || !boardClickController.IsResolvingFreeMove))
                boardClickController?.ResumeSuspendedFreeMove();
            return true;
        }

        if (targetingCard == null) return false;
        CardData data = targetingCard.Data;
        Vector2Int? direction = null;
        ResolveActingUnit();
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

    /// <summary>取消卡牌或独立棋子选择，并清除卡牌待命状态及全部攻击范围高亮。</summary>
    public void CancelTargeting(bool resumeFreeMove = true)
    {
        ClearPendingUnitSelection();
        if (targetingCard == null)
        {
            boardClickController?.ClearAttackRange();
            if (resumeFreeMove && BattleFlow.CanPlayerAct)
                boardClickController?.ResumeSuspendedFreeMove();
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
        if (resumeFreeMove && BattleFlow.CanPlayerAct)
            boardClickController?.ResumeSuspendedFreeMove();
    }

    /// <summary>
    /// 执行数据库定义的手牌回合结束触发器，并按通用诅咒/消耗字段移动全部手牌。
    /// </summary>
    public void EndTurnDiscardAll()
    {
        CancelTargeting();
        List<HandCardView> snapshot = new List<HandCardView>(hand);
        foreach (HandCardView view in snapshot)
        {
            ExecuteContentTrigger(view.Instance, ContentTriggerKeys.OnTurnEndInHand);
            bool exhaustAtTurnEnd = view.Instance.Definition.Curse && view.Instance.Definition.ExhaustOnPlay &&
                                    !view.Instance.Data.temporary;
            MoveViewToZone(view, exhaustAtTurnEnd ? exhaustPile : discardPile, false);
        }
        LayoutHand();
        UpdateDeckDisplay();
    }

    /// <summary>把牌顶卡加入强制免费出牌队列，并在队列耗尽时通知行为图恢复。</summary>
    public void QueueFreeTopCards(int count, Action onComplete = null)
    {
        int queued = 0;
        for (int i = 0; i < count; i++)
        {
            CardInstance next = TakeTopCard();
            if (next == null) break;
            next.FreePlay = true;
            next.ForcedPlay = true;
            forcedQueue.Enqueue(next);
            queued++;
        }
        if (queued == 0)
        {
            onComplete?.Invoke();
            return;
        }
        forcedQueueCompleted += onComplete;
        if (!forcedPresentationScheduled) StartCoroutine(PresentForcedNextFrame());
    }

    /// <summary>打开牌顶弃置选择界面，并在玩家确认或没有可选牌时通知行为图恢复。</summary>
    public void ShowTopCardsForDiscard(int count, Action onComplete = null)
    {
        if (modal != null)
        {
            onComplete?.Invoke();
            return;
        }
        List<CardInstance> cards = drawPile.Take(Mathf.Max(0, count)).ToList();
        if (cards.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }
        HashSet<CardInstance> selected = new HashSet<CardInstance>();
        modal = CreateModal("预演：选择要置入弃牌堆的牌");
        Transform content = modal.transform.Find("Panel/Content");
        foreach (CardInstance card in cards)
        {
            Button button = CreateButton(content, $"{card.Data.cardName}  [{card.Data.costText}]");
            button.onClick.AddListener(() =>
            {
                if (!selected.Add(card)) selected.Remove(card);
                button.GetComponent<Image>().color = selected.Contains(card)
                    ? new Color(1f, 0.72f, 0.72f, 1f)
                    : new Color(0.9f, 0.9f, 0.9f, 1f);
            });
        }
        Button confirm = CreateButton(content, "确认");
        confirm.onClick.AddListener(() =>
        {
            foreach (CardInstance card in selected)
                if (drawPile.Remove(card)) discardPile.Add(card);
            CloseModal();
            UpdateDeckDisplay();
            onComplete?.Invoke();
        });
    }

    public void BeginActivatedClassAbility()
    {
        if (!ContentClassPassiveRuntime.TryGetSelectedProfile(out ClassProfileDefinition profile) ||
            string.IsNullOrWhiteSpace(profile.GetTraitString("activated_ability_id")) ||
            player.State.ActivatedAbilityUsedThisTurn || hand.Count == 0) return;
        pendingActivatedAbilityProfile = profile;
        activatedAbilityCardCostMode = true;
        Debug.Log("请选择一张手牌支付职业能力费用。", this);
    }

    public void ShowCardDetails(HandCardView card)
    {
        if (card?.Data == null || detailPanel == null) return;
        detailOwner = card;
        detailTitle.text = $"{card.Data.cardName}　{RarityName(card.Data.rarityId)}";
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

    /// <summary>
    /// 只使用已发布数据库中的职业配方构建初始牌库；内容无效时立即停止战斗初始化。
    /// </summary>
    private void BuildStartingDeck()
    {
        foreach (HandCardView view in hand.ToList()) if (view != null) Destroy(view.gameObject);
        hand.Clear(); totalDeck.Clear(); drawPile.Clear(); discardPile.Clear(); exhaustPile.Clear();

        if (!ContentRuntime.IsLoaded)
        {
            throw new InvalidOperationException($"没有可用的数据库内容包，无法构建牌库：{ContentRuntime.LoadError}");
        }

        string selectedClassId = GameSession.SelectedClassId;
        if (RunSession.HasActive && RunSession.Current.deckCardIds != null && RunSession.Current.deckCardIds.Length > 0)
        {
            for (int i = 0; i < RunSession.Current.deckCardIds.Length; i++)
            {
                CardDefinition card = ContentRuntime.Registry.GetCard(RunSession.Current.deckCardIds[i]);
                totalDeck.Add(new CardInstance(card));
            }
        }
        else
        {
            if (!ContentRuntime.Registry.TryGetClassProfile(selectedClassId, out ClassProfileDefinition profile) || profile.DeckRecipe.Count == 0)
            {
                throw new InvalidOperationException($"数据库未提供职业 {selectedClassId} 的有效初始牌库配方。");
            }
            totalDeck.AddRange(StartingDeckBuilder.Build(profile, ContentRuntime.Registry));
            if (RunSession.HasActive) RunSession.SetDeck(ExportOwnedCardIds());
            Debug.Log($"{profile.DisplayName}初始牌库生成完毕：{totalDeck.Count}张。", this);
        }

        drawPile.AddRange(totalDeck);
    }

    /// <summary>把当前牌库、手牌、弃牌和消耗堆里的卡牌 ID 交给存档。</summary>
    public List<string> ExportOwnedCardIds()
    {
        List<string> ids = new List<string>();
        AppendCardIds(ids, drawPile);
        for (int i = 0; i < hand.Count; i++)
            if (hand[i]?.Instance?.Definition != null) ids.Add(hand[i].Instance.Definition.CardId);
        AppendCardIds(ids, discardPile);
        AppendCardIds(ids, exhaustPile);
        if (ids.Count == 0) AppendCardIds(ids, totalDeck);
        return ids;
    }

    private static void AppendCardIds(List<string> ids, List<CardInstance> cards)
    {
        if (cards == null) return;
        for (int i = 0; i < cards.Count; i++)
            if (cards[i]?.Definition != null) ids.Add(cards[i].Definition.CardId);
    }

    private bool DrawOne()
    {
        CardInstance instance = TakeTopCard();
        if (instance == null) return false;
        if (instance.Definition != null && ContentCardEffectExecutor.CanExecuteTrigger(instance.Definition, ContentTriggerKeys.OnDraw))
        {
            ExecuteContentTrigger(instance, ContentTriggerKeys.OnDraw);
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
        if (hand.Count >= EffectiveHandLimit)
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
        ExecuteContentTrigger(instance, ContentTriggerKeys.OnAddedToHand);
        LayoutHand(true);
        view.PlayDrawAnimation();
        UpdateDeckDisplay();
        return view;
    }

    /// <summary>为手牌生命周期事件创建无目标执行上下文并运行数据库触发器。</summary>
    /// <param name="instance">发生生命周期事件的卡牌实例。</param>
    /// <param name="triggerKey">需要执行的触发器 key。</param>
    /// <returns>存在且成功执行数据库行为时返回 true。</returns>
    private bool ExecuteContentTrigger(CardInstance instance, string triggerKey)
    {
        if (instance?.Definition == null || !ContentCardEffectExecutor.CanExecuteTrigger(instance.Definition, triggerKey))
            return false;
        CardPlayResult result = new CardPlayResult();
        ContentCardExecutionContext context = new ContentCardExecutionContext(
            instance, player, player, null, null, this, boardClickController, result,
            true, targetQueryService: this);
        return ContentCardEffectExecutor.TryExecuteTrigger(context, triggerKey);
    }

    /// <summary>交由通用效果解析器结算卡牌；成功时扣除手牌并清理目标选择界面。</summary>
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

        if (targetingCard == view)
        {
            targetingCard = null;
            view.SetAwaitingTarget(false);
            boardClickController?.ClearAttackRange();
        }

        bool wasForced = view == forcedCard;
        List<CardInstance> zone = view.Data.exhaust ? exhaustPile : discardPile;
        MoveViewToZone(view, zone, true);
        if (wasForced) forcedCard = null;
        CardPlayed?.Invoke(view.Data);

        Action continuation = () =>
        {
            if (wasForced) PresentNextForcedCard();
            if (result.EndTurn) BattleFlow.Instance?.RequestEndPlayerTurn();
        };
        if (result.FreeMoveSteps > 0) boardClickController.BeginFreeMove(player, result.FreeMoveSteps, () =>
        {
            result.ExecutionContext?.CompletePendingInteraction();
            continuation();
        });
        else
        {
            if (BattleFlow.CanPlayerAct) boardClickController?.ResumeSuspendedFreeMove();
            continuation();
        }
        return true;
    }

    private void BeginTargeting(HandCardView view)
    {
        ResolveReferences();
        Unit caster = ResolveActingUnit();
        targetingCard = view;
        view.SetAwaitingTarget(true);
        boardClickController.SuspendFreeMove();
        boardClickController.ClearSelection();
        ContentRuleQuery rangeQuery = ContentRuleQueryRuntime.Evaluate(new ContentRuleQuery(
            ContentRuleQueryKeys.TargetRange, caster, null, view.Data.range, view.Instance.Definition));
        int range = Mathf.Max(0, rangeQuery.Value);
        boardClickController.ShowAttackRange(caster, Mathf.Max(1, range));
        LayoutHand();
        ShowCardDetails(view);
    }

    private void PresentNextForcedCard()
    {
        if (forcedCard != null) return;
        if (forcedQueue.Count == 0)
        {
            Action completed = forcedQueueCompleted;
            forcedQueueCompleted = null;
            completed?.Invoke();
            return;
        }
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

    /// <summary>消耗所选手牌并进入棋盘治疗目标选择，不再创建单位名称列表。</summary>
    private void PayActivatedAbilityCardCost(HandCardView view)
    {
        activatedAbilityCardCostMode = false;
        MoveViewToZone(view, exhaustPile, true);
        ResolveActingUnit();
        player.State.ActivatedAbilityUsedThisTurn = true;
        int range = Mathf.Max(0, pendingActivatedAbilityProfile?.GetTraitInt("activated_ability_target_range", 0) ?? 0);
        BeginBoardUnitSelection(
            unit => unit.IsAlive && Mathf.Abs(unit.Position.x - player.Position.x) +
                Mathf.Abs(unit.Position.y - player.Position.y) <= range,
            unit =>
            {
                ContentClassPassiveRuntime.Execute(pendingActivatedAbilityProfile,
                    ContentTriggerKeys.OnActivatedAbility, player, boardClickController, unit, this);
                pendingActivatedAbilityProfile = null;
            }, range);
    }

    /// <summary>在棋盘上进入统一棋子选择模式，成功点击后自动清理所有范围高亮。</summary>
    private void BeginBoardUnitSelection(Func<Unit, bool> validator, Action<Unit> completed, int range)
    {
        CancelTargeting(false);
        pendingUnitValidator = validator;
        pendingUnitSelection = completed;
        Unit caster = ResolveActingUnit();
        boardClickController?.SuspendFreeMove();
        boardClickController?.ClearSelection();
        boardClickController?.ShowAttackRange(caster, Mathf.Max(0, range));
        Debug.Log("请直接点击棋盘上的目标棋子；右键可以取消。", this);
    }

    /// <summary>退出独立棋子选择模式并隐藏棋盘范围，回调会在清理后由确认方法执行。</summary>
    private void ClearPendingUnitSelection()
    {
        pendingUnitSelection = null;
        pendingUnitValidator = null;
        boardClickController?.ClearAttackRange();
    }

    private void MoveViewToZone(HandCardView view, List<CardInstance> zone, bool animate)
    {
        if (view == null) return;
        hand.Remove(view);
        zone.Add(view.Instance);
        CombatEventBus.Shared.Publish(new CardZoneChangedEvent(
            view.Instance, ContentCardZoneKeys.Hand, GetZoneKey(zone)));
        if (targetingCard == view) targetingCard = null;
        HideCardDetails(view);
        if (animate) view.PlayCard(); else Destroy(view.gameObject);
        LayoutHand();
        UpdateDeckDisplay();
    }

    /// <summary>Returns the stable zone key for one of this hand system's backing collections.</summary>
    private string GetZoneKey(List<CardInstance> zone)
    {
        if (ReferenceEquals(zone, drawPile)) return ContentCardZoneKeys.Draw;
        if (ReferenceEquals(zone, discardPile)) return ContentCardZoneKeys.Discard;
        if (ReferenceEquals(zone, exhaustPile)) return ContentCardZoneKeys.Exhaust;
        return string.Empty;
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
        cardEffectResolver.Bind(boardClickController, ResolveActingUnit(), BattleUnits.PrimaryEnemy);
        return cardEffectResolver;
    }

    private void ResolveReferences()
    {
        if (boardClickController == null)
        {
            boardClickController = FindAnyObjectByType<BoardClickController>();
        }

        player = ResolveActingUnit();
        ResolveResolver();
    }

    /// <summary>
    /// 手牌 Awake 早于编制生成；场景里还可能残留已删除的 Player 引用。
    /// 必须用 Unity 空判断在打牌时重新解析施放者，否则 ShowAttackRange 会直接返回。
    /// </summary>
    private Unit ResolveActingUnit()
    {
        Unit selected = boardClickController != null ? boardClickController.SelectedUnit : null;
        if (selected != null && selected.IsPlayer && selected.IsAlive)
        {
            player = selected;
            return player;
        }

        Unit ally = BattleUnits.PrimaryAlly;
        if (ally != null)
        {
            player = ally;
        }

        return player;
    }

    /// <summary>绑定 BattleInterface 卡背与数量文本，并恢复卡背按钮的可点击状态。</summary>
    private void BindDeckPile()
    {
        GameObject cardControl = GameObject.Find("C_CardControl");
        Transform stack = cardControl == null ? null : cardControl.transform.Find("Stack");
        Transform pile = stack == null ? null : stack.Find("I_Stack");
        Transform number = stack == null ? null : stack.Find("Number");
        deckPileImage ??= pile?.GetComponent<Image>();
        deckPileButton ??= pile?.GetComponent<Button>();
        deckCountText ??= number?.GetComponent<TMP_Text>();
        if (deckPileButton != null)
        {
            deckPileButton.interactable = true;
            deckPileButton.onClick.RemoveListener(ShowDrawPile);
            deckPileButton.onClick.AddListener(ShowDrawPile);
        }
    }

    /// <summary>点击卡背后以只读弹窗按牌顶顺序显示当前牌堆内容。</summary>
    private void ShowDrawPile()
    {
        if (modal != null) return;
        modal = CreateModal($"牌堆（{drawPile.Count}张，最上方为牌顶）");
        Transform content = modal.transform.Find("Panel/Content");
        string cardLines = drawPile.Count == 0
            ? "牌堆为空"
            : string.Join("\n", drawPile.Select((card, index) =>
                $"{index + 1}. {card.Data.cardName}　[{card.Data.costText}]"));
        CreateListLabel(content, cardLines, drawPile.Count);
        Button close = CreateButton(content, "关闭");
        close.onClick.AddListener(CloseModal);
    }

    /// <summary>创建牌堆弹窗中的多行只读卡牌清单，并根据条目数分配可读高度。</summary>
    private TMP_Text CreateListLabel(Transform parent, string value, int itemCount)
    {
        TMP_Text label = CreateText("CardList", parent, 18f);
        label.gameObject.AddComponent<LayoutElement>().preferredHeight = Mathf.Clamp(itemCount * 27f + 24f, 80f, 390f);
        label.text = value;
        label.alignment = TextAlignmentOptions.TopLeft;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Ellipsis;
        return label;
    }

    private void UpdateDeckDisplay()
    {
        if (deckCountText != null) deckCountText.text = drawPile.Count.ToString();
    }

    /// <summary>创建白底深色文字的卡牌详情浮窗，保证规则说明在不同场景亮度下清晰可读。</summary>
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
        Image panelImage = detailPanel.GetComponent<Image>();
        panelImage.sprite = WhiteUiSprite();
        panelImage.color = Color.white;
        detailTitle = CreateText("Title", detailPanel.transform, 26f);
        detailDescription = CreateText("Description", detailPanel.transform, 20f);
        SetRect(detailTitle.rectTransform, new Vector2(0f, 0.72f), Vector2.one, new Vector2(14f, 0f), new Vector2(-14f, -8f));
        SetRect(detailDescription.rectTransform, Vector2.zero, new Vector2(1f, 0.72f), new Vector2(14f, 12f), new Vector2(-14f, -4f));
        detailDescription.textWrappingMode = TextWrappingModes.Normal;
        detailPanel.SetActive(false);
    }

    /// <summary>创建带半透明遮罩的白色内容弹窗，并配置深色标题与垂直内容区域。</summary>
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
        Image panelImage = panel.GetComponent<Image>();
        panelImage.sprite = WhiteUiSprite();
        panelImage.color = Color.white;
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

    /// <summary>创建白色弹窗内的浅灰按钮和居中深色文字。</summary>
    private Button CreateButton(Transform parent, string label)
    {
        GameObject obj = new GameObject("Choice", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        obj.transform.SetParent(parent, false);
        obj.GetComponent<Image>().color = new Color(0.9f, 0.9f, 0.9f, 1f);
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

    /// <summary>创建弹窗使用的深色 TextMeshPro 文字并应用统一中文字体。</summary>
    private static TMP_Text CreateText(string name, Transform parent, float size)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        TMP_Text text = obj.GetComponent<TMP_Text>();
        text.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/SourceHanSansSC-Regular SDF") ?? TMP_Settings.defaultFontAsset;
        text.fontSize = size; text.color = new Color(0.08f, 0.08f, 0.1f, 1f); text.raycastTarget = false;
        return text;
    }

    private static void SetRect(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax;
    }

    private static string RarityName(string rarityId)
    {
        return ContentRuntime.IsLoaded && ContentRuntime.Registry.TryGetRarity(rarityId, out RarityDefinition rarity)
            ? rarity.DisplayName : rarityId;
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
        whiteUiSprite.name = "HandCardWhiteUi";
        return whiteUiSprite;
    }

    private static Canvas FindScreenCanvas()
    {
        foreach (Canvas canvas in FindObjectsByType<Canvas>())
            if (canvas.renderMode != RenderMode.WorldSpace) return canvas;
        return null;
    }
}
