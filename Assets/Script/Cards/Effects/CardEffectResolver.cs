using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 统一验证卡牌费用和执行条件，然后扣除行动点并调用对应效果处理器。
/// HandCardSystem 只有在这里返回成功后才会把卡牌移出手牌。
/// </summary>
public class CardEffectResolver : MonoBehaviour
{
    [SerializeField] private BoardClickController actionPointController;
    [SerializeField] private Unit player;
    [SerializeField] private Unit enemy;

    private readonly Dictionary<CardEffectType, ICardEffectHandler> handlers =
        new Dictionary<CardEffectType, ICardEffectHandler>();

    private void Awake()
    {
        handlers[CardEffectType.Heal] = new HealCardEffectHandler();
        handlers[CardEffectType.GainBlock] = new BlockCardEffectHandler();
        handlers[CardEffectType.PhysicalDamage] = new PhysicalDamageCardEffectHandler();
        handlers[CardEffectType.FireDamage] = new FireDamageCardEffectHandler();
        handlers[CardEffectType.Move] = new RunCardEffectHandler();
        ResolveSceneReferences();
    }

    public void Bind(BoardClickController actionPoints, Unit playerUnit, Unit enemyUnit)
    {
        actionPointController = actionPoints;
        player = playerUnit;
        enemy = enemyUnit;
    }

    public bool TryPlay(CardData card, Unit target = null)
    {
        if (card == null)
        {
            return false;
        }

        ResolveSceneReferences();
        if (actionPointController == null)
        {
            Debug.LogError("无法结算卡牌：没有找到行动点控制器。", this);
            return false;
        }

        if (!handlers.TryGetValue(card.effectType, out ICardEffectHandler handler))
        {
            Debug.LogError($"卡牌 {card.cardName} 没有对应的效果处理器。", card);
            return false;
        }

        if (card.RequiresTarget && target == null)
        {
            Debug.Log($"打出 {card.cardName} 需要先选择范围内的敌人。", card);
            return false;
        }

        CardEffectContext context =
            new CardEffectContext(actionPointController, player, enemy, target);

        if (actionPointController.CurrentActionPoints < card.actionPointCost)
        {
            Debug.Log($"行动点不足，无法打出 {card.cardName}。", card);
            return false;
        }

        if (!handler.CanExecute(card, context))
        {
            Debug.Log($"当前无法执行 {card.cardName} 的效果。", card);
            return false;
        }

        if (!actionPointController.TrySpendActionPoints(card.actionPointCost))
        {
            return false;
        }

        handler.Execute(card, context);
        return true;
    }

    private void ResolveSceneReferences()
    {
        if (actionPointController == null)
        {
            actionPointController = FindAnyObjectByType<BoardClickController>();
        }

        if (player == null)
        {
            GameObject playerObject = GameObject.Find("Player");
            player = playerObject == null ? null : playerObject.GetComponent<Unit>();
        }

        if (enemy == null)
        {
            GameObject enemyObject = GameObject.Find("Monster");
            enemy = enemyObject == null ? null : enemyObject.GetComponent<Unit>();
        }
    }
}
