using UnityEngine;
using UnityEngine.SceneManagement;

public class GameUIController : MonoBehaviour
{
    private void Start()
    {
        if (SceneManager.GetActiveScene().name != "S_Menu") return;
        Transform existing = transform.Find("Continue") ?? transform.Find("B_Continue");
        if (existing == null) return;
        existing.gameObject.SetActive(RunSession.HasSaveFile);
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
