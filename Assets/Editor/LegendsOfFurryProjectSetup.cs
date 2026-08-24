#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class LegendsOfFurryProjectSetup
{
    private const string ClassScene = "Assets/Scenes/S_ClassSelect.unity";
    private const string BattleScene = "Assets/Scenes/S_Battle.unity";
    private const string StartingDeckAsset = "Assets/Cards/Decks/PlayerStartingDeck.asset";

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

    public static void ValidateProject()
    {
        EnsureScenes();
        if (CardCatalog.All.Count != 61)
            throw new System.InvalidOperationException($"卡牌数量应为61，实际为{CardCatalog.All.Count}。");
        if (ClassCatalog.Get(HeroClass.Warrior) == null || ClassCatalog.Get(HeroClass.Priest) == null)
            throw new System.InvalidOperationException("职业目录不完整。");

        DeckData baseDeck = AssetDatabase.LoadAssetAtPath<DeckData>(StartingDeckAsset);
        if (baseDeck == null)
            throw new System.InvalidOperationException("未找到基础牌库 PlayerStartingDeck。");
        CardData[] baseCards = baseDeck.CreateDrawPile().ToArray();
        if (baseCards.Length != 10 ||
            baseCards.Count(card => card.cardId == "hit_01") != 3 ||
            baseCards.Count(card => card.cardId == "block_01") != 3 ||
            baseCards.Count(card => card.cardId == "run_01") != 3 ||
            baseCards.Count(card => card.cardId == "heal_01") != 1)
            throw new System.InvalidOperationException("基础牌库应为3爪击、3格挡、3疾走、1疗愈。");

        EditorSceneManager.OpenScene(ClassScene, OpenSceneMode.Single);
        EditorSceneManager.OpenScene(BattleScene, OpenSceneMode.Single);
        Debug.Log("LEGENDS_OF_FURRY_VALIDATION_OK: 5 classes, 61 equipment cards, 10 base cards, class-select and battle scenes loaded.");
    }
}
#endif
