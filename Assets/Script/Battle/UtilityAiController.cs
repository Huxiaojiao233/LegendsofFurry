using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;
using UnityEngine;

/// <summary>所有 AI 单位共用的候选生成、合法性过滤、Utility 评分和逐行动重评估控制器。</summary>
[DisallowMultipleComponent]
public sealed class UtilityAiController : MonoBehaviour, IActionPointPool, IContentCardZoneService, IContentTargetQueryService
{
    private readonly List<CardInstance> hand = new List<CardInstance>();
    private readonly List<CardInstance> draw = new List<CardInstance>();
    private readonly List<CardInstance> discard = new List<CardInstance>();
    private readonly List<CardInstance> exhaust = new List<CardInstance>();
    private Unit owner;
    private string deckId;
    private bool initialized;
    private int maximumActionPoints = 3;

    public int CurrentActionPoints { get; private set; }
    public int MaxActionPoints => maximumActionPoints;

    public void Configure(string deployedDeckId)
    {
        owner = GetComponent<Unit>();
        deckId = deployedDeckId ?? string.Empty;
        initialized = false;
        CombatCardZoneRegistry.Register(owner, this);
    }

    /// <summary>开始一次 AI 回合；每执行一个行动后都会重新生成并评分候选。</summary>
    public IEnumerator RunTurn(float stepPause = 0.08f)
    {
        bool firstTurn = !initialized;
        EnsureDeck();
        if (owner == null || !owner.IsAlive) yield break;
        maximumActionPoints = owner.State.EffectiveActionPointMaximum(
            Mathf.Max(0, owner.Definition?.InitialActionPoints ?? 3));
        CurrentActionPoints = maximumActionPoints;
        if (!firstTurn) DrawCards(owner.Definition?.DrawPerTurn ?? 5, true);

        for (int guard = 0; guard < 32 && owner.IsAlive && CurrentActionPoints > 0; guard++)
        {
            AiCandidate best = BuildCandidates().OrderByDescending(item => item.Score)
                .ThenBy(item => item.StableKey, StringComparer.Ordinal).FirstOrDefault();
            if (best == null || best.Score <= 0f) break;
            if (best.Card != null)
            {
                if (!ExecuteCard(best.Card, best.Target, out CardPlayResult result)) break;
                if (result.FreeMoveSteps > 0)
                    yield return MoveToward(best.Target ?? NearestOpponent(), result.FreeMoveSteps, 0, stepPause);
                if (result.EndTurn) break;
            }
            else
            {
                yield return MoveToward(best.Target, 1, 1, stepPause);
            }
            if (stepPause > 0f) yield return new WaitForSeconds(stepPause);
        }

        discard.AddRange(hand);
        hand.Clear();
    }

    private IEnumerable<AiCandidate> BuildCandidates()
    {
        AiWeights weights = ResolveWeights();
        foreach (CardInstance card in hand.ToArray())
        {
            if (!CanPay(card) || !ContentCardEffectExecutor.CanExecuteOnPlay(card.Definition)) continue;
            CardTargetRule rule = card.Definition.Target;
            if (rule.SelectionMode is "none" or "self")
            {
                Unit target = rule.SelectionMode == "self" ? owner : null;
                yield return new AiCandidate(card, target, ScoreCard(card.Definition, target, weights));
                continue;
            }
            if (rule.SelectionMode != "unit") continue;
            foreach (Unit target in GetUnits())
            {
                if (!IsLegalTarget(card.Definition, target)) continue;
                yield return new AiCandidate(card, target, ScoreCard(card.Definition, target, weights));
            }
        }

        Unit opponent = NearestOpponent();
        if (opponent != null && CanMoveToward(opponent))
        {
            int distance = Manhattan(owner, opponent);
            float score = weights.Approach * Mathf.Max(0.1f, distance - weights.PreferredRange + 1f);
            yield return new AiCandidate(null, opponent, score);
        }
    }

    private bool ExecuteCard(CardInstance card, Unit target, out CardPlayResult result)
    {
        result = new CardPlayResult();
        int beforeAction = CurrentActionPoints;
        int beforeMana = owner.State.Mana;
        if (!ContentCardPlayRules.TryCalculateCost(card.Definition, beforeAction, beforeMana, card.FreePlay,
                out int actionCost, out int manaCost)) return false;
        if (!card.FreePlay && (!TrySpendActionPoints(actionCost) || !owner.State.TrySpendMana(manaCost))) return false;
        ContentCardExecutionContext context = new ContentCardExecutionContext(
            card, owner, target, null, null, this, this, result, true,
            beforeAction, beforeMana, actionCost, manaCost, targetQueryService: this);
        result.ExecutionContext = context;
        result.Success = ContentCardEffectExecutor.TryExecuteOnPlay(context);
        hand.Remove(card);
        (card.Definition.ExhaustOnPlay || card.Data.exhaust ? exhaust : discard).Add(card);
        return result.Success;
    }

    private float ScoreCard(CardDefinition card, Unit target, AiWeights weights)
    {
        HashSet<string> tags = new HashSet<string>(card.Tags ?? new List<string>(), StringComparer.Ordinal);
        float score = card.AiBaseScore;
        if (card.IsAttack || tags.Contains("attack") || tags.Contains("damage"))
        {
            score += 4f * weights.Attack;
            if (target != null && target.CurrentHealth <= Mathf.Max(1, owner.AttackDamage)) score += 6f * weights.Kill;
        }
        if (tags.Contains("heal"))
            score += (owner.MaxHealth - owner.CurrentHealth) / (float)Mathf.Max(1, owner.MaxHealth) * 8f * weights.Healing;
        if (tags.Contains("defense") || tags.Contains("armor") || tags.Contains("buff"))
            score += 3f * weights.Defense;
        if (tags.Contains("move") || tags.Contains("mobility"))
            score += 2f * weights.Approach;
        if (tags.Contains("retreat"))
            score += owner.CurrentHealth / (float)Mathf.Max(1, owner.MaxHealth) <= weights.LowHealthThreshold
                ? 6f * weights.Retreat : 0f;
        if (target != null && target.Faction == owner.Faction && card.IsAttack) score -= 1000f;
        return score - Mathf.Max(0, card.Cost.ActionCost - 1) * 0.25f;
    }

    private bool IsLegalTarget(CardDefinition card, Unit target)
    {
        if (target == null) return false;
        int distance = Manhattan(owner, target);
        ContentRuleQuery range = ContentRuleQueryRuntime.Evaluate(new ContentRuleQuery(
            ContentRuleQueryKeys.TargetRange, owner, target, card.Target.Range, card));
        CardDefinition effectiveCard = new CardDefinition { Target = new CardTargetRule
        {
            SelectionMode = card.Target.SelectionMode, Range = Mathf.Max(0, range.Value),
            TeamFilter = card.Target.TeamFilter, LifeStateFilter = card.Target.LifeStateFilter,
            RequiresLineOfSight = card.Target.RequiresLineOfSight, AllowSelf = card.Target.AllowSelf
        }};
        return ContentCardPlayRules.ValidateTarget(effectiveCard, new ContentCardTargetSelection
        {
            HasUnit = true,
            HasCell = true,
            Distance = distance,
            TargetIsSelf = target == owner,
            TargetHasSameFaction = target.Faction == owner.Faction,
            TargetIsAlive = target.IsAlive,
            HasLineOfSight = !card.Target.RequiresLineOfSight || HasLineOfSight(target)
        });
    }

    private bool HasLineOfSight(Unit target)
    {
        if (owner?.Board == null || target == null) return false;
        Vector2Int delta = target.Position - owner.Position;
        int steps = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
        for (int index = 1; index < steps; index++)
        {
            int x = Mathf.RoundToInt(Mathf.Lerp(owner.Position.x, target.Position.x, index / (float)steps));
            int y = Mathf.RoundToInt(Mathf.Lerp(owner.Position.y, target.Position.y, index / (float)steps));
            if (!owner.Board.TryGetCell(x, y, out _) || owner.Board.IsOccupied(x, y)) return false;
        }
        return true;
    }

    private IEnumerator MoveToward(Unit target, int maximumSteps, int actionCostPerStep, float pause)
    {
        if (target == null || owner?.Board == null) yield break;
        List<Vector2Int> path = new List<Vector2Int>();
        if (!owner.Board.TryFindPath(owner.Position, target.Position, owner, path) || path.Count <= 1) yield break;
        for (int index = 1; index < path.Count && index <= maximumSteps; index++)
        {
            Vector2Int next = path[index];
            if (next == target.Position || actionCostPerStep > CurrentActionPoints) break;
            if (actionCostPerStep > 0 && !TrySpendActionPoints(actionCostPerStep)) break;
            if (!owner.MoveToAnimated(next.x, next.y, this)) break;
            while (owner.IsMoving) yield return null;
            if (pause > 0f) yield return new WaitForSeconds(pause);
        }
    }

    private bool CanMoveToward(Unit target) => CurrentActionPoints > 0 && target != null && Manhattan(owner, target) > 1;
    private static int Manhattan(Unit a, Unit b) => a == null || b == null ? int.MaxValue :
        Mathf.Abs(a.Position.x - b.Position.x) + Mathf.Abs(a.Position.y - b.Position.y);

    private Unit NearestOpponent()
    {
        return BattleRoster.Instance?.FindNearestLivingOpponent(owner) ?? GetUnits()
            .Where(item => item != null && item.IsAlive && item.Faction != owner.Faction)
            .OrderBy(item => Manhattan(owner, item)).FirstOrDefault();
    }

    private bool CanPay(CardInstance card) => card?.Definition != null && ContentCardPlayRules.TryCalculateCost(
        card.Definition, CurrentActionPoints, owner.State.Mana, card.FreePlay, out _, out _);

    private void EnsureDeck()
    {
        owner ??= GetComponent<Unit>();
        if (initialized) return;
        initialized = true;
        if (!ContentRuntime.IsLoaded || string.IsNullOrWhiteSpace(deckId)) return;
        draw.AddRange(ContentDeckFactory.CreateInstances(ContentRuntime.Registry, deckId));
        Shuffle(draw);
        DrawCards(owner.Definition?.StartingHandSize ?? 5, true);
    }

    private AiWeights ResolveWeights()
    {
        AiProfileDefinition profile = null;
        string profileId = owner.Definition?.AiProfileId;
        UnitAiTuningDefinition tuning = owner.Definition?.AiOverrides;
        if (owner.Definition?.IsBoss == true)
        {
            float ratio = owner.CurrentHealth / (float)Mathf.Max(1, owner.MaxHealth);
            BossPhaseDefinition phase = owner.Definition.BossPhases
                .Where(item => ratio <= item.MaximumHealthRatio)
                .OrderBy(item => item.MaximumHealthRatio).FirstOrDefault();
            if (phase != null)
            {
                if (!string.IsNullOrWhiteSpace(phase.AiProfileId)) profileId = phase.AiProfileId;
                tuning = phase.AiOverrides ?? tuning;
            }
        }
        if (ContentRuntime.IsLoaded && !string.IsNullOrWhiteSpace(profileId))
            ContentRuntime.Registry.TryGetAiProfile(profileId, out profile);
        profile ??= new AiProfileDefinition();
        return new AiWeights(profile, tuning);
    }

    public bool TrySpendActionPoints(int amount)
    {
        if (amount < 0 || amount > CurrentActionPoints) return false;
        CurrentActionPoints -= amount;
        return true;
    }
    public void GainActionPoints(int amount) => CurrentActionPoints = Mathf.Clamp(CurrentActionPoints + Mathf.Max(0, amount), 0, MaxActionPoints);
    public void IncreaseMaximumActionPoints(int amount)
    {
        maximumActionPoints += Mathf.Max(0, amount);
        CurrentActionPoints += Mathf.Max(0, amount);
    }

    public void DrawCards(int count, bool mandatory)
    {
        for (int index = 0; index < Mathf.Max(0, count); index++)
        {
            if (draw.Count == 0)
            {
                draw.AddRange(discard); discard.Clear(); Shuffle(draw);
            }
            if (draw.Count == 0) break;
            CardInstance card = draw[0]; draw.RemoveAt(0); hand.Add(card);
        }
    }

    public bool GenerateCards(ContentCardQuery query, int count, string destinationZone)
    {
        CardDefinition definition = ResolveCard(query);
        if (definition == null) return false;
        List<CardInstance> zone = Zone(destinationZone);
        if (zone == null) return false;
        for (int index = 0; index < Mathf.Max(0, count); index++) zone.Add(new CardInstance(definition));
        return true;
    }
    public int MoveCards(ContentCardQuery query, int count, string sourceZone, string destinationZone)
    {
        List<CardInstance> source = Zone(sourceZone); List<CardInstance> destination = Zone(destinationZone);
        if (source == null || destination == null || source == destination) return 0;
        CardInstance[] matches = source.Where(item => Matches(item, query)).Take(count <= 0 ? int.MaxValue : count).ToArray();
        foreach (CardInstance card in matches) { source.Remove(card); destination.Add(card); }
        return matches.Length;
    }
    public int RemoveCards(ContentCardQuery query, string zone)
    {
        List<CardInstance>[] zones = zone == ContentCardZoneKeys.All
            ? new[] { hand, draw, discard, exhaust } : new[] { Zone(zone) };
        int removed = 0;
        foreach (List<CardInstance> list in zones.Where(item => item != null)) removed += list.RemoveAll(item => Matches(item, query));
        return removed;
    }
    public void RevealTopCardsAndChooseDiscard(int count, Action onComplete) => onComplete?.Invoke();
    public void PlayTopCardsForFree(int count, Action onComplete) => onComplete?.Invoke();
    public bool AddCardToHandOrDiscard(CardInstance instance) { if (instance == null) return false; hand.Add(instance); return true; }
    public IReadOnlyList<Unit> GetUnits() => FindObjectsByType<Unit>().ToArray();
    public IReadOnlyList<CardInstance> GetCards(string zoneKey) => Zone(zoneKey)?.ToArray() ?? Array.Empty<CardInstance>();

    private List<CardInstance> Zone(string key) => key switch
    {
        ContentCardZoneKeys.Hand => hand, ContentCardZoneKeys.Draw => draw,
        ContentCardZoneKeys.Discard => discard, ContentCardZoneKeys.Exhaust => exhaust, _ => null
    };
    private CardDefinition ResolveCard(ContentCardQuery query)
    {
        if (query == null || !ContentRuntime.IsLoaded) return null;
        if (!string.IsNullOrWhiteSpace(query.cardId) && ContentRuntime.Registry.TryGetCard(query.cardId, out CardDefinition exact)) return exact;
        return ContentRuntime.Registry.Cards.FirstOrDefault(item => Matches(new CardInstance(item), query));
    }
    private static bool Matches(CardInstance card, ContentCardQuery query)
    {
        if (card?.Definition == null || query == null) return false;
        CardDefinition definition = card.Definition;
        return (string.IsNullOrWhiteSpace(query.cardId) || definition.CardId == query.cardId) &&
               (string.IsNullOrWhiteSpace(query.familyId) || definition.FamilyId == query.familyId) &&
               (string.IsNullOrWhiteSpace(query.rarityId) || definition.RarityId == query.rarityId) &&
               (string.IsNullOrWhiteSpace(query.tag) || definition.Tags.Contains(query.tag));
    }
    private static void Shuffle(List<CardInstance> cards)
    {
        for (int index = cards.Count - 1; index > 0; index--)
        {
            int swap = UnityEngine.Random.Range(0, index + 1);
            (cards[index], cards[swap]) = (cards[swap], cards[index]);
        }
    }

    private sealed class AiCandidate
    {
        public AiCandidate(CardInstance card, Unit target, float score)
        { Card = card; Target = target; Score = score; StableKey = (card?.Definition.CardId ?? "move") + ":" + (target?.InstanceId ?? ""); }
        public CardInstance Card { get; }
        public Unit Target { get; }
        public float Score { get; }
        public string StableKey { get; }
    }

    private readonly struct AiWeights
    {
        public AiWeights(AiProfileDefinition profile, UnitAiTuningDefinition tuning)
        {
            Attack = Pick(tuning?.AttackWeight, profile.AttackWeight); Defense = Pick(tuning?.DefenseWeight, profile.DefenseWeight);
            Healing = Pick(tuning?.HealingWeight, profile.HealingWeight); Approach = Pick(tuning?.ApproachWeight, profile.ApproachWeight);
            Retreat = Pick(tuning?.RetreatWeight, profile.RetreatWeight); Kill = Pick(tuning?.KillWeight, profile.KillWeight);
            PreferredRange = Pick(tuning?.PreferredRange, profile.PreferredRange); LowHealthThreshold = Pick(tuning?.LowHealthThreshold, profile.LowHealthThreshold);
        }
        public readonly float Attack, Defense, Healing, Approach, Retreat, Kill, PreferredRange, LowHealthThreshold;
        private static float Pick(float? value, float fallback) => value.HasValue && !float.IsNaN(value.Value) ? value.Value : fallback;
    }

    private void OnDestroy() => CombatCardZoneRegistry.Unregister(owner, this);
}
