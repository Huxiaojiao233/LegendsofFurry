using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using LegendsOfFurry.Content.Runtime;

/// <summary>
/// S_Loading 入口：使用场景内 UGUI，分帧加载内容包并显示真实进度后进入主菜单。
/// </summary>
public sealed class ContentBootLoader : MonoBehaviour
{
    public const string LoadingSceneName = "S_Loading";
    public const string NextSceneName = "S_Menu";

    [SerializeField] private string nextScene = NextSceneName;
    [SerializeField] private float minimumDisplaySeconds = 0.35f;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Image progressFill;

    private float displayStarted;
    private string pendingStatus = "正在启动…";
    private float pendingProgress = 0.02f;
    private float displayedProgress;
    private static Sprite whiteSprite;

    private void Awake()
    {
        EnsureProgressVisual();
        displayedProgress = pendingProgress;
        ApplyProgressVisual(displayedProgress);
        SetProgress(pendingStatus, pendingProgress);
    }

    private IEnumerator Start()
    {
        displayStarted = Time.realtimeSinceStartup;
        // 先让加载屏至少渲染两帧，避免启动时白屏卡死。
        yield return null;
        yield return null;
        Canvas.ForceUpdateCanvases();

        bool ok = false;
        yield return ContentRuntime.EnsureLoadedRoutine(
            (message, progress) =>
            {
                pendingStatus = message;
                pendingProgress = progress;
                SetProgress(pendingStatus, pendingProgress);
            },
            success => ok = success);

        // 加载结束后把条推满并多留一帧，避免一闪而过。
        SetProgress(pendingStatus, 1f);
        displayedProgress = 1f;
        ApplyProgressVisual(1f);
        yield return null;

        float elapsed = Time.realtimeSinceStartup - displayStarted;
        if (elapsed < minimumDisplaySeconds)
            yield return new WaitForSecondsRealtime(minimumDisplaySeconds - elapsed);

        if (!ok || !ContentRuntime.IsLoaded)
        {
            string error = ContentRuntime.LoadError?.Message ?? "未知错误";
            SetProgress("内容加载失败\n" + error, 1f);
            ContentLoadFailureNotice.Ensure();
            yield break;
        }

        SetProgress("进入游戏…", 1f);
        yield return null;
        string target = string.IsNullOrWhiteSpace(nextScene) ? NextSceneName : nextScene;
        if (!Application.CanStreamedLevelBeLoaded(target))
        {
            string fallback = FindFirstLoadableScene(target);
            if (string.IsNullOrEmpty(fallback))
            {
                SetProgress($"无法进入 {target}\n请把该场景加入当前 Build Profile。", 1f);
                Debug.LogError($"场景 {target} 不在当前 Build Profile / Build Settings 中。", this);
                yield break;
            }

            Debug.LogWarning($"场景 {target} 不可用，改用 {fallback}。", this);
            target = fallback;
        }

        SceneManager.LoadScene(target, LoadSceneMode.Single);
    }

    private static string FindFirstLoadableScene(string preferred)
    {
        if (Application.CanStreamedLevelBeLoaded(preferred)) return preferred;
        string[] candidates = { "S_Menu", "S_ClassSelect", "S_Battle" };
        for (int i = 0; i < candidates.Length; i++)
        {
            if (Application.CanStreamedLevelBeLoaded(candidates[i])) return candidates[i];
        }

        int count = SceneManager.sceneCountInBuildSettings;
        for (int i = 0; i < count; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            if (string.IsNullOrEmpty(path)) continue;
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name == LoadingSceneName) continue;
            if (Application.CanStreamedLevelBeLoaded(name)) return name;
        }

        return null;
    }

    private void Update()
    {
        if (progressFill == null) return;
        // 用 Update 平滑追上，加载协程 yield 的帧也能看见条在动。
        float speed = Mathf.Max(0.35f, Mathf.Abs(pendingProgress - displayedProgress) * 4f);
        displayedProgress = Mathf.MoveTowards(displayedProgress, pendingProgress, Time.unscaledDeltaTime * speed);
        ApplyProgressVisual(displayedProgress);
        if (statusText != null && !string.IsNullOrEmpty(pendingStatus))
            statusText.text = pendingStatus;
    }

    private void SetProgress(string status, float normalized)
    {
        pendingStatus = status ?? string.Empty;
        pendingProgress = Mathf.Clamp01(normalized);
        if (statusText != null) statusText.text = pendingStatus;
        // 立刻抬一点，保证本帧就能看到变化。
        displayedProgress = Mathf.Max(displayedProgress, pendingProgress * 0.9f);
        ApplyProgressVisual(displayedProgress);
    }

    private void EnsureProgressVisual()
    {
        if (progressFill == null) return;
        if (progressFill.sprite == null)
            progressFill.sprite = WhiteSprite();
        progressFill.type = Image.Type.Filled;
        progressFill.fillMethod = Image.FillMethod.Horizontal;
        progressFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        progressFill.fillAmount = 0f;
    }

    private void ApplyProgressVisual(float normalized)
    {
        if (progressFill == null) return;
        if (progressFill.sprite == null)
            progressFill.sprite = WhiteSprite();
        progressFill.type = Image.Type.Filled;
        progressFill.fillMethod = Image.FillMethod.Horizontal;
        progressFill.fillAmount = Mathf.Clamp01(normalized);
    }

    private static Sprite WhiteSprite()
    {
        if (whiteSprite != null) return whiteSprite;
        Texture2D texture = Texture2D.whiteTexture;
        whiteSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f);
        whiteSprite.name = "LoadingProgressWhite";
        return whiteSprite;
    }
}
