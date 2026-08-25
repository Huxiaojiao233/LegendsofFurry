#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;

[InitializeOnLoad]
public static class LegendsOfFurryProjectSetup
{
    private const string ClassScene = "Assets/Scenes/S_ClassSelect.unity";
    private const string BattleScene = "Assets/Scenes/S_Battle.unity";

    static LegendsOfFurryProjectSetup()
    {
        EditorApplication.delayCall += EnsureScenes;
    }

    [MenuItem("Tools/Legends Of Furry/生成职业选择与战斗场景")]
    public static void EnsureScenes()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        if (!File.Exists(ClassScene))
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            GameObject cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            cameraObject.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            cameraObject.GetComponent<Camera>().backgroundColor = new Color(0.03f, 0.05f, 0.08f);
            GameObject controller = new GameObject("ClassSelectionController", typeof(ClassSelectionController));
            SceneManager.MoveGameObjectToScene(controller, scene);
            EditorSceneManager.SaveScene(scene, ClassScene);
            EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.Refresh();
        }

        EditorBuildSettingsScene[] expected =
        {
            new EditorBuildSettingsScene(ClassScene, true),
            new EditorBuildSettingsScene(BattleScene, true)
        };
        bool differs = EditorBuildSettings.scenes.Length != expected.Length ||
            !EditorBuildSettings.scenes.Select(item => item.path).SequenceEqual(expected.Select(item => item.path));
        if (differs) EditorBuildSettings.scenes = expected;
    }

    /// <summary>
    /// 验证数据库内容和两个正式场景均可加载，供自动验收与人工菜单复用。
    /// </summary>
    public static void ValidateProject()
    {
        EnsureScenes();
        ContentPackage package = ContentBuildValidator.ValidatePublishedContent();

        EditorSceneManager.OpenScene(ClassScene, OpenSceneMode.Single);
        EditorSceneManager.OpenScene(BattleScene, OpenSceneMode.Single);
        Debug.Log($"LEGENDS_OF_FURRY_VALIDATION_OK: {package.ClassProfiles.Count} classes, {package.Cards.Count} database cards, class-select and battle scenes loaded.");
    }
}
#endif
