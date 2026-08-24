using UnityEngine;
using UnityEngine.SceneManagement;

public class GameUIController : MonoBehaviour
{
    public void QuitGame()
    {
        Debug.Log("退出游戏");

        #if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
        #else
                Application.Quit();
        #endif
    }

    public void BackToClassSelect()
    {
        GameSession.ClearSelectedClass();
        SceneManager.LoadScene("S_ClassSelect", LoadSceneMode.Single);
    }
}