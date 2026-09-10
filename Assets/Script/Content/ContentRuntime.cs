using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using LegendsOfFurry.Content.Contracts;
using UnityEngine;

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>
/// 向游戏提供只读内容注册表。正式启动由 S_Loading 异步触发加载，避免 BeforeSceneLoad 卡死首帧。
/// </summary>
public static class ContentRuntime
{
    public static ContentRegistry Registry { get; private set; }
    public static Exception LoadError { get; private set; }
    public static bool IsLoaded => Registry != null;
    public static bool IsLoading { get; private set; }
    private static ContentLoadResult loadResult;
    private static bool loadAttempted;
    public static IReadOnlyList<ContentPackLoadInfo> LoadedPacks =>
        loadResult?.LoadedPacks ?? Array.Empty<ContentPackLoadInfo>();
    public static IReadOnlyList<ContentLoadDiagnostic> Diagnostics =>
        loadResult?.Diagnostics ?? Array.Empty<ContentLoadDiagnostic>();
    public static string ContentFingerprint => loadResult?.Fingerprint ?? string.Empty;

    /// <summary>启动时不再同步解包；留给加载场景画完第一帧后再 EnsureLoaded。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        loadAttempted = false;
        IsLoading = false;
        // 故意不在这里读盘，否则首场景出现前会长时间“未响应”。
    }

    /// <summary>
    /// 幂等加载内容包。status 会收到当前步骤文案；progress01 约 0~1。
    /// </summary>
    public static bool EnsureLoaded(Action<string, float> progress = null)
    {
        bool ok = false;
        IEnumerator steps = EnsureLoadedRoutine(progress, success => ok = success);
        while (steps.MoveNext()) { }
        return ok;
    }

    /// <summary>分帧加载，供 S_Loading 进度条显示真实进度。</summary>
    public static IEnumerator EnsureLoadedRoutine(Action<string, float> progress = null, Action<bool> onComplete = null)
    {
        if (IsLoaded)
        {
            Report(progress, "准备就绪", 1f);
            onComplete?.Invoke(true);
            yield break;
        }

        if (LoadError != null && loadAttempted)
        {
            Report(progress, "加载失败", 1f);
            onComplete?.Invoke(false);
            yield break;
        }

        IsLoading = true;
        loadAttempted = true;
        bool success = false;
        Exception failure = null;

        Report(progress, "准备内容目录…", 0.02f);
        yield return null;

        string baseRoot = Path.Combine(Application.streamingAssetsPath, "Content");
        string bundledPacks = Path.Combine(baseRoot, "Packs");
        string localPacks = ResolveGameDirectoryPacksRoot();
        string userPacks = Path.Combine(Application.persistentDataPath, "Content", "Packs");
        string settingsPath = Path.Combine(Application.persistentDataPath, "Content", "pack-settings.json");
        Debug.Log($"内容加载开始。引擎桩：{baseRoot}；内置包：{bundledPacks}；投放目录：{localPacks}");

        Report(progress, "读取扩展包开关…", 0.06f);
        yield return null;

        IReadOnlyCollection<string> disabled = null;
        bool isEditor = Application.isEditor;
        try
        {
            disabled = ContentPackLoader.ReadDisabledPackIds(settingsPath);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        ContentLoadResult pendingResult = null;
        if (failure == null)
        {
            Exception loadException = null;
            IEnumerator loadSteps = ContentPackLoader.CoLoad(
                baseRoot,
                new[]
                {
                    new ContentPackRoot(bundledPacks, !isEditor),
                    new ContentPackRoot(localPacks, isEditor),
                    new ContentPackRoot(userPacks, false)
                },
                disabled,
                (message, localProgress) =>
                {
                    Report(progress, message, Mathf.Lerp(0.08f, 0.78f, Mathf.Clamp01(localProgress)));
                },
                loaded => pendingResult = loaded,
                exception => loadException = exception);

            while (loadSteps.MoveNext())
                yield return loadSteps.Current;

            if (loadException != null)
                failure = loadException;
            else if (pendingResult == null)
                failure = new InvalidDataException("内容包加载未返回结果。");
            else
                loadResult = pendingResult;
        }

        if (failure == null)
        {
            Report(progress, "校验内容结构…", 0.82f);
            yield return null;
            try
            {
                ContentPackage package = loadResult.Package;
                ContentRuntimeCapabilityValidator.ValidateOrThrow(package);
                if (package.Cards.Count == 0)
                    throw new InvalidDataException("没有可用卡牌。玩家包需要 StreamingAssets/Content/Packs/lofe_core.lofepackage。");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }

        if (failure == null)
        {
            Report(progress, "构建运行时注册表…", 0.9f);
            yield return null;
            try
            {
                Registry = new ContentRegistry(loadResult.Package);
                LoadError = null;
                foreach (ContentLoadDiagnostic diagnostic in Diagnostics)
                    Debug.LogWarning($"内容包诊断 [{diagnostic.Severity}] {diagnostic.PackId}: {diagnostic.Message}");
                Debug.Log($"内容包加载成功：{Registry.Package.ContentVersion}，卡牌 {Registry.Cards.Count} 张，扩展包 {LoadedPacks.Count} 个，指纹 {ContentFingerprint}。投放目录：{localPacks}");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }

        if (failure == null)
        {
            Report(progress, "预热世界地图…", 0.96f);
            yield return null;
            try
            {
                _ = WorldCatalog.Default;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }

        if (failure == null)
        {
            Report(progress, "准备就绪", 1f);
            yield return null;
            success = true;
        }
        else
        {
            Registry = null;
            loadResult = null;
            LoadError = failure;
            Debug.LogError($"内容包加载失败，游戏内容入口已停用：{failure}");
            Report(progress, "加载失败", 1f);
            success = false;
        }

        IsLoading = false;
        onComplete?.Invoke(success);
    }

    private static void Report(Action<string, float> progress, string status, float value)
    {
        progress?.Invoke(status, Mathf.Clamp01(value));
    }

    /// <summary>
    /// 游戏或工程根目录下的 Content/Packs，供离线扩展包直接投放。
    /// 编辑器指向工程根；玩家构建指向 exe 所在目录。
    /// </summary>
    public static string ResolveGameDirectoryPacksRoot()
    {
        string parent = Directory.GetParent(Application.dataPath)?.FullName;
        return string.IsNullOrEmpty(parent)
            ? Path.Combine(Application.persistentDataPath, "Content", "Packs")
            : Path.Combine(parent, "Content", "Packs");
    }

    /// <summary>
    /// 与 Play Mode 相同的根目录组合：StreamingAssets 引擎桩 + 内置 Packs + 工程/安装目录 Packs。
    /// 供编辑器测试和构建门禁用，不改写运行时单例。
    /// </summary>
    public static ContentLoadResult LoadComposedSnapshot()
    {
        string baseRoot = Path.Combine(Application.streamingAssetsPath, "Content");
        return ContentPackLoader.Load(baseRoot, new[]
        {
            Path.Combine(baseRoot, "Packs"),
            ResolveGameDirectoryPacksRoot()
        });
    }

    /// <summary>
    /// 编辑器测试把合成结果绑到单例注册表，让 CombatantState 能读到发布的状态定义。
    /// Play Mode 由 S_Loading 调用 EnsureLoaded。
    /// </summary>
    public static ContentRegistry BindComposedSnapshotForEditorTests()
    {
        loadResult = LoadComposedSnapshot();
        ContentPackage package = loadResult.Package;
        ContentRuntimeCapabilityValidator.ValidateOrThrow(package);
        Registry = new ContentRegistry(package);
        LoadError = null;
        loadAttempted = true;
        return Registry;
    }

    /// <summary>返回覆盖或新增资源所对应的、已经校验过的外部文件路径。</summary>
    public static bool TryGetExternalAssetPath(string assetKey, out string path)
    {
        path = null;
        return loadResult != null && loadResult.TryGetExternalAssetPath(assetKey, out path);
    }
}
}
