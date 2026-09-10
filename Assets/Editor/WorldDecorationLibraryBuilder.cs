#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>把 Forest Essentials 预制件引用写进 Resources，运行时和出包都能加载。</summary>
public sealed class WorldDecorationLibraryBuilder : IPreprocessBuildWithReport
{
    public const string AssetPath = "Assets/Resources/World/DecorationLibrary.asset";

    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report) => Rebuild();

    [MenuItem("Tools/Legends Of Furry/重建装饰预制件库")]
    public static void Rebuild()
    {
        EnsureFolder("Assets/Resources");
        EnsureFolder("Assets/Resources/World");
        WorldDecorationCatalog.Refresh();
        WorldDecorationLibrary library = AssetDatabase.LoadAssetAtPath<WorldDecorationLibrary>(AssetPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<WorldDecorationLibrary>();
            AssetDatabase.CreateAsset(library, AssetPath);
        }

        library.entries.Clear();
        for (int i = 0; i < WorldDecorationCatalog.All.Count; i++)
        {
            WorldDecorationEntry entry = WorldDecorationCatalog.All[i];
            library.entries.Add(new WorldDecorationLibraryEntry
            {
                id = entry.Id,
                category = entry.Category,
                label = entry.Label,
                glyph = entry.Glyph,
                prefab = WorldDecorationCatalog.LoadPrefab(entry.Id)
            });
        }

        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
        Debug.Log($"装饰预制件库已写入 {AssetPath}，共 {library.entries.Count} 个。");
    }

    public static void RebuildIfNeeded()
    {
        if (!AssetDatabase.IsValidFolder(WorldDecorationCatalog.PrefabRoot)) return;
        int prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { WorldDecorationCatalog.PrefabRoot }).Length;
        WorldDecorationLibrary library = AssetDatabase.LoadAssetAtPath<WorldDecorationLibrary>(AssetPath);
        if (library != null && library.entries != null && library.entries.Count == prefabs) return;
        Rebuild();
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
