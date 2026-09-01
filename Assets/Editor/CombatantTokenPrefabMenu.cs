#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>生成运行时棋子预制件：不含角色贴图，进战斗后再从内容包刷外观。</summary>
public static class CombatantTokenPrefabMenu
{
    private const string PrefabPath = "Assets/Resources/CombatantToken.prefab";

    [MenuItem("Tools/Legends Of Furry/创建棋子预制件")]
    public static void CreatePrefab()
    {
        Directory.CreateDirectory("Assets/Resources");
        GameObject token = new GameObject("CombatantToken");
        try
        {
            CombatantTokenFactory.EnsureTokenComponents(token);
            MeshRenderer renderer = token.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.enabled = false;
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(token, PrefabPath);
            Selection.activeObject = prefab;
            Debug.Log($"已写入棋子预制件：{PrefabPath}。请从 S_Battle 删除 Player 与 Monster。", prefab);
        }
        finally
        {
            Object.DestroyImmediate(token);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
#endif
