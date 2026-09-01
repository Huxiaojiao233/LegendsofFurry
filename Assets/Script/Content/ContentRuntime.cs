using System;
using System.Collections.Generic;
using System.IO;
using LegendsOfFurry.Content.Contracts;
using UnityEngine;

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>
/// 在首个场景加载前读取已发布内容并向游戏系统提供统一只读注册表。
/// </summary>
public static class ContentRuntime
{
    public static ContentRegistry Registry { get; private set; }
    public static Exception LoadError { get; private set; }
    public static bool IsLoaded => Registry != null;
    private static ContentLoadResult loadResult;
    public static IReadOnlyList<ContentPackLoadInfo> LoadedPacks =>
        loadResult?.LoadedPacks ?? Array.Empty<ContentPackLoadInfo>();
    public static IReadOnlyList<ContentLoadDiagnostic> Diagnostics =>
        loadResult?.Diagnostics ?? Array.Empty<ContentLoadDiagnostic>();
    public static string ContentFingerprint => loadResult?.Fingerprint ?? string.Empty;

    /// <summary>
    /// 在场景加载前初始化数据库注册表；失败会保留异常，后续入口据此阻止进入战斗。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        try
        {
            string baseRoot = Path.Combine(Application.streamingAssetsPath, "Content");
            string bundledPacks = Path.Combine(baseRoot, "Packs");
            string localPacks = ResolveGameDirectoryPacksRoot();
            string userPacks = Path.Combine(Application.persistentDataPath, "Content", "Packs");
            string settingsPath = Path.Combine(Application.persistentDataPath, "Content", "pack-settings.json");
            IReadOnlyCollection<string> disabled = ContentPackLoader.ReadDisabledPackIds(settingsPath);
            loadResult = ContentPackLoader.Load(baseRoot, new[]
            {
                new ContentPackRoot(bundledPacks, true),
                new ContentPackRoot(localPacks, true),
                new ContentPackRoot(userPacks, false)
            }, disabled);
            ContentPackage package = loadResult.Package;
            ContentRuntimeCapabilityValidator.ValidateOrThrow(package);
            if (package.Cards.Count == 0)
                throw new InvalidDataException("没有可用卡牌。请把离线内容包放到游戏 Content/Packs 目录。");
            Registry = new ContentRegistry(package);
            LoadError = null;
            foreach (ContentLoadDiagnostic diagnostic in Diagnostics)
                Debug.LogWarning($"内容包诊断 [{diagnostic.Severity}] {diagnostic.PackId}: {diagnostic.Message}");
            Debug.Log($"内容包加载成功：{Registry.Package.ContentVersion}，卡牌 {Registry.Cards.Count} 张，扩展包 {LoadedPacks.Count} 个，指纹 {ContentFingerprint}。投放目录：{localPacks}");
        }
        catch (Exception exception)
        {
            Registry = null;
            loadResult = null;
            LoadError = exception;
            Debug.LogError($"内容包加载失败，游戏内容入口已停用：{exception}");
        }
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

    /// <summary>返回覆盖或新增资源所对应的、已经校验过的外部文件路径。</summary>
    public static bool TryGetExternalAssetPath(string assetKey, out string path)
    {
        path = null;
        return loadResult != null && loadResult.TryGetExternalAssetPath(assetKey, out path);
    }
}
}
