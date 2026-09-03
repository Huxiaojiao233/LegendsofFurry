using UnityEngine;
using LegendsOfFurry.Content.Runtime;

/// <summary>内容包启动失败时盖在画面上，避免玩家只看到闪屏后的黑屏。</summary>
public sealed class ContentLoadFailureNotice : MonoBehaviour
{
    public static void Ensure()
    {
        if (ContentRuntime.IsLoaded)
        {
            return;
        }

        if (FindAnyObjectByType<ContentLoadFailureNotice>() != null)
        {
            return;
        }

        GameObject host = new GameObject("ContentLoadFailureNotice");
        DontDestroyOnLoad(host);
        host.AddComponent<ContentLoadFailureNotice>();
    }

    private void OnGUI()
    {
        string message = ContentRuntime.LoadError == null
            ? "内容包未加载。"
            : ContentRuntime.LoadError.Message;
        GUIStyle box = new GUIStyle(GUI.skin.box)
        {
            fontSize = 18,
            alignment = TextAnchor.UpperLeft,
            wordWrap = true,
            padding = new RectOffset(24, 24, 24, 24)
        };
        GUI.Box(new Rect(40, 40, Screen.width - 80, Screen.height - 80),
            "内容包加载失败，游戏无法进入。\n\n" + message +
            "\n\n请重新打包，或把 lofe_core.lofepackage 放到游戏 StreamingAssets/Content/Packs/ 下。",
            box);
    }
}
