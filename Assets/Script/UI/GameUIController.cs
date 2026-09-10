using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameUIController : MonoBehaviour
{
    private void Start()
    {
        if (SceneManager.GetActiveScene().name != "S_Menu") return;
        Transform existing = transform.Find("Continue") ?? transform.Find("B_Continue");
        if (existing != null) existing.gameObject.SetActive(RunSession.HasSaveFile);
        EnsureWorldEditorEntry();
    }

    /// <summary>主菜单中的 Runtime 世界编辑器入口；场景本身保持空白，由运行时代码建立编辑环境。</summary>
    private void EnsureWorldEditorEntry()
    {
        if (GameObject.Find("OpenWorldEditor") != null) return;
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null) return;
        GameObject buttonObject = new GameObject("OpenWorldEditor", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(canvas.transform, false);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-42f, 42f);
        rect.sizeDelta = new Vector2(176f, 44f);
        buttonObject.GetComponent<Image>().color = new Color(0.11f, 0.2f, 0.28f, 0.94f);
        buttonObject.GetComponent<Button>().onClick.AddListener(OpenWorldEditor);

        GameObject label = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        label.transform.SetParent(buttonObject.transform, false);
        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
        Text text = label.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 18;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.text = "世界编辑器";
        text.raycastTarget = false;
    }

    public void OpenWorldEditor()
    {
        SceneManager.LoadScene("S_WorldEditor", LoadSceneMode.Single);
    }

    public void QuitGame()
    {
        Debug.Log("退出游戏");

        #if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
        #else
                Application.Quit();
        #endif
    }

    public void ContinueRun()
    {
        if (!RunSession.TryLoad())
        {
            Debug.LogWarning("没有可继续的存档。");
            return;
        }

        SceneManager.LoadScene("S_Battle", LoadSceneMode.Single);
    }

    public void BackToClassSelect()
    {
        GameSession.ClearSelectedClass();
        SceneManager.LoadScene("S_ClassSelect", LoadSceneMode.Single);
    }

    public void BackToMenu()
    {
        GameSession.ClearSelectedClass();
        SceneManager.LoadScene("S_Menu", LoadSceneMode.Single);
    }
}
