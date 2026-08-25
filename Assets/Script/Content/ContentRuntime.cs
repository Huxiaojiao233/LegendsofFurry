using System;
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

    /// <summary>
    /// 在场景加载前初始化数据库注册表；失败会保留异常，后续入口据此阻止进入战斗。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        try
        {
            ContentPackage package = ContentPackageLoader.LoadFromStreamingAssets();
            ContentRuntimeCapabilityValidator.ValidateOrThrow(package);
            Registry = new ContentRegistry(package);
            LoadError = null;
            Debug.Log($"内容包加载成功：{Registry.Package.ContentVersion}，卡牌 {Registry.Cards.Count} 张。");
        }
        catch (Exception exception)
        {
            Registry = null;
            LoadError = exception;
            Debug.LogError($"内容包加载失败，游戏内容入口已停用：{exception}");
        }
    }
}
}
