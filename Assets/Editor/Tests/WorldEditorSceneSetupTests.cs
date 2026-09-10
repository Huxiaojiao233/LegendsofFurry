#if UNITY_EDITOR
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class WorldEditorSceneSetupTests
{
    [Test]
    public void BuildCopiesBattleCameraBoardAndLighting()
    {
        WorldEditorSceneSetup.Build();
        WorldEditorSceneSetup.Build();

        Scene editorScene = SceneManager.GetActiveScene();
        GameObject editorCameraObject = FindRoot(editorScene, "Main Camera");
        Assert.That(editorCameraObject, Is.Not.Null);
        Camera editorCamera = editorCameraObject.GetComponent<Camera>();
        Assert.That(editorCamera, Is.Not.Null);
        Assert.That(editorCamera.CompareTag("MainCamera"), Is.True);
        Assert.That(editorCamera.orthographic, Is.True);
        Assert.That(editorCameraObject.GetComponent<RectTransform>(), Is.Null);
        Assert.That(editorCameraObject.GetComponent<BoardCameraController>(), Is.Not.Null);
        Assert.That(GameObject.Find("WorldEditorCamera"), Is.Null);

        GameObject editorBoardObject = FindRoot(editorScene, "Board");
        Assert.That(editorBoardObject, Is.Not.Null);
        Assert.That(editorBoardObject.GetComponent<RectTransform>(), Is.Null);
        Assert.That(editorBoardObject.transform.Find("GridRoot"), Is.Not.Null);
        BoardGenerator board = editorBoardObject.GetComponent<BoardGenerator>();
        Assert.That(board, Is.Not.Null);

        Vector3 cameraPosition = editorCamera.transform.position;
        Quaternion cameraRotation = editorCamera.transform.rotation;
        float orthoSize = editorCamera.orthographicSize;
        Vector3 boardPosition = editorBoardObject.transform.position;
        Quaternion boardRotation = editorBoardObject.transform.rotation;
        SerializedObject editorRig = new SerializedObject(editorCameraObject.GetComponent<BoardCameraController>());
        Assert.That(editorRig.FindProperty("board").objectReferenceValue, Is.EqualTo(board));
        Assert.That(editorRig.FindProperty("targetCamera").objectReferenceValue, Is.EqualTo(editorCamera));
        float panSpeed = editorRig.FindProperty("keyboardPanSpeed").floatValue;
        float zoomSensitivity = editorRig.FindProperty("zoomSensitivity").floatValue;
        float minDistance = editorRig.FindProperty("minimumDistance").floatValue;
        float maxDistance = editorRig.FindProperty("maximumDistance").floatValue;

        Scene battle = EditorSceneManager.OpenScene(TerrainPrefabSetup.BattleScenePath, OpenSceneMode.Additive);
        try
        {
            GameObject battleCameraObject = FindRoot(battle, "Main Camera");
            GameObject battleBoardObject = FindRoot(battle, "Board");
            Assert.That(battleCameraObject, Is.Not.Null);
            Assert.That(battleBoardObject, Is.Not.Null);
            Camera battleCamera = battleCameraObject.GetComponent<Camera>();
            SerializedObject battleRig = new SerializedObject(battleCameraObject.GetComponent<BoardCameraController>());
            Assert.That(orthoSize, Is.EqualTo(battleCamera.orthographicSize));
            Assert.That(cameraPosition, Is.EqualTo(battleCamera.transform.position));
            Assert.That(Quaternion.Angle(cameraRotation, battleCamera.transform.rotation), Is.LessThan(0.01f));
            Assert.That(boardPosition, Is.EqualTo(battleBoardObject.transform.position));
            Assert.That(Quaternion.Angle(boardRotation, battleBoardObject.transform.rotation), Is.LessThan(0.01f));
            Assert.That(panSpeed, Is.EqualTo(battleRig.FindProperty("keyboardPanSpeed").floatValue));
            Assert.That(zoomSensitivity, Is.EqualTo(battleRig.FindProperty("zoomSensitivity").floatValue));
            Assert.That(minDistance, Is.EqualTo(battleRig.FindProperty("minimumDistance").floatValue));
            Assert.That(maxDistance, Is.EqualTo(battleRig.FindProperty("maximumDistance").floatValue));
            Assert.That(FindRoot(editorScene, "Directional Light"), Is.Not.Null);
            Assert.That(FindRoot(battle, "Directional Light"), Is.Not.Null);
            Assert.That(FindRoot(editorScene, "Global Volume"), Is.Not.Null);
            Assert.That(FindRoot(battle, "Global Volume"), Is.Not.Null);
        }
        finally
        {
            EditorSceneManager.CloseScene(battle, true);
        }

        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include);
        int editorCanvases = 0;
        for (int i = 0; i < canvases.Length; i++)
            if (canvases[i] != null && canvases[i].name == "WorldEditorCanvas") editorCanvases++;
        Assert.That(editorCanvases, Is.EqualTo(1));

        Assert.That(Object.FindAnyObjectByType<EventSystem>(), Is.Not.Null);
        RuntimeWorldEditorController controller = Object.FindAnyObjectByType<RuntimeWorldEditorController>();
        Assert.That(controller, Is.Not.Null);
        SerializedObject so = new SerializedObject(controller);
        Assert.That(so.FindProperty("board").objectReferenceValue, Is.Not.Null);
        Assert.That(so.FindProperty("view").objectReferenceValue, Is.Not.Null);
        Assert.That(so.FindProperty("editorCamera"), Is.Null);
        Assert.That(so.FindProperty("backButton").objectReferenceValue, Is.Not.Null);

        SerializedObject boardSo = new SerializedObject(board);
        Assert.That(boardSo.FindProperty("fallbackCellPrefab").objectReferenceValue, Is.Not.Null);
        Assert.That(boardSo.FindProperty("terrainPrefabs").arraySize, Is.GreaterThan(0));
        Assert.That(boardSo.FindProperty("gridRoot").objectReferenceValue, Is.Not.Null);
        Assert.That(Object.FindAnyObjectByType<WorldEditorView>(), Is.Not.Null);
        Assert.That(Object.FindAnyObjectByType<WorldEditorInputController>(), Is.Not.Null);
        SerializedObject viewSo = new SerializedObject(Object.FindAnyObjectByType<WorldEditorView>());
        Assert.That(viewSo.FindProperty("worldList").objectReferenceValue, Is.Not.Null);
        Assert.That(viewSo.FindProperty("newWorldIdInput").objectReferenceValue, Is.Not.Null);
        Assert.That(viewSo.FindProperty("heightUpToolButton").objectReferenceValue, Is.Not.Null);
        Assert.That(viewSo.FindProperty("unitList").objectReferenceValue, Is.Not.Null);
        Assert.That(viewSo.FindProperty("stageIdInput").objectReferenceValue, Is.Not.Null);
        Assert.That(viewSo.FindProperty("contourToggle").objectReferenceValue, Is.Not.Null);
        Toggle contour = viewSo.FindProperty("contourToggle").objectReferenceValue as Toggle;
        Toggle bounds = viewSo.FindProperty("chunkBoundsToggle").objectReferenceValue as Toggle;
        Assert.That(contour.targetGraphic, Is.Not.Null);
        Assert.That(contour.graphic, Is.Not.Null);
        Assert.That(bounds.targetGraphic, Is.Not.Null);
        Assert.That(bounds.graphic, Is.Not.Null);
        TMPro.TMP_Dropdown stageTypes = viewSo.FindProperty("stageTypeList").objectReferenceValue as TMPro.TMP_Dropdown;
        Assert.That(stageTypes.template.GetComponent<ScrollRect>(), Is.Not.Null);
        Assert.That(stageTypes.template.GetComponent<ScrollRect>().viewport, Is.Not.Null);
        Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(WorldEditorSceneSetup.ScenePath), Is.Not.Null);
    }

    private static GameObject FindRoot(Scene scene, string name)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] != null && roots[i].name == name) return roots[i];
        }

        return null;
    }
}
#endif
