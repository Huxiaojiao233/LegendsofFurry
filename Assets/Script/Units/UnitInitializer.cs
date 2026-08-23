using TMPro;
using UnityEngine;

/// <summary>
/// 把双方单位放到棋盘上最近的空格，并设置阵营。
/// 随机地图可能导致偏好坐标是空洞，因此会向外搜索。
/// </summary>
public class UnitInitializer : MonoBehaviour
{
    [SerializeField] private BoardGenerator board;
    [SerializeField] private Unit player;
    [SerializeField] private Unit enemy;

    [SerializeField] private Vector2Int playerPreferredCell = new Vector2Int(3, 2);
    [SerializeField] private Vector2Int enemyPreferredCell = new Vector2Int(5, 6);

    [Header("战斗 UI（可留空，按名称回退）")]
    [SerializeField] private TMP_Text playerHealthText;
    [SerializeField] private TMP_Text playerArmorText;
    [SerializeField] private TMP_Text enemyHealthText;
    [SerializeField] private TMP_Text enemyArmorText;

    private void Start()
    {
        ResolveReferences();
        BindCombatUI();
        PlaceUnit(player, UnitFaction.Player, playerPreferredCell);
        PlaceUnit(enemy, UnitFaction.Enemy, enemyPreferredCell);
    }

    private void ResolveReferences()
    {
        if (board == null)
        {
            board = FindAnyObjectByType<BoardGenerator>();
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

    private void BindCombatUI()
    {
        if (playerHealthText == null)
        {
            playerHealthText = FindText("T_Self_HP");
        }

        if (playerArmorText == null)
        {
            playerArmorText = FindText("T_Self_Attr");
        }

        if (enemyHealthText == null)
        {
            enemyHealthText = FindText("T_Enemy_HP");
        }

        if (enemyArmorText == null)
        {
            enemyArmorText = FindText("T_Enemy_Attr");
        }

        player?.BindCombatUI(playerHealthText, playerArmorText);
        enemy?.BindCombatUI(enemyHealthText, enemyArmorText);
    }

    private void PlaceUnit(Unit unit, UnitFaction faction, Vector2Int preferred)
    {
        if (unit == null || board == null)
        {
            return;
        }

        unit.SetFaction(faction);
        unit.SetBoard(board);

        if (!board.TryFindNearestFreeCell(preferred, unit, out Vector2Int cell))
        {
            Debug.LogWarning($"{unit.name} 找不到可放置的格子。", unit);
            return;
        }

        unit.MoveTo(cell.x, cell.y);
    }

    private static TMP_Text FindText(string objectName)
    {
        GameObject found = GameObject.Find(objectName);
        return found == null ? null : found.GetComponent<TMP_Text>();
    }
}
