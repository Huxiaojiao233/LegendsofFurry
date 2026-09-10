using System;
using System.Collections.Generic;
using System.IO;
using LegendsOfFurry.Content.Contracts;
using UnityEngine;

/// <summary>
/// 地图装饰资源：Forest Essentials 预制件。编辑器按文件夹分类刷，运行时原样实例化到格子上。
/// 旧存档里的 base.tree / base.rock / base.camp 会映射到对应预制件。
/// </summary>
public static class WorldDecorationCatalog
{
    public const string PrefabRoot = "Assets/Resources/Decorations";
    public const string ResourcesPrefabRoot = "Decorations";
    public const string DefaultId = "hs.tree.oak.01";

    public static readonly string[] CategoryOrder =
    {
        "Trees_Deciduous", "Trees_Conifers", "Trees_Fruit", "Trees_Character",
        "Bushes", "Rocks", "Logs", "Grass", "Flowers", "Mushrooms", "Ferns",
        "GroundDetails", "Water"
    };

    private static readonly List<WorldDecorationEntry> entries = new List<WorldDecorationEntry>();
    private static readonly Dictionary<string, WorldDecorationEntry> byId =
        new Dictionary<string, WorldDecorationEntry>(StringComparer.Ordinal);
    private static bool loaded;

    public static IReadOnlyList<WorldDecorationEntry> All
    {
        get
        {
            Ensure();
            return entries;
        }
    }

    public static string CanonicalId(string definition)
    {
        if (definition == WorldTerrainCatalog.Tree) return DefaultId;
        if (definition == WorldTerrainCatalog.Rock) return "hs.rock.medium.01";
        if (definition == WorldTerrainCatalog.Camp) return "hs.stump.01";
        return string.IsNullOrEmpty(definition) ? DefaultId : definition;
    }

    public static string IdFromPrefabName(string fileName)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        if (stem.StartsWith("P_HS_LP_", StringComparison.Ordinal))
            stem = stem.Substring("P_HS_LP_".Length);
        return "hs." + stem.ToLowerInvariant().Replace('_', '.');
    }

    public static string CategoryLabel(string category) => category switch
    {
        "Trees_Deciduous" => "阔叶",
        "Trees_Conifers" => "针叶",
        "Trees_Fruit" => "果树",
        "Trees_Character" => "枯树",
        "Bushes" => "灌木",
        "Rocks" => "岩石",
        "Logs" => "枯木",
        "Grass" => "草丛",
        "Flowers" => "花",
        "Mushrooms" => "蘑菇",
        "Ferns" => "蕨",
        "GroundDetails" => "地面",
        "Water" => "水景",
        _ => category
    };

    public static string GlyphOf(string definition)
    {
        if (!TryGet(definition, out WorldDecorationEntry entry))
        {
            if (definition == WorldTerrainCatalog.Camp) return "帐";
            if (definition == WorldTerrainCatalog.Tree) return "阔";
            return "石";
        }

        return entry.Glyph;
    }

    public static bool TryGet(string definition, out WorldDecorationEntry entry)
    {
        Ensure();
        return byId.TryGetValue(CanonicalId(definition), out entry);
    }

    public static WorldDecorationEntry[] InCategory(string category)
    {
        Ensure();
        List<WorldDecorationEntry> list = new List<WorldDecorationEntry>();
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].Category == category) list.Add(entries[i]);
        return list.ToArray();
    }

    public static WorldDecorationEntry[] Search(string query)
    {
        Ensure();
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<WorldDecorationEntry>();
        string needle = query.Trim().ToLowerInvariant();
        List<WorldDecorationEntry> list = new List<WorldDecorationEntry>();
        for (int i = 0; i < entries.Count; i++)
        {
            WorldDecorationEntry entry = entries[i];
            if (entry.Id.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                entry.Label.ToLowerInvariant().IndexOf(needle, StringComparison.Ordinal) >= 0 ||
                CategoryLabel(entry.Category).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                list.Add(entry);
        }

        return list.ToArray();
    }

    public static GameObject LoadPrefab(string definition)
    {
        if (!TryGet(definition, out WorldDecorationEntry entry)) return null;
        if (entry.Prefab != null) return entry.Prefab;
#if UNITY_EDITOR
        if (!string.IsNullOrEmpty(entry.AssetPath))
        {
            entry.Prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(entry.AssetPath);
            return entry.Prefab;
        }
#endif
        return null;
    }

    public static GameObject Spawn(WorldDecorationDefinition deco, Transform parent)
    {
        GameObject prefab = LoadPrefab(deco.Definition);
        GameObject obj;
        if (prefab != null)
        {
            obj = UnityEngine.Object.Instantiate(prefab, parent, false);
            obj.name = string.IsNullOrEmpty(deco.Id) ? CanonicalId(deco.Definition) : deco.Id;
            FitToCell(obj, deco.Rotation);
        }
        else
        {
            obj = SpawnFallback(deco, parent);
        }

        DisableColliders(obj);
        return obj;
    }

    public static void Refresh()
    {
        loaded = false;
        Ensure();
    }

    private static void Ensure()
    {
        if (loaded) return;
        loaded = true;
        entries.Clear();
        byId.Clear();
#if UNITY_EDITOR
        ScanEditor();
#endif
        if (entries.Count == 0) LoadLibrary();
    }

    private static void LoadLibrary()
    {
        WorldDecorationLibrary library = Resources.Load<WorldDecorationLibrary>(WorldDecorationLibrary.ResourcePath);
        if (library != null && library.entries != null)
        {
            for (int i = 0; i < library.entries.Count; i++)
            {
                WorldDecorationLibraryEntry item = library.entries[i];
                if (item == null || string.IsNullOrEmpty(item.id)) continue;
                Add(new WorldDecorationEntry
                {
                    Id = item.id,
                    Category = item.category ?? "",
                    Label = string.IsNullOrEmpty(item.label) ? item.id : item.label,
                    Glyph = string.IsNullOrEmpty(item.glyph) ? GlyphForCategory(item.category) : item.glyph,
                    Prefab = item.prefab
                });
            }
        }

        // 正式运行时不依赖 Editor 的 AssetDatabase：直接把 Resources/Decorations
        // 中实际打包的预制件纳入画笔目录。
        GameObject[] prefabs = Resources.LoadAll<GameObject>(ResourcesPrefabRoot);
        for (int i = 0; i < prefabs.Length; i++)
        {
            GameObject prefab = prefabs[i];
            if (prefab == null) continue;
            string id = IdFromPrefabName(prefab.name);
            Add(new WorldDecorationEntry
            {
                Id = id,
                Category = "Decorations",
                Label = prefab.name.Replace("P_HS_LP_", string.Empty).Replace('_', ' '),
                Glyph = "饰",
                Prefab = prefab
            });
        }
    }

#if UNITY_EDITOR
    private static void ScanEditor()
    {
        if (!UnityEditor.AssetDatabase.IsValidFolder(PrefabRoot)) return;
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot });
        Array.Sort(guids, (a, b) => string.CompareOrdinal(
            UnityEditor.AssetDatabase.GUIDToAssetPath(a),
            UnityEditor.AssetDatabase.GUIDToAssetPath(b)));
        for (int i = 0; i < guids.Length; i++)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                continue;
            string category = CategoryFromPath(path);
            string id = IdFromPrefabName(path);
            Add(new WorldDecorationEntry
            {
                Id = id,
                Category = category,
                Label = LabelFromPath(path),
                Glyph = GlyphForCategory(category),
                AssetPath = path
            });
        }
    }
#endif

    private static void Add(WorldDecorationEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.Id) || byId.ContainsKey(entry.Id)) return;
        entries.Add(entry);
        byId[entry.Id] = entry;
    }

    private static string CategoryFromPath(string assetPath)
    {
        string relative = assetPath.Replace('\\', '/');
        string root = PrefabRoot.TrimEnd('/') + "/";
        if (relative.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            relative = relative.Substring(root.Length);
        int slash = relative.IndexOf('/');
        return slash <= 0 ? "Other" : relative.Substring(0, slash);
    }

    private static string LabelFromPath(string assetPath)
    {
        string stem = Path.GetFileNameWithoutExtension(assetPath);
        if (stem.StartsWith("P_HS_LP_", StringComparison.Ordinal))
            stem = stem.Substring("P_HS_LP_".Length);
        return stem.Replace('_', ' ');
    }

    private static string GlyphForCategory(string category) => category switch
    {
        "Trees_Deciduous" => "阔",
        "Trees_Conifers" => "针",
        "Trees_Fruit" => "果",
        "Trees_Character" => "枯",
        "Bushes" => "丛",
        "Rocks" => "石",
        "Logs" => "桩",
        "Grass" => "草",
        "Flowers" => "花",
        "Mushrooms" => "菇",
        "Ferns" => "蕨",
        "GroundDetails" => "地",
        "Water" => "水",
        _ => "饰"
    };

    private static void FitToCell(GameObject obj, int rotation)
    {
        obj.transform.localRotation = Quaternion.Euler(0f, rotation, 0f);
        obj.transform.localScale = Vector3.one;
        obj.transform.localPosition = Vector3.zero;
        Bounds local = LocalBounds(obj.transform);
        float xz = Mathf.Max(local.size.x, local.size.z, 0.001f);
        float scale = 0.82f / xz;
        float height = local.size.y * scale;
        if (height > 2.4f) scale *= 2.4f / height;
        if (height < 0.28f && local.size.y > 0.001f) scale = 0.28f / local.size.y;
        scale = Mathf.Clamp(scale, 0.02f, 8f);
        obj.transform.localScale = Vector3.one * scale;
        obj.transform.localPosition = new Vector3(0f, WorldTerrain.StepY - local.min.y * scale, 0f);
    }

    private static Bounds LocalBounds(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        bool started = false;
        Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
        for (int r = 0; r < renderers.Length; r++)
        {
            Renderer renderer = renderers[r];
            if (renderer == null) continue;
            Bounds world = renderer.bounds;
            Vector3 center = world.center;
            Vector3 extents = world.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = center + new Vector3(
                    (i & 1) == 0 ? -extents.x : extents.x,
                    (i & 2) == 0 ? -extents.y : extents.y,
                    (i & 4) == 0 ? -extents.z : extents.z);
                Vector3 local = root.InverseTransformPoint(corner);
                if (!started)
                {
                    bounds = new Bounds(local, Vector3.zero);
                    started = true;
                }
                else bounds.Encapsulate(local);
            }
        }

        return started ? bounds : new Bounds(Vector3.zero, Vector3.one);
    }

    private static void DisableColliders(GameObject obj)
    {
        Collider[] colliders = obj.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null) colliders[i].enabled = false;
    }

    private static GameObject SpawnFallback(WorldDecorationDefinition deco, Transform parent)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = string.IsNullOrEmpty(deco.Id) ? deco.Definition : deco.Id;
        obj.transform.SetParent(parent, false);
        obj.transform.localRotation = Quaternion.Euler(0f, deco.Rotation, 0f);
        obj.transform.localPosition = new Vector3(0f, 0.22f, 0f);
        obj.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);
        return obj;
    }
}

public sealed class WorldDecorationEntry
{
    public string Id;
    public string Category;
    public string Label;
    public string Glyph;
    public string AssetPath;
    public GameObject Prefab;
}
