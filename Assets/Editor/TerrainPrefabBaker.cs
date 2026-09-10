#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.ProBuilder;

/// <summary>
/// 把 8 个地形 Prefab 的 ProBuilder 几何烘焙成可共享 Mesh，并给地形材质打开 GPU Instancing。
/// </summary>
public static class TerrainPrefabBaker
{
    public const string MeshFolder = TerrainPrefabSetup.TerrainPrefabFolder + "/Meshes";

    [MenuItem("Tools/Legends Of Furry/Bake Terrain Prefabs For Instancing")]
    public static void Bake()
    {
        EnsureFolder(TerrainPrefabSetup.TerrainPrefabFolder);
        EnsureFolder(MeshFolder);

        int bakedPrefabs = 0;
        int enabledMaterials = 0;
        var uniqueMaterials = new HashSet<Material>();
        for (int i = 0; i < TerrainPrefabSetup.Entries.Length; i++)
        {
            string prefabPath = $"{TerrainPrefabSetup.TerrainPrefabFolder}/{TerrainPrefabSetup.Entries[i].fileName}";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"找不到地形 Prefab：{prefabPath}");
                continue;
            }

            BakePrefab(prefabPath, Path.GetFileNameWithoutExtension(TerrainPrefabSetup.Entries[i].fileName),
                uniqueMaterials);
            bakedPrefabs++;
        }

        CollectMaterialsInFolder("Assets/Resources/Prefabs/Terrain/Resources/Materials", uniqueMaterials);
        foreach (Material material in uniqueMaterials)
        {
            if (EnableInstancing(material))
                enabledMaterials++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        int reverted = 0;
        foreach (Material material in uniqueMaterials)
        {
            if (material == null) continue;
            if (material.enableInstancing) continue;
            if (EnableInstancing(material))
                reverted++;
        }

        if (reverted > 0)
            AssetDatabase.SaveAssets();

        Debug.Log($"已烘焙 {bakedPrefabs} 个地形 Prefab，并启用 {enabledMaterials + reverted} 个材质的 GPU Instancing。");
    }

    public static void BakeBatch()
    {
        try
        {
            Bake();
            EditorApplication.Exit(0);
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void BakePrefab(string prefabPath, string prefabName, HashSet<Material> uniqueMaterials)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            ProBuilderMesh[] proBuilderMeshes = root.GetComponentsInChildren<ProBuilderMesh>(true);
            for (int i = 0; i < proBuilderMeshes.Length; i++)
                StripProBuilder(proBuilderMeshes[i], prefabName, i);

            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer meshRenderer = renderers[i];
                if (meshRenderer == null || !IsBaseTerrainRenderer(meshRenderer)) continue;
                MeshFilter filter = meshRenderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                if (meshRenderer.GetComponent<TerrainBatchSource>() == null)
                    meshRenderer.gameObject.AddComponent<TerrainBatchSource>();

                Material[] materials = meshRenderer.sharedMaterials;
                for (int m = 0; m < materials.Length; m++)
                {
                    if (materials[m] != null)
                        uniqueMaterials.Add(materials[m]);
                }
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void StripProBuilder(ProBuilderMesh pb, string prefabName, int index)
    {
        if (pb == null) return;
        GameObject go = pb.gameObject;
        pb.ToMesh();
        pb.Refresh();

        MeshFilter filter = go.GetComponent<MeshFilter>();
        Mesh source = filter != null ? filter.sharedMesh : null;
        if (source == null)
        {
            Debug.LogError($"地形 {prefabName} 上的 ProBuilderMesh 没有可用 Mesh。", go);
            return;
        }

        string meshName = $"{prefabName}_{go.name}_{index}";
        string assetPath = $"{MeshFolder}/{SanitizeFileName(meshName)}.asset";
        Mesh baked = Object.Instantiate(source);
        baked.name = meshName;
        baked = SaveOrReplaceMesh(baked, assetPath);

        pb.preserveMeshAssetOnDestroy = true;
        DestroyComponentsByTypeName(go, "ProBuilderShape", "PolyShape", "BezierShape", "Entity");
        Object.DestroyImmediate(pb, true);

        if (filter != null)
        {
            filter.hideFlags = HideFlags.None;
            filter.sharedMesh = baked;
        }

        if (go.TryGetComponent(out MeshCollider collider))
            collider.sharedMesh = baked;
    }

    private static void DestroyComponentsByTypeName(GameObject go, params string[] typeNames)
    {
        if (go == null || typeNames == null || typeNames.Length == 0) return;
        Component[] components = go.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null) continue;
            string typeName = component.GetType().Name;
            for (int t = 0; t < typeNames.Length; t++)
            {
                if (typeName != typeNames[t]) continue;
                Object.DestroyImmediate(component, true);
                break;
            }
        }
    }

    private static Mesh SaveOrReplaceMesh(Mesh baked, string assetPath)
    {
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        if (existing != null)
        {
            EditorUtility.CopySerialized(baked, existing);
            existing.name = baked.name;
            Object.DestroyImmediate(baked);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        AssetDatabase.CreateAsset(baked, assetPath);
        return baked;
    }

    private static bool IsBaseTerrainRenderer(MeshRenderer renderer)
    {
        string objectName = renderer.gameObject.name;
        return objectName != "MoveRangeHighlight" &&
               objectName != "HoverHighlight" &&
               objectName != "IntentHighlight" &&
               renderer.GetComponent<LineRenderer>() == null;
    }

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalid.Length; i++)
            name = name.Replace(invalid[i], '_');
        return name.Replace(' ', '_');
    }

    private static void CollectMaterialsInFolder(string folder, HashSet<Material> uniqueMaterials)
    {
        if (!AssetDatabase.IsValidFolder(folder)) return;
        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { folder });
        for (int i = 0; i < guids.Length; i++)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guids[i]));
            if (material != null)
                uniqueMaterials.Add(material);
        }
    }

    private static bool EnableInstancing(Material material)
    {
        if (material == null) return false;
        SerializedObject so = new SerializedObject(material);
        SerializedProperty property = so.FindProperty("m_EnableInstancingVariants");
        bool changed = false;
        if (property != null && !property.boolValue)
        {
            property.boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            changed = true;
        }

        if (!material.enableInstancing)
        {
            material.enableInstancing = true;
            changed = true;
        }

        if (changed)
            EditorUtility.SetDirty(material);
        return changed;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        string name = path.Substring(slash + 1);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }
}
#endif
