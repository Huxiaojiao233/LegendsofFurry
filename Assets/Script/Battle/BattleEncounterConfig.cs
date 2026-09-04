using UnityEngine;

/// <summary>
/// 可选的调试场景己方编制。正式敌人必须在世界编辑器的关卡中显式部署。
/// </summary>
public class BattleEncounterConfig : MonoBehaviour
{
    [Header("己方单位 ID（空则用内容包 PlayerUnitId，最多 3 名）")]
    [SerializeField] private string[] allyUnitIds = { };

    [Header("出生格（与上面数组按下标对应；越界则向外找空格）")]
    [SerializeField] private Vector2Int[] allySpawnCells =
    {
        new Vector2Int(3, 2),
        new Vector2Int(2, 2),
        new Vector2Int(4, 2)
    };

    public string[] AllyUnitIds => allyUnitIds;
    public Vector2Int[] AllySpawnCells => allySpawnCells;
}
