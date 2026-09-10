#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 为每种地形从 Block 复制占位 Prefab，并挂到 S_Battle 的 BoardGenerator 上。
/// 美术可直接替换这些 Prefab 内模型，运行时按 terrainId 实例化，不再染色。
/// </summary>
public static class TerrainPrefabSetup
{
    public const string TerrainPrefabFolder = "Assets/Resources/Prefabs/Terrain";
    public const string SourceBlockPrefab = "Assets/Resources/Prefabs/Block.prefab";
    public const string BattleScenePath = "Assets/Scenes/S_Battle.unity";

    public static readonly (string terrainId, string fileName)[] Entries =
    {
        (WorldTerrainCatalog.Grass, "Terrain_Grass.prefab"),
        (WorldTerrainCatalog.Stone, "Terrain_Stone.prefab"),
        (WorldTerrainCatalog.Water, "Terrain_Water.prefab"),
        (WorldTerrainCatalog.Dirt, "Terrain_Dirt.prefab"),
        (WorldTerrainCatalog.Sand, "Terrain_Sand.prefab"),
        (WorldTerrainCatalog.Road, "Terrain_Road.prefab"),
        (WorldTerrainCatalog.Forest, "Terrain_Forest.prefab"),
        (WorldTerrainCatalog.Void, "Terrain_Void.prefab"),
    };

    [MenuItem("Tools/Legends Of Furry/Ensure Terrain Prefabs")]
    public static void EnsureTerrainPrefabs()
    {
        BoardGenerator.TerrainPrefabBinding[] bindings = CreateOrLoadTerrainPrefabs(out GameObject fallback);
        if (bindings == null) return;
        WireBoardGenerator(bindings, fallback);
        Debug.Log($"已确保 {Entries.Length} 个地形 Prefab 位于 {TerrainPrefabFolder}，并已绑定 BoardGenerator。");
    }

    public static void EnsureTerrainPrefabsBatch()
    {
        EnsureTerrainPrefabs();
        EditorApplication.Exit(0);
    }

    private static BoardGenerator.TerrainPrefabBinding[] CreateOrLoadTerrainPrefabs(out GameObject fallback)
    {
        fallback = null;
        if (!Directory.Exists(TerrainPrefabFolder))
            Directory.CreateDirectory(TerrainPrefabFolder);

        fallback = AssetDatabase.LoadAssetAtPath<GameObject>(SourceBlockPrefab);
        if (fallback == null)
        {
            Debug.LogError($"找不到源地形 Prefab：{SourceBlockPrefab}");
            return null;
        }

        var bindings = new List<BoardGenerator.TerrainPrefabBinding>();
        for (int i = 0; i < Entries.Length; i++)
        {
            string path = $"{TerrainPrefabFolder}/{Entries[i].fileName}";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                GameObject instance = PrefabUtility.InstantiatePrefab(fallback) as GameObject;
                if (instance == null) instance = Object.Instantiate(fallback);
                instance.name = Path.GetFileNameWithoutExtension(Entries[i].fileName);
                PrefabUtility.SaveAsPrefabAsset(instance, path);
                Object.DestroyImmediate(instance);
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }

            bindings.Add(new BoardGenerator.TerrainPrefabBinding
            {
                terrainId = Entries[i].terrainId,
                prefab = prefab
            });
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return bindings.ToArray();
    }

    private static void WireBoardGenerator(BoardGenerator.TerrainPrefabBinding[] bindings, GameObject fallback)
    {
        if (!File.Exists(BattleScenePath)) return;

        Scene scene = EditorSceneManager.OpenScene(BattleScenePath, OpenSceneMode.Single);
        BoardGenerator[] boards = Object.FindObjectsByType<BoardGenerator>(FindObjectsInactive.Include);
        for (int i = 0; i < boards.Length; i++)
        {
            BoardGenerator board = boards[i];
            SerializedObject so = new SerializedObject(board);
            SerializedProperty fallbackProp = so.FindProperty("fallbackCellPrefab");
            if (fallbackProp != null && fallbackProp.objectReferenceValue == null)
                fallbackProp.objectReferenceValue = fallback;

            SerializedProperty array = so.FindProperty("terrainPrefabs");
            if (array == null) continue;
            array.arraySize = bindings.Length;
            for (int b = 0; b < bindings.Length; b++)
            {
                SerializedProperty element = array.GetArrayElementAtIndex(b);
                element.FindPropertyRelative("terrainId").stringValue = bindings[b].terrainId;
                element.FindPropertyRelative("prefab").objectReferenceValue = bindings[b].prefab;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(board);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    [InitializeOnLoadMethod]
    private static void AutoEnsure()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            bool missing = false;
            for (int i = 0; i < Entries.Length; i++)
            {
                if (!File.Exists($"{TerrainPrefabFolder}/{Entries[i].fileName}"))
                {
                    missing = true;
                    break;
                }
            }

            if (!missing) return;
            CreateOrLoadTerrainPrefabs(out _);
            Scene active = SceneManager.GetActiveScene();
            if (active.path == BattleScenePath)
                EnsureTerrainPrefabs();
            else
                Debug.Log("Terrain placeholder prefabs created. Run Tools/Legends Of Furry/Ensure Terrain Prefabs to bind BoardGenerator.");
        };
    }
}
#endif
