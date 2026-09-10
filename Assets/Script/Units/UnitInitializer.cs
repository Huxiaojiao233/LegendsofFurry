using TMPro;
using UnityEngine;

/// <summary>
/// 把生命、护甲绑定到各自专用文本。棋子由 BattleRoster 运行时生成并落盘，这里不再按名称找场景棋子。
/// </summary>
public class UnitInitializer : MonoBehaviour
{
    [Header("战斗 UI（可留空，按名称回退）")]
    [SerializeField] private TMP_Text playerHealthText;
    [SerializeField] private TMP_Text playerArmorText;
    [SerializeField] private TMP_Text enemyHealthText;
    [SerializeField] private TMP_Text enemyArmorText;

    /// <summary>绑定生命与护甲专用文本到本场编制的主己方与主敌人。</summary>
    private void Start()
    {
        BindCombatUI();
    }

    /// <summary>把生命、护甲绑定到各自专用文本，绝不占用角色属性文本。</summary>
    private void BindCombatUI()
    {
        if (playerHealthText == null)
        {
            playerHealthText = FindText("T_Self_HP");
        }

        if (playerArmorText == null)
        {
            playerArmorText = FindText("T_Self_Armor");
        }

        if (enemyHealthText == null)
        {
            enemyHealthText = FindText("T_Enemy_HP");
        }

        if (enemyArmorText == null)
        {
            enemyArmorText = FindText("T_Enemy_Armor");
        }

        BattleUnits.PrimaryAlly?.BindCombatUI(playerHealthText, playerArmorText);
        BattleUnits.PrimaryEnemy?.BindCombatUI(enemyHealthText, enemyArmorText);
    }

    /// <summary>按场景对象名称查找 TextMeshPro 文本；未找到时返回 null。</summary>
    private static TMP_Text FindText(string objectName)
    {
        GameObject found = GameObject.Find(objectName);
        return found == null ? null : found.GetComponent<TMP_Text>();
    }
}
