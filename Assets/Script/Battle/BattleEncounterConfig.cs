using UnityEngine;

/// <summary>
/// 可选的本场战斗编制。不挂此组件时，使用内容包 GameSettings 的 1 名己方 + 1 名敌人。
/// 己方角色 ID 最多取 3 个；敌人数量不限，每种敌人写一次 ID，可重复。
/// </summary>
public class BattleEncounterConfig : MonoBehaviour
{
    [Header("己方角色 ID（空则用内容包 PlayerCharacterId，最多 3 名）")]
    [SerializeField] private string[] allyCharacterIds = { };

    [Header("敌人角色 ID（空则用内容包 EnemyCharacterId）")]
    [SerializeField] private string[] enemyCharacterIds = { };

    [Header("出生格（与上面数组按下标对应；越界则向外找空格）")]
    [SerializeField] private Vector2Int[] allySpawnCells =
    {
        new Vector2Int(3, 2),
        new Vector2Int(2, 2),
        new Vector2Int(4, 2)
    };

    [SerializeField] private Vector2Int[] enemySpawnCells =
    {
        new Vector2Int(5, 6),
        new Vector2Int(6, 6),
        new Vector2Int(4, 6)
    };

    public string[] AllyCharacterIds => allyCharacterIds;
    public string[] EnemyCharacterIds => enemyCharacterIds;
    public Vector2Int[] AllySpawnCells => allySpawnCells;
    public Vector2Int[] EnemySpawnCells => enemySpawnCells;
}
