#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 在编辑器手动检查和正式构建前验证当前数据库发布包，阻止缺失或不可执行内容进入玩家版本。
/// </summary>
public sealed class ContentBuildValidator : IPreprocessBuildWithReport
{
    public int callbackOrder => -1000;

    /// <summary>
    /// Unity 开始构建时加载 current 指向的内容包，并把任何错误提升为构建失败。
    /// </summary>
    /// <param name="report">Unity 当前构建报告。</param>
    public void OnPreprocessBuild(BuildReport report)
    {
        try
        {
            ValidatePublishedContent();
        }
        catch (Exception exception)
        {
            throw new BuildFailedException($"数据库内容构建前检查失败：{exception.Message}");
        }
    }

    /// <summary>
    /// 从项目 StreamingAssets 读取并完整验证当前发布包、运行时能力和关键内容数量。
    /// </summary>
    /// <returns>已经通过全部构建门禁的内容包。</returns>
    public static ContentPackage ValidatePublishedContent()
    {
        string contentRoot = Path.Combine(Application.dataPath, "StreamingAssets", "Content");
        ContentPackage package = ContentPackageLoader.LoadFromDirectory(contentRoot);
        ContentRuntimeCapabilityValidator.ValidateOrThrow(package);
        if (package.Cards.Count == 0) throw new InvalidDataException("内容包没有可用卡牌。");
        if (!package.Cards.Any(card => card.Enabled)) throw new InvalidDataException("内容包没有已启用卡牌。");
        if (package.ClassProfiles.Count == 0 || package.ClassProfiles.Any(profile => profile.DeckRecipe.Count == 0))
            throw new InvalidDataException("职业资料缺失，或存在没有初始牌库配方的职业。");
        Debug.Log($"数据库内容构建检查通过：{package.ContentVersion}，{package.Cards.Count} 张卡牌。");
        return package;
    }

    /// <summary>
    /// 从 Unity 菜单执行与正式构建完全相同的数据库内容门禁。
    /// </summary>
    [MenuItem("Tools/Legends Of Furry/验证数据库内容包")]
    private static void ValidateFromMenu()
    {
        ValidatePublishedContent();
    }
}
#endif
