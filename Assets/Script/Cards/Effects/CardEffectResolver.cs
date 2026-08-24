using System.Collections.Generic;
using UnityEngine;

public sealed class CardPlayResult
{
    public bool Success;
    public bool EndTurn;
    public int FreeMoveSteps;
    public CardFamily RemoveFamily = CardFamily.None;
}

public class CardEffectResolver : MonoBehaviour
{
    [SerializeField] private BoardClickController actionPointController;
    [SerializeField] private HandCardSystem handCardSystem;
    [SerializeField] private Unit player;
    [SerializeField] private Unit enemy;

    public void Bind(BoardClickController actionPoints, Unit playerUnit, Unit enemyUnit)
    {
        actionPointController = actionPoints;
        player = playerUnit;
        enemy = enemyUnit;
        handCardSystem ??= FindAnyObjectByType<HandCardSystem>();
    }

    public bool CanAfford(CardInstance instance)
    {
        ResolveReferences();
        if (instance?.Data == null || actionPointController == null || player == null) return false;
        CardData card = instance.Data;
        if (card.unplayable) return false;
        if (card.isAttack && player.State.Has(CombatStatus.CannotAttack)) return false;
        if (instance.FreePlay) return true;
        int actionCost = card.spendsAllActionPoints ? actionPointController.CurrentActionPoints : card.actionPointCost;
        if (card.spendsAllActionPoints && actionCost <= 0) return false;
        if (actionPointController.CurrentActionPoints < actionCost) return false;
        int manaCost = card.spendsAllMana ? player.State.Mana : card.manaCost;
        return player.State.Mana >= manaCost;
    }

    public CardPlayResult TryPlay(CardInstance instance, Unit target = null,
        BoardCell targetCell = null, Vector2Int? direction = null)
    {
        CardPlayResult result = new CardPlayResult();
        ResolveReferences();
        if (!CanAfford(instance)) return result;

        CardData card = instance.Data;
        if (!ValidateTarget(card, target, targetCell, direction)) return result;

        int xAction = actionPointController.CurrentActionPoints;
        int xMana = player.State.Mana;
        if (!instance.FreePlay)
        {
            int actionCost = card.spendsAllActionPoints ? xAction : card.actionPointCost;
            int manaCost = card.spendsAllMana ? xMana : card.manaCost;
            if (!actionPointController.TrySpendActionPoints(actionCost) || !player.State.TrySpendMana(manaCost))
                return result;
        }

        result.Success = ResolveCard(instance, target, targetCell, direction, xAction, xMana, result);
        return result;
    }

    // 旧接口兼容旧代码/资产。
    public bool TryPlay(CardData card, Unit target = null)
    {
        return TryPlay(new CardInstance(card), target).Success;
    }

    private bool ResolveCard(CardInstance card, Unit target, BoardCell cell,
        Vector2Int? direction, int xAction, int xMana, CardPlayResult result)
    {
        string id = card.Data.cardId;
        switch (id)
        {
            case "hit_01": Damage(card, target, 4, DamageType.Normal); break;
            case "block_01": player.AddArmor(3); break;
            case "run_01": result.FreeMoveSteps = 3; break;
            case "heal_01": player.Heal(2); break;

            case "sword_slash": DamageDirection(card, direction.Value, 5, false); break;
            case "sword_thrust":
                bool armored = target.Armor > 0;
                Damage(card, target, 4, DamageType.Normal, false);
                if (armored) Damage(card, target, 2, DamageType.Normal, false, 1, false);
                FinishAttackModifiers(card); break;
            case "sword_flurry":
                if (xAction > 0) Damage(card, target, 2, DamageType.Normal, true, xAction);
                else FinishAttackModifiers(card);
                break;
            case "sword_pommel": Damage(card, target, 3, DamageType.Normal); KnockBack(target); break;
            case "sword_charge": player.State.Add(CombatStatus.SwordCharge, 4); result.EndTurn = true; break;
            case "sword_guard": Damage(card, target, 4, DamageType.Normal); player.AddArmor(3); break;
            case "sword_desperate_throw": Damage(card, target, 10, DamageType.Normal); result.RemoveFamily = CardFamily.Sword; break;

            case "shield_protect": player.AddArmor(5); break;
            case "shield_polish": player.State.Add(CombatStatus.BlockRetention); break;
            case "shield_bash": Damage(card, target, 2, DamageType.Normal); player.AddArmor(5); break;
            case "shield_raise": player.State.Add(CombatStatus.RaiseShieldPending); break;
            case "shield_throw": target.State.Add(CombatStatus.NormalImmunity); result.RemoveFamily = CardFamily.Shield; break;

            case "bow_shot": Damage(card, target, 4, DamageType.Normal); break;
            case "bow_elbow": Damage(card, target, 3, DamageType.Normal); handCardSystem.DrawCards(1, true); break;
            case "bow_roll": Damage(card, target, 4, DamageType.Normal); result.FreeMoveSteps = 1; break;
            case "bow_charge_snipe":
                Damage(card, target, 6 + card.PersistentDamageBonus, DamageType.Normal);
                if (target == player) card.PersistentDamageBonus += 4;
                break;
            case "bow_scatter":
                Damage(card, target, 4, DamageType.Normal); break;
            case "bow_rain":
                foreach (Unit unit in UnitsInArea(cell.Coordinate, 1)) Damage(card, unit, 3, DamageType.True, false);
                FinishAttackModifiers(card); break;
            case "bow_piercing": DamageDirection(card, direction.Value, 10, true); break;

            case "staff_fireball": Damage(card, target, 6, DamageType.Fire); break;
            case "staff_ice": Damage(card, target, 3, DamageType.Ice); if (!target.State.Has(CombatStatus.Warming)) target.State.Add(CombatStatus.Cold); break;
            case "staff_lightning":
                Damage(card, target, 4, DamageType.Lightning, false);
                foreach (Unit unit in UnitsInManhattanRange(target.Position, 1))
                    if (unit != target) Damage(card, unit, 2, DamageType.Lightning, false, 1, false);
                FinishAttackModifiers(card); break;
            case "staff_swing": Damage(card, target, 4, DamageType.Normal); break;
            case "staff_meditate": player.State.TryGainMana(2); player.State.Add(CombatStatus.CannotAttack, 1, 1); break;
            case "staff_arcane": Damage(card, target, 5, DamageType.True); break;
            case "staff_storm": Damage(card, target, 3 * xMana, DamageType.Normal); player.State.ScheduleManaExhaustion(3); break;

            case "scepter_elbow": Damage(card, target, 4, DamageType.Normal); break;
            case "scepter_tap": Damage(card, target, 2, DamageType.Normal); target.State.Add(CombatStatus.Haze, 1, 1); break;
            case "scepter_shine": Damage(card, target, 3, DamageType.Light); target.State.ClearNegative(); break;
            case "scepter_heal": target.Heal(5); break;
            case "scepter_dispel": target.State.ClearAll(); break;
            case "scepter_light_shield": target.AddArmor(10); break;
            case "scepter_inner_fire": target.State.Add(CombatStatus.HeartFire, 1, 2); break;

            case "dagger_combo": Damage(card, target, 2, DamageType.Normal, true, 2); break;
            case "dagger_thrust": Damage(card, target, 3, DamageType.True); break;
            case "dagger_cut": Damage(card, target, 1, DamageType.Normal); target.State.Add(CombatStatus.Broken); break;
            case "dagger_assassinate": Damage(card, target, 4, DamageType.Normal); result.FreeMoveSteps = 2; break;
            case "dagger_throat":
                Damage(card, target, 5, DamageType.True);
                if (!target.IsAlive) actionPointController.GainActionPoints(2);
                break;
            case "dagger_throw": Damage(card, target, 4, DamageType.Normal); target.State.Add(CombatStatus.Poison, 3); break;
            case "dagger_wrist": Damage(card, target, 4, DamageType.True); target.State.Add(CombatStatus.Exhaustion, 1, 3); break;

            case "emerald_glimmer": handCardSystem.DrawCards(1, true); break;
            case "emerald_flash": handCardSystem.DrawCards(1, true); actionPointController.GainActionPoints(1); break;
            case "emerald_notice": player.State.Add(CombatStatus.DodgeNextNormal); actionPointController.GainActionPoints(1); break;
            case "emerald_brilliant": player.State.Add(CombatStatus.NormalImmunity); actionPointController.GainActionPoints(2); break;
            case "crystal_preview": if (target == player) handCardSystem.ShowTopCardsForDiscard(3); break;
            case "crystal_divination": if (target == player) handCardSystem.QueueFreeTopCards(1); break;
            case "crystal_inference": handCardSystem.QueueFreeTopCards(2); break;
            case "crystal_channel": Debug.Log("【通灵】属性牌库暂时跳过，效果不结算。", this); break;
            case "cross_glimmer": target.State.Add(CombatStatus.Regeneration, 3); break;
            case "cross_flash": target.State.ClearNegative(); target.State.Add(CombatStatus.Regeneration, 3); break;
            case "cross_brilliant": if (!target.IsAlive) target.Revive(5); break;
            case "cloak_worn": result.FreeMoveSteps = 1; break;
            case "cloak_clear": handCardSystem.DrawCards(1, true); result.FreeMoveSteps = 1; break;
            case "cloak_cover": handCardSystem.DrawCards(1, true); actionPointController.GainActionPoints(1); break;
            case "cloak_hide": actionPointController.IncreaseMaximumActionPoints(1); break;
            default:
                if (id.StartsWith("identify_")) handCardSystem.ResolveIdentify(card.Data.sourcePool);
                else if (id == "crystal_shatter") { }
                else return false;
                break;
        }
        return true;
    }

    private bool ValidateTarget(CardData card, Unit target, BoardCell cell, Vector2Int? direction)
    {
        if (card.targetMode == CardTargetMode.Self) return true;
        if (player == null || player.Board == null) return false;
        int effectiveRange = card.range + (card.family == CardFamily.Bow && GameSession.SelectedClass == HeroClass.Ranger ? 1 : 0);

        if (card.targetMode == CardTargetMode.Direction) return direction.HasValue && direction.Value != Vector2Int.zero;
        Vector2Int targetPosition = target != null ? target.Position : cell != null ? cell.Coordinate : new Vector2Int(int.MinValue, int.MinValue);
        int distance = Mathf.Abs(player.Position.x - targetPosition.x) + Mathf.Abs(player.Position.y - targetPosition.y);
        if (distance > effectiveRange) return false;
        if (card.targetMode == CardTargetMode.Unit)
        {
            if (target == null) return false;
            if (!target.IsAlive && card.cardId != "cross_brilliant") return false;
            if (!HasLineOfSight(player, target) && card.family != CardFamily.Sword && card.family != CardFamily.Dagger && card.family != CardFamily.Shield) return false;
        }
        if (card.targetMode == CardTargetMode.AreaCell)
            return cell != null && HasLineOfSightToCell(player, cell.Coordinate);
        return true;
    }

    private void Damage(CardInstance card, Unit target, int baseDamage, DamageType type,
        bool finishModifiers = true, int hitCount = 1, bool allowQuick = true)
    {
        if (target == null) return;
        int quickExtra = allowQuick && player.State.Has(CombatStatus.Quick) && card.Data.isAttack ? 1 : 0;
        int totalHits = Mathf.Max(1, hitCount) + quickExtra;
        int swordCharge = card.Data.family == CardFamily.Sword ? player.State.Get(CombatStatus.SwordCharge) : 0;
        for (int i = 0; i < totalHits; i++)
        {
            int amount = baseDamage;
            if (player.State.Has(CombatStatus.Sharp)) amount += 1;
            if (i == 0 && swordCharge > 0) amount += swordCharge;
            amount = Mathf.Max(0, amount - player.State.Get(CombatStatus.Haze));
            if (player.State.Has(CombatStatus.HeartFire)) amount = Mathf.FloorToInt(amount * 1.1f);
            target.TakeTypedDamage(amount, type, player);
        }
        if (finishModifiers) FinishAttackModifiers(card);
    }

    private void FinishAttackModifiers(CardInstance card)
    {
        if (!card.Data.isAttack) return;
        if (player.State.Has(CombatStatus.Sharp)) player.State.Reduce(CombatStatus.Sharp);
        if (player.State.Has(CombatStatus.Quick)) player.State.Reduce(CombatStatus.Quick);
        if (card.Data.family == CardFamily.Sword && player.State.Has(CombatStatus.SwordCharge)) player.State.Remove(CombatStatus.SwordCharge);
    }

    private void DamageDirection(CardInstance card, Vector2Int direction, int damage, bool pierces)
    {
        BoardGenerator board = player.Board;
        Vector2Int perpendicular = new Vector2Int(-direction.y, direction.x);
        if (!pierces)
        {
            foreach (Vector2Int coordinate in new[] { player.Position + direction, player.Position + direction + perpendicular, player.Position + direction - perpendicular })
                if (board.TryGetOccupant(coordinate.x, coordinate.y, out Unit unit)) Damage(card, unit, damage, DamageType.Normal, false);
            FinishAttackModifiers(card);
            return;
        }

        Vector2Int current = player.Position + direction;
        while (board.TryGetCell(current.x, current.y, out _))
        {
            if (board.TryGetOccupant(current.x, current.y, out Unit unit)) Damage(card, unit, damage, DamageType.Normal, false);
            current += direction;
        }
        FinishAttackModifiers(card);
    }

    private void KnockBack(Unit target)
    {
        Vector2Int delta = target.Position - player.Position;
        Vector2Int direction = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)
            ? new Vector2Int(delta.x == 0 ? 0 : (int)Mathf.Sign(delta.x), 0)
            : new Vector2Int(0, delta.y == 0 ? 0 : (int)Mathf.Sign(delta.y));
        Vector2Int destination = target.Position + direction;
        if (target.Board.TryGetCell(destination.x, destination.y, out _) && !target.Board.IsOccupied(destination.x, destination.y, target))
            target.MoveToAnimated(destination.x, destination.y);
    }

    private IEnumerable<Unit> UnitsInArea(Vector2Int center, int radius)
    {
        foreach (Unit unit in FindObjectsByType<Unit>())
            if (unit.IsAlive && Mathf.Max(Mathf.Abs(unit.Position.x - center.x), Mathf.Abs(unit.Position.y - center.y)) <= radius) yield return unit;
    }

    private IEnumerable<Unit> UnitsInManhattanRange(Vector2Int center, int range, int maximum = int.MaxValue)
    {
        int count = 0;
        foreach (Unit unit in FindObjectsByType<Unit>())
        {
            if (!unit.IsAlive || Mathf.Abs(unit.Position.x - center.x) + Mathf.Abs(unit.Position.y - center.y) > range) continue;
            yield return unit;
            if (++count >= maximum) yield break;
        }
    }

    private bool HasLineOfSight(Unit from, Unit to)
    {
        Vector2Int delta = to.Position - from.Position;
        int steps = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
        for (int i = 1; i < steps; i++)
        {
            int x = Mathf.RoundToInt(Mathf.Lerp(from.Position.x, to.Position.x, (float)i / steps));
            int y = Mathf.RoundToInt(Mathf.Lerp(from.Position.y, to.Position.y, (float)i / steps));
            if (!from.Board.TryGetCell(x, y, out _) || from.Board.IsOccupied(x, y)) return false;
        }
        return true;
    }

    private bool HasLineOfSightToCell(Unit from, Vector2Int destination)
    {
        Vector2Int delta = destination - from.Position;
        int steps = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
        for (int i = 1; i < steps; i++)
        {
            int x = Mathf.RoundToInt(Mathf.Lerp(from.Position.x, destination.x, (float)i / steps));
            int y = Mathf.RoundToInt(Mathf.Lerp(from.Position.y, destination.y, (float)i / steps));
            if (!from.Board.TryGetCell(x, y, out _) || from.Board.IsOccupied(x, y)) return false;
        }
        return true;
    }

    private void ResolveReferences()
    {
        actionPointController ??= FindAnyObjectByType<BoardClickController>();
        handCardSystem ??= FindAnyObjectByType<HandCardSystem>();
        if (player == null) player = GameObject.Find("Player")?.GetComponent<Unit>();
        if (enemy == null) enemy = GameObject.Find("Monster")?.GetComponent<Unit>();
    }
}
