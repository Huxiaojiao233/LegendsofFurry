using UnityEngine;

/// <summary>本场战斗单位查询：优先编制表，兼容测试场景里按名称放置的单位。</summary>
public static class BattleUnits
{
    public static Unit PrimaryAlly =>
        BattleRoster.Instance != null && BattleRoster.Instance.PrimaryAlly != null
            ? BattleRoster.Instance.PrimaryAlly
            : GameObject.Find("Player")?.GetComponent<Unit>();

    public static Unit PrimaryEnemy =>
        BattleRoster.Instance != null && BattleRoster.Instance.PrimaryEnemy != null
            ? BattleRoster.Instance.PrimaryEnemy
            : GameObject.Find("Monster")?.GetComponent<Unit>();
}
