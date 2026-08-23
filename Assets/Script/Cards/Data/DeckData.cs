using System.Collections.Generic;
using UnityEngine;

/// <summary>牌库中的一个配置项：指定卡牌及其加入牌库的张数。</summary>
[System.Serializable]
public class DeckEntry
{
    public CardData card;

    [Min(1)]
    public int amount = 1;
}

[CreateAssetMenu(fileName = "NewDeck", menuName = "Legends Of Furry/Deck Data")]
/// <summary>一套可复用的初始牌库配置。</summary>
public class DeckData : ScriptableObject
{
    public List<DeckEntry> cards = new List<DeckEntry>();

    /// <summary>
    /// 将“卡牌 + 数量”配置展开成实际抽牌列表。
    /// 同一种 CardData 可以在结果中出现多次，代表多张相同卡牌。
    /// </summary>
    public List<CardData> CreateDrawPile()
    {
        List<CardData> result = new List<CardData>();

        foreach (DeckEntry entry in cards)
        {
            if (entry == null || entry.card == null)
            {
                continue;
            }

            for (int i = 0; i < Mathf.Max(1, entry.amount); i++)
            {
                result.Add(entry.card);
            }
        }

        return result;
    }
}
