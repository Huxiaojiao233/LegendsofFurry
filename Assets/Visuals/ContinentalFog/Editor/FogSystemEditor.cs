using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ContinentalFog.Editor
{
    [CustomEditor(typeof(FogSystem))]
    public sealed class FogSystemInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("中央保持清晰，雾从边缘向外加深。Map Size 为本物体局部 XZ 尺寸；运行时生成地面可指定 Bounds Target。生成的子物体由系统管理，参数保存在 Prefab。", MessageType.Info);
            DrawDefaultInspector();
            var fog = (FogSystem)target;
            if (GUILayout.Button("适配地面范围 / Fit Bounds"))
            {
                Undo.RecordObject(fog, "Fit fog bounds"); fog.FitBounds(); fog.Rebuild(); EditorUtility.SetDirty(fog);
            }
            if (GUILayout.Button("重建雾层 / Rebuild")) { fog.Rebuild(); SceneView.RepaintAll(); }
            if (fog.mapSize.x < fog.innerWidth * 2 || fog.mapSize.y < fog.innerWidth * 2)
                EditorGUILayout.HelpBox("Inner Width 太大，会吞掉地图中央。建议不超过短边的 15%。", MessageType.Warning);
        }
    }

    public static class FogSystemSetup
    {
        public const string Root = "Assets/Visuals/ContinentalFog";
        public const string PrefabPath = Root + "/Prefabs/FogSystem.prefab";

        [MenuItem("Tools/Continental Fog/Create Reusable Prefab")]
        public static void CreatePrefab()
        {
            Directory.CreateDirectory(Root + "/Materials");
            Directory.CreateDirectory(Root + "/Prefabs");
            AssetDatabase.Refresh();
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/SoftBoundary.shader");
            if (!shader) throw new System.InvalidOperationException("Fog shader is not imported yet.");
            string materialPath = Root + "/Materials/ContinentalFog.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, materialPath); }
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath))
            {
                var root = new GameObject("FogSystem");
                root.SetActive(false);
                root.AddComponent<FogSystem>().fogMaterial = material;
                root.SetActive(true);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Continental Fog/Add FogSystem to Current Scene")]
        public static void AddToScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            CreatePrefab();
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
                if (root.GetComponentInChildren<FogSystem>(true)) { Selection.activeGameObject = root; return; }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
            Undo.RegisterCreatedObjectUndo(instance, "Add Continental Fog");
            Selection.activeGameObject = instance;
            EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
