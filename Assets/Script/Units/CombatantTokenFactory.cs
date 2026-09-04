using UnityEngine;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;

/// <summary>
/// 从 Resources/Prefabs/Chess 实例化棋子；没有手摆模型时回退到程序生成的占位网格。
/// 顶面贴图和外框颜色在生成后从内容包写入。
/// </summary>
public static class CombatantTokenFactory
{
    public const string PrefabResourcePath = "Prefabs/Chess";
    public const string FallbackPrefabResourcePath = "Prefabs/CombatantToken";

    /// <summary>生成一枚已绑定角色定义的棋子。</summary>
    public static Unit Spawn(UnitDefinition definition, UnitFaction faction, string objectName)
    {
        GameObject prefab = Resources.Load<GameObject>(PrefabResourcePath)
            ?? Resources.Load<GameObject>(FallbackPrefabResourcePath)
            ?? Resources.Load<GameObject>("CombatantToken");
        string resolvedName = !string.IsNullOrWhiteSpace(objectName)
            ? objectName
            : definition != null ? definition.UnitId : "Combatant";
        GameObject instance = prefab != null
            ? Object.Instantiate(prefab)
            : new GameObject(resolvedName);
        instance.name = resolvedName;
        EnsureTokenComponents(instance);
        if (!HasAuthoredVisual(instance))
        {
            MeshRenderer renderer = instance.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.enabled = false;
        }

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

    /// <summary>根上要有 Unit 和可点中的碰撞；手摆模型不再被占位立方体覆盖。</summary>
    public static void EnsureTokenComponents(GameObject instance)
    {
        Require<Unit>(instance);
        if (HasAuthoredVisual(instance))
        {
            if (instance.GetComponentInChildren<Collider>() == null)
                FitBoxCollider(instance);
            return;
        }

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

    /// <summary>子物体上已有非占位网格时，视为场景里摆好的棋子模型。</summary>
    public static bool HasAuthoredVisual(GameObject instance)
    {
        if (instance == null) return false;
        MeshFilter[] filters = instance.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            Mesh mesh = filters[i].sharedMesh;
            if (mesh != null && mesh.name != "CombatantToken") return true;
        }

        return false;
    }

    public static MeshRenderer ResolveVisualRenderer(GameObject instance)
    {
        if (instance == null) return null;
        MeshRenderer[] renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
        MeshRenderer best = null;
        int bestSlots = -1;
        for (int i = 0; i < renderers.Length; i++)
        {
            MeshRenderer renderer = renderers[i];
            if (renderer == null) continue;
            int slots = renderer.sharedMaterials != null ? renderer.sharedMaterials.Length : 0;
            if (slots > bestSlots)
            {
                best = renderer;
                bestSlots = slots;
            }
        }

        return best;
    }

    private static void FitBoxCollider(GameObject instance)
    {
        BoxCollider box = Require<BoxCollider>(instance);
        MeshRenderer renderer = ResolveVisualRenderer(instance);
        if (renderer == null)
        {
            box.size = new Vector3(CombatantTokenMesh.Width, CombatantTokenMesh.Height, CombatantTokenMesh.Width);
            box.center = new Vector3(0f, CombatantTokenMesh.Height * 0.5f, 0f);
            return;
        }

        Bounds bounds = renderer.bounds;
        Vector3 localCenter = instance.transform.InverseTransformPoint(bounds.center);
        Vector3 localSize = instance.transform.InverseTransformVector(bounds.size);
        box.center = localCenter;
        box.size = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));
    }

    private static T Require<T>(GameObject instance) where T : Component
    {
        T component = instance.GetComponent<T>();
        return component != null ? component : instance.AddComponent<T>();
    }
}
