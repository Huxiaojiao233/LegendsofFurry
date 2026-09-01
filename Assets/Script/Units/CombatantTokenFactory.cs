using UnityEngine;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;

/// <summary>
/// 从 Resources/CombatantToken 预制件实例化棋子；预制件缺失时用纯代码补齐网格和组件。
/// 贴图和外框颜色始终在生成后从内容包写入。
/// </summary>
public static class CombatantTokenFactory
{
    public const string PrefabResourcePath = "CombatantToken";

    /// <summary>生成一枚已绑定角色定义的棋子。</summary>
    public static Unit Spawn(CharacterDefinition definition, UnitFaction faction, string objectName)
    {
        GameObject prefab = Resources.Load<GameObject>(PrefabResourcePath);
        string resolvedName = !string.IsNullOrWhiteSpace(objectName)
            ? objectName
            : definition != null ? definition.CharacterId : "Combatant";
        GameObject instance = prefab != null
            ? Object.Instantiate(prefab)
            : new GameObject(resolvedName);
        instance.name = resolvedName;
        EnsureTokenComponents(instance);
        MeshRenderer renderer = instance.GetComponent<MeshRenderer>();
        if (renderer != null) renderer.enabled = false;
        Unit unit = instance.GetComponent<Unit>();
        unit.SetFaction(faction);
        if (definition != null)
        {
            unit.ConfigureCombatant(definition);
        }
        else
        {
            TokenVisualRuntime.Apply(unit, null);
        }

        return unit;
    }

    /// <summary>保证预制件或空物体具备网格、碰撞和 Unit。</summary>
    public static void EnsureTokenComponents(GameObject instance)
    {
        Require<Unit>(instance);
        MeshFilter filter = Require<MeshFilter>(instance);
        Mesh mesh = filter.sharedMesh;
        if (mesh == null || mesh.subMeshCount < 2)
        {
            filter.sharedMesh = CombatantTokenMesh.Create();
        }

        Require<MeshRenderer>(instance);
        BoxCollider box = Require<BoxCollider>(instance);
        box.size = new Vector3(CombatantTokenMesh.Width, CombatantTokenMesh.Height, CombatantTokenMesh.Width);
        box.center = Vector3.zero;
    }

    private static T Require<T>(GameObject instance) where T : Component
    {
        T component = instance.GetComponent<T>();
        return component != null ? component : instance.AddComponent<T>();
    }
}
